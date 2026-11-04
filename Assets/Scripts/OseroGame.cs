// 立体オセロ（8×8×8）の画面と操作。シーンを再生すると自動で起動する（シーンに置く必要なし）。
// PC（マウス・横画面）とスマホのブラウザ（タッチ・縦画面）の両方で動かす。
// Web（WebGL）で使えない機能（別スレッド、PC のフォント）は使わない。
//
// 画面: 真ん中に 3D の盤。左のパネルに石の数・手番・ボタン。右のパネルに「見ている層」を平面にした 8×8 の盤。
//       画面が縦長（スマホの縦画面）なら、上に 3D の盤、下に 2 つのパネルを並べる。
// 操作:
//   ・置ける場所は、どの層でも半透明の青い立方体で出る。それを押すと石を置く（右の平面の盤の青いマスを押してもよい）
//   ・見ている層は ▼▲（PC は W/S・↑/↓ キーでも）で変える。3D の盤ではその層に点線のマス目が出る
//   ・「表示」ボタンで、3D の盤を「全部の層」と「見ている層だけ」で切り替える
//   ・ドラッグで回転、ホイール（スマホは 2 本指）で拡大・縮小
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;
#endif

public class OseroGame : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<OseroGame>() != null) return;
        new GameObject("OseroGame").AddComponent<OseroGame>();
    }

    const int N = OseroBoard.N;

    // ---- 色と大きさ ----
    static readonly Color Background = new Color(0.95f, 0.95f, 0.97f);
    static readonly Color BlackColor = new Color(0.12f, 0.12f, 0.13f);
    static readonly Color WhiteColor = new Color(0.98f, 0.98f, 0.96f);
    static readonly Color BlackGhostColor = new Color(0.1f, 0.1f, 0.1f, 0.35f);
    static readonly Color WhiteGhostColor = new Color(0.75f, 0.75f, 0.80f, 0.45f);
    static readonly Color FrameColor = new Color(0.62f, 0.62f, 0.68f);
    static readonly Color GridColor = new Color(0.35f, 0.35f, 0.40f);
    static readonly Color HintColor = new Color(0.35f, 0.55f, 1f, 0.28f);       // 置ける場所（半透明の青）
    static readonly Color HintHoverColor = new Color(0.35f, 0.55f, 1f, 0.60f);  // マウスを乗せた置ける場所
    static readonly Color LastColor = new Color(0.9f, 0.2f, 0.2f);
    static readonly Color PanelColor = new Color(1f, 0.98f, 0.94f, 0.92f);
    static readonly Color ButtonColor = new Color(0.95f, 0.84f, 0.58f);
    static readonly Color TextColor = new Color(0.15f, 0.15f, 0.18f);
    static readonly Color MessageColor = new Color(0.75f, 0.25f, 0.10f);
    static readonly Color BoardGreen = new Color(0.20f, 0.56f, 0.33f);           // 右の平面の盤のマス
    static readonly Color BoardLine = new Color(0.08f, 0.25f, 0.14f);
    static readonly Color BoardLegal = new Color(0.62f, 0.76f, 1f);              // 右の平面の盤の置けるマス
    const float HintSize = 0.86f;      // 置ける場所の立方体の大きさ（マス 1 に対して）
    const float StoneScale = 0.6f;     // 石の大きさ
    const float FocusScale = 0.8f;     // 「見ている層だけ」のときの、その層の石
    const float OtherScale = 0.3f;     // 「見ている層だけ」のときの、ほかの層の石（半透明）
    const float BoardCell = 52f;       // 右の平面の盤の 1 マスの大きさ（UI の単位）

    readonly OseroBoard board = new OseroBoard();
    readonly List<int> flipBuf = new List<int>();
    int viewLayer = 3;      // 見ている層（0〜7。画面では 1〜8 層）
    bool focusOnly;         // 3D の盤で、見ている層だけをはっきり出す
    int lastMove = -1;
    readonly float[] changedAt = new float[OseroBoard.CellCount]; // 石が置かれた・裏返った時刻（ポンと動かす）

    // ---- 3D の物 ----
    Camera cam;
    Material matBlack, matWhite, matBlackGhost, matWhiteGhost, matHint, matHintHover, matGrid, matFrame,
             matHoverBlack, matHoverWhite, matLast;
    Mesh sphereMesh, cubeMesh;
    Transform boardRoot;
    readonly Transform[] stoneObj = new Transform[OseroBoard.CellCount];
    readonly MeshRenderer[] stoneRend = new MeshRenderer[OseroBoard.CellCount];
    Transform layerGrid;           // 見ている層の点線のマス目
    readonly List<Transform> hints = new List<Transform>();      // 置ける場所の立方体（使い回す）
    readonly List<MeshRenderer> hintRends = new List<MeshRenderer>();
    readonly List<int> hintCells = new List<int>();             // 今出している立方体のマスの番号
    int hoveredHint = -1;
    Transform hover; MeshRenderer hoverRend;
    Transform lastMark;

    // ---- カメラ ----
    float yaw = 30f, pitch = 35f, zoom = 1f;

    // ---- UI ----
    Font font;
    CanvasScaler scaler;
    RectTransform leftPanel, rightPanel;
    int layoutPortrait = -1;   // 今の並べ方（1 = 縦画面、0 = 横画面、-1 = まだ）
    Text statusText, messageText, helpText, newGameText, layerText, focusText, boardTitle, boardInfo;
    readonly Image[] cellImg = new Image[N * N];     // 右の平面の盤のマス（[z * N + x]）
    readonly Image[] cellStone = new Image[N * N];
    readonly Image[] cellLast = new Image[N * N];
    Sprite circleSprite;
    float messageUntil;
    float restartConfirmUntil;

    // =====================================================================
    void Start()
    {
        Application.targetFrameRate = 60;
        for (int i = 0; i < changedAt.Length; i++) changedAt[i] = -10f;

        cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 200f;

        // 縦画面では 3D の盤を画面の上だけに描くので、残りの所を背景色で塗るカメラ（何も映さない）
        var bgCam = new GameObject("Background Camera").AddComponent<Camera>();
        bgCam.clearFlags = CameraClearFlags.SolidColor;
        bgCam.backgroundColor = Background;
        bgCam.cullingMask = 0;
        bgCam.depth = cam.depth - 10;

        MakeMaterials();
        MakeMeshes();
        BuildBoard();
        BuildUI();

#if ENABLE_INPUT_SYSTEM
        ETouch.EnhancedTouchSupport.Enable();
#endif
        Refresh();
    }

    // ---- マテリアル（色） ----
    void MakeMaterials()
    {
        Shader solid = Shader.Find("Osero3D/Solid");
        Shader ghost = Shader.Find("Osero3D/Ghost");
        Shader glass = Shader.Find("Osero3D/Glass");
        if (solid == null) Debug.LogError("シェーダー Osero3D/Solid が見つからない（Assets/Resources/OseroSolid.shader）");
        if (ghost == null) Debug.LogError("シェーダー Osero3D/Ghost が見つからない（Assets/Resources/OseroGhost.shader）");
        if (glass == null) { Debug.LogError("シェーダー Osero3D/Glass が見つからない（Assets/Resources/OseroGlass.shader）"); glass = ghost; }
        matBlack = M(solid, BlackColor);
        matWhite = M(solid, WhiteColor);
        matBlackGhost = M(ghost, BlackGhostColor);
        matWhiteGhost = M(ghost, WhiteGhostColor);
        matHoverBlack = M(ghost, new Color(0.1f, 0.1f, 0.1f, 0.5f));
        matHoverWhite = M(ghost, new Color(0.85f, 0.85f, 0.9f, 0.7f));
        matHint = M(glass, HintColor);
        matHintHover = M(glass, HintHoverColor);
        matGrid = M(solid, GridColor);
        matFrame = M(solid, FrameColor);
        matLast = M(solid, LastColor);
    }

    static Material M(Shader s, Color c)
    {
        var m = new Material(s != null ? s : Shader.Find("Sprites/Default"));
        m.SetColor("_BaseColor", c);
        return m;
    }

    void MakeMeshes()
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = s.GetComponent<MeshFilter>().sharedMesh;
        Destroy(s);
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = c.GetComponent<MeshFilter>().sharedMesh;
        Destroy(c);
    }

    Transform MakeObj(string name, Mesh mesh, Material mat, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    // マス (x,y,z) の中心の位置。盤全体の真ん中が (0,0,0)。y が層（0 が一番下）
    static Vector3 CellPos(int x, int y, int z) => new Vector3(x - 3.5f, y - 3.5f, z - 3.5f);
    static float LayerY(int y) => y - 3.5f;

    void BuildBoard()
    {
        boardRoot = new GameObject("Board").transform;

        // 外枠（立方体の 12 本の辺）
        const float h = N / 2f, t = 0.035f;
        for (int a = 0; a < 3; a++)
            for (int s1 = -1; s1 <= 1; s1 += 2)
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    var e = MakeObj("Frame", cubeMesh, matFrame, boardRoot);
                    Vector3 p = Vector3.zero, sc = Vector3.one * t;
                    if (a == 0) { p = new Vector3(0, s1 * h, s2 * h); sc.x = N + t; }
                    if (a == 1) { p = new Vector3(s1 * h, 0, s2 * h); sc.y = N + t; }
                    if (a == 2) { p = new Vector3(s1 * h, s2 * h, 0); sc.z = N + t; }
                    e.localPosition = p; e.localScale = sc;
                }

        // 見ている層の点線のマス目（層の底の高さに引く）
        layerGrid = new GameObject("LayerGrid").transform;
        layerGrid.SetParent(boardRoot, false);
        const float dash = 0.3f, gap = 0.2f, lt = 0.025f;
        int dashes = Mathf.RoundToInt(N / (dash + gap));
        for (int i = 0; i <= N; i++)
        {
            float p = i - h;
            for (int d = 0; d < dashes; d++)
            {
                float q = -h + d * (dash + gap) + dash / 2f + gap / 2f;
                var lx = MakeObj("Dash", cubeMesh, matGrid, layerGrid);
                lx.localPosition = new Vector3(q, -0.5f, p); lx.localScale = new Vector3(dash, lt, lt);
                var lz = MakeObj("Dash", cubeMesh, matGrid, layerGrid);
                lz.localPosition = new Vector3(p, -0.5f, q); lz.localScale = new Vector3(lt, lt, dash);
            }
        }

        // 石（全部のマスに 1 つずつ作っておき、空きのマスでは隠す）
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            OseroBoard.FromIndex(i, out int x, out int y, out int z);
            var st = MakeObj("Stone", sphereMesh, matBlack, boardRoot);
            st.localPosition = CellPos(x, y, z);
            stoneObj[i] = st;
            stoneRend[i] = st.GetComponent<MeshRenderer>();
            st.gameObject.SetActive(false);
        }

        // 置ける場所の立方体は Refresh で必要な数だけ作る

        // マウスを乗せた置ける場所に出す、うすい石
        hover = MakeObj("Hover", sphereMesh, matHoverBlack, boardRoot);
        hover.localScale = Vector3.one * StoneScale;
        hoverRend = hover.GetComponent<MeshRenderer>();
        hover.gameObject.SetActive(false);

        // 最後に置いた石の印（石の上に乗せる小さな赤い点）
        lastMark = MakeObj("LastMove", sphereMesh, matLast, boardRoot);
        lastMark.gameObject.SetActive(false);
    }

    // =====================================================================
    // UI
    void BuildUI()
    {
        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0] : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        circleSprite = MakeCircleSprite(64);

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        var canvasGo = new GameObject("UI");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 左のパネル
        var left = MakePanel(canvasGo.transform, 420f);
        leftPanel = (RectTransform)left;
        MakeText(left, "立体オセロ", 34, TextAnchor.MiddleLeft, 44);
        statusText = MakeText(left, "", 22, TextAnchor.UpperLeft, 64);
        messageText = MakeText(left, "", 20, TextAnchor.UpperLeft, 52);
        messageText.color = MessageColor;
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        newGameText = MakeButton(MakeRow(left), "新しい対局", 160, OnNewGame);
        var row = MakeRow(left);
        MakeButton(row, "▼", 50, () => ChangeLayer(-1));
        layerText = MakeText(row, "", 20, TextAnchor.MiddleCenter, 40, 170);
        MakeButton(row, "▲", 50, () => ChangeLayer(1));
        focusText = MakeButton(MakeRow(left), "", 388, () => { focusOnly = !focusOnly; Refresh(); });
        helpText = MakeText(left, "", 17, TextAnchor.UpperLeft, 72);

        // 右のパネル（見ている層を平面にした盤）
        var right = MakePanel(canvasGo.transform, BoardCell * N + 32);
        rightPanel = (RectTransform)right;
        var titleRow = MakeRow(right);
        MakeButton(titleRow, "▼", 50, () => ChangeLayer(-1));
        boardTitle = MakeText(titleRow, "", 24, TextAnchor.MiddleCenter, 40, BoardCell * N - 116);
        MakeButton(titleRow, "▲", 50, () => ChangeLayer(1));
        BuildFlatBoard(right);
        boardInfo = MakeText(right, "", 18, TextAnchor.MiddleLeft, 28);
    }

    // 右のパネルの 8×8 の盤。上が奥（z = 7）、下が手前（z = 0）。左から x = 0〜7
    void BuildFlatBoard(Transform parent)
    {
        var go = new GameObject("FlatBoard", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = BoardLine;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = BoardCell * N + 4;
        le.preferredHeight = le.minHeight = BoardCell * N + 4;

        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int row = N - 1 - z;
                var c = new GameObject("Cell", typeof(RectTransform));
                c.transform.SetParent(go.transform, false);
                var rt = (RectTransform)c.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(2 + x * BoardCell + 1, -(2 + row * BoardCell + 1));
                rt.sizeDelta = new Vector2(BoardCell - 2, BoardCell - 2);
                var img = c.AddComponent<Image>();
                img.color = BoardGreen;
                var b = c.AddComponent<Button>();
                b.targetGraphic = img;
                b.transition = Selectable.Transition.None;
                int cx = x, cz = z;
                b.onClick.AddListener(() => TryPlace(OseroBoard.Index(cx, viewLayer, cz)));

                var st = new GameObject("Stone", typeof(RectTransform));
                st.transform.SetParent(c.transform, false);
                var srt = (RectTransform)st.transform;
                srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
                srt.offsetMin = new Vector2(5, 5); srt.offsetMax = new Vector2(-5, -5);
                var simg = st.AddComponent<Image>();
                simg.sprite = circleSprite;
                simg.raycastTarget = false;

                var lm = new GameObject("Last", typeof(RectTransform));
                lm.transform.SetParent(c.transform, false);
                var lrt = (RectTransform)lm.transform;
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(12, 12);
                var limg = lm.AddComponent<Image>();
                limg.sprite = circleSprite;
                limg.color = LastColor;
                limg.raycastTarget = false;

                cellImg[z * N + x] = img;
                cellStone[z * N + x] = simg;
                cellLast[z * N + x] = limg;
            }
    }

    // 丸い絵（石・印に使う）。ふちをなめらかにする
    static Sprite MakeCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float r = size / 2f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    Transform MakePanel(Transform parent, float width)
    {
        var go = new GameObject("Panel", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(width, 0);
        go.AddComponent<Image>().color = PanelColor;
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(14, 14, 12, 12);
        v.spacing = 8;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go.transform;
    }

    Transform MakeRow(Transform parent)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        go.AddComponent<LayoutElement>().preferredHeight = 40;
        return go.transform;
    }

    Text MakeText(Transform parent, string s, int size, TextAnchor anchor, float height, float width = -1)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = TextColor;
        t.text = s;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = height;
        if (width > 0) le.preferredWidth = le.minWidth = width;
        else le.flexibleWidth = 1;
        return t;
    }

    // ボタンを作り、その文字（Text）を返す
    Text MakeButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = ButtonColor;
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = width;
        le.preferredHeight = le.minHeight = 40;

        var tg = new GameObject("Label", typeof(RectTransform));
        tg.transform.SetParent(go.transform, false);
        var t = tg.AddComponent<Text>();
        t.font = font;
        t.fontSize = 20;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = TextColor;
        t.text = label;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    // ---- 縦画面・横画面の並べ方 ----
    void ApplyLayout(bool portrait)
    {
        layoutPortrait = portrait ? 1 : 0;
        if (portrait)
        {
            // 縦画面: 上に 3D の盤、下に左右 2 つのパネル。操作の説明は場所が無いので隠す
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;
            SetPanel(leftPanel, new Vector2(0, 0), new Vector2(12, 12), 500f);
            SetPanel(rightPanel, new Vector2(1, 0), new Vector2(-12, 12), BoardCell * N + 32);
            cam.rect = new Rect(0f, 0.36f, 1f, 0.64f);
        }
        else
        {
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            SetPanel(leftPanel, new Vector2(0, 1), new Vector2(16, -16), 420f);
            SetPanel(rightPanel, new Vector2(1, 1), new Vector2(-16, -16), BoardCell * N + 32);
            cam.rect = new Rect(0f, 0f, 1f, 1f);
        }
        helpText.gameObject.SetActive(!portrait);
    }

    static void SetPanel(RectTransform rt, Vector2 corner, Vector2 offset, float width)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = corner;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
    }

    void ShowMessage(string s, float seconds)
    {
        messageText.text = s;
        messageUntil = seconds > 0 ? Time.unscaledTime + seconds : float.MaxValue;
    }

    void OnNewGame()
    {
        // 対局の途中は 2 回押したときだけ最初からにする（押し間違い防止）
        if (!board.GameOver && Time.unscaledTime > restartConfirmUntil)
        {
            restartConfirmUntil = Time.unscaledTime + 3f;
            return;
        }
        restartConfirmUntil = 0;
        board.Reset();
        lastMove = -1;
        viewLayer = 3;
        for (int i = 0; i < changedAt.Length; i++) changedAt[i] = -10f;
        ShowMessage("", 0);
        Refresh();
    }

    void ChangeLayer(int d)
    {
        viewLayer = Mathf.Clamp(viewLayer + d, 0, N - 1);
        Refresh();
    }

    // =====================================================================
    // 石を置く
    void TryPlace(int cell)
    {
        if (board.GameOver) return;
        Stone mover = board.Turn;
        OseroBoard.FromIndex(cell, out int x, out int y, out int z);
        if (!board.Play(x, y, z, flipBuf, out var after)) return;

        float now = Time.unscaledTime;
        changedAt[cell] = now;
        foreach (int i in flipBuf) changedAt[i] = now;
        lastMove = cell;

        string moverName = mover == Stone.Black ? "黒" : "白";
        string otherName = mover == Stone.Black ? "白" : "黒";
        if (after == OseroBoard.After.Passed)
            ShowMessage(otherName + "は置ける所が無いのでパス。もう一度" + moverName + "の番", 4f);
        else if (after == OseroBoard.After.GameOver)
        {
            int b = board.Count(Stone.Black), w = board.Count(Stone.White);
            string result = b > w ? "黒の勝ち！" : w > b ? "白の勝ち！" : "引き分け";
            ShowMessage("終わり　" + result + "（黒 " + b + " 対 白 " + w + "）", 0);
        }
        else
            ShowMessage(moverName + "が " + (y + 1) + "層に置いて " + flipBuf.Count + " 個裏返した", 3f);
        Refresh();
    }

    // 盤・UI の表示を今の状態に合わせる
    void Refresh()
    {
        layerGrid.localPosition = new Vector3(0, LayerY(viewLayer), 0);

        // 置ける場所の立方体（全部の層、または見ている層だけ）と、層ごとの置ける数
        SetHover(-1);
        hintCells.Clear();
        int[] perLayer = new int[N];
        int total = 0;
        bool[] legal = new bool[OseroBoard.CellCount];
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            OseroBoard.FromIndex(i, out int x, out int y, out int z);
            if (board.GameOver || !board.IsLegal(x, y, z, board.Turn)) continue;
            legal[i] = true;
            perLayer[y]++;
            total++;
            if (!focusOnly || y == viewLayer) hintCells.Add(i);
        }
        while (hints.Count < hintCells.Count)
        {
            var hn = MakeObj("Hint", cubeMesh, matHint, boardRoot);
            hn.localScale = Vector3.one * HintSize;
            hints.Add(hn);
            hintRends.Add(hn.GetComponent<MeshRenderer>());
        }
        for (int k = 0; k < hints.Count; k++)
        {
            bool on = k < hintCells.Count;
            hints[k].gameObject.SetActive(on);
            if (!on) continue;
            OseroBoard.FromIndex(hintCells[k], out int x, out int y, out int z);
            hints[k].localPosition = CellPos(x, y, z);
            hintRends[k].sharedMaterial = matHint;
        }

        // 右の平面の盤
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int i = OseroBoard.Index(x, viewLayer, z);
                Stone s = board.Get(i);
                cellImg[z * N + x].color = legal[i] ? BoardLegal : BoardGreen;
                cellStone[z * N + x].enabled = s != Stone.Empty;
                cellStone[z * N + x].color = s == Stone.Black ? BlackColor : WhiteColor;
                cellLast[z * N + x].enabled = i == lastMove;
            }
        boardTitle.text = (viewLayer + 1) + "層の盤面";
        boardInfo.text = board.GameOver ? "" : "この層に置ける所: " + perLayer[viewLayer] + " か所（青いマス）";

        int b = board.Count(Stone.Black), w = board.Count(Stone.White);
        string turn = board.GameOver ? "対局終了" : (board.Turn == Stone.Black ? "● 黒の番です" : "○ 白の番です");
        statusText.text = turn + (board.GameOver ? "" : "（置ける所 " + total + " か所）")
            + "\n● 黒 " + b + "　○ 白 " + w;
        layerText.text = "見ている層: " + (viewLayer + 1) + "層";
        focusText.text = focusOnly ? "表示: 見ている層だけ" : "表示: 全部の層";

        UpdateStones();
    }

    // 石の色・大きさ（毎フレーム。置いた・裏返った石をポンと動かす）
    void UpdateStones()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            Stone s = board.Get(i);
            var go = stoneObj[i].gameObject;
            if (s == Stone.Empty) { if (go.activeSelf) go.SetActive(false); continue; }
            if (!go.activeSelf) go.SetActive(true);

            OseroBoard.FromIndex(i, out _, out int y, out _);
            Material m;
            float scale;
            if (!focusOnly) { m = s == Stone.Black ? matBlack : matWhite; scale = StoneScale; }
            else if (y == viewLayer) { m = s == Stone.Black ? matBlack : matWhite; scale = FocusScale; }
            else { m = s == Stone.Black ? matBlackGhost : matWhiteGhost; scale = OtherScale; }

            float t = now - changedAt[i];
            if (t >= 0 && t < 0.35f) scale *= 1f + 0.35f * Mathf.Sin(t / 0.35f * Mathf.PI);

            if (stoneRend[i].sharedMaterial != m) stoneRend[i].sharedMaterial = m;
            stoneObj[i].localScale = Vector3.one * scale;
        }

        if (lastMove >= 0)
        {
            OseroBoard.FromIndex(lastMove, out int x, out int y, out int z);
            float sc = !focusOnly ? StoneScale : y == viewLayer ? FocusScale : OtherScale;
            lastMark.gameObject.SetActive(true);
            lastMark.localPosition = CellPos(x, y, z) + Vector3.up * (sc * 0.5f + 0.04f);
            lastMark.localScale = Vector3.one * Mathf.Max(0.1f, sc * 0.22f);
        }
        else lastMark.gameObject.SetActive(false);
    }

    // =====================================================================
    // 入力（マウス・タッチ・キー）
    bool pointerDown;          // 1 本指（マウス）で押している
    bool dragging;
    Vector2 pressPos, lastPos;
    float lastPinchDist = -1f;
    bool multiTouched;         // 今回押している間に 2 本指になった（指を離しても置かない）
    float lastTouchTime = -10f;

    void Update()
    {
        bool portrait = Screen.height > Screen.width;
        if ((portrait ? 1 : 0) != layoutPortrait) ApplyLayout(portrait);

        float now = Time.unscaledTime;
        if (now > messageUntil && messageText.text != "") messageText.text = "";
        newGameText.text = (!board.GameOver && now < restartConfirmUntil) ? "もう一度押す" : "新しい対局";

        bool touchUsed = HandleTouch();
        if (!touchUsed && now - lastTouchTime > 0.5f) HandleMouse();
        HandleKeys();

        UpdateStones();
        helpText.text = now - lastTouchTime < 5f
            ? "青い立方体を押す: 置く\n1本指: 回す　2本指: 拡大縮小"
            : "左クリック（青い立方体）: 置く\nドラッグ: 回す　ホイール: 拡大縮小\n↑↓キー: 見る層を変える";
    }

    void LateUpdate()
    {
        // 盤全体（半径約 7）が画面に収まるように距離を決める。
        // 横画面では左右にパネルがあるので、その間の幅に収める
        float vfov = cam.fieldOfView * Mathf.Deg2Rad;
        float usable = 1f;
        if (layoutPortrait == 0 && Screen.width > 0)
        {
            var corners = new Vector3[4];
            leftPanel.GetWorldCorners(corners);
            float l = corners[2].x / Screen.width;
            rightPanel.GetWorldCorners(corners);
            float r = 1f - corners[0].x / Screen.width;
            usable = Mathf.Clamp(1f - 2f * Mathf.Max(l, r), 0.3f, 1f);
        }
        float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov / 2f) * cam.aspect * usable);
        float fov = Mathf.Min(vfov, hfov);
        float dist = 7.2f / Mathf.Sin(fov / 2f) * zoom;
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.position = rot * new Vector3(0, 0, -dist);
        cam.transform.rotation = rot;
    }

    void Rotate(Vector2 delta)
    {
        float k = 180f / Mathf.Max(400f, Mathf.Min(Screen.width, Screen.height));
        yaw += delta.x * k;
        pitch = Mathf.Clamp(pitch - delta.y * k, -85f, 85f);
    }

    void Zoom(float factor) => zoom = Mathf.Clamp(zoom * factor, 0.35f, 1.6f);

    float DragThreshold => Mathf.Max(8f, Mathf.Min(Screen.width, Screen.height) * 0.015f);

    bool OverUI(Vector2 screenPos)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(leftPanel, screenPos, null)
            || RectTransformUtility.RectangleContainsScreenPoint(rightPanel, screenPos, null);
    }

    // 押し始め・動かす・離す（マウス 1 つ、またはタッチ 1 本）
    void PointerBegin(Vector2 p)
    {
        if (OverUI(p)) { pointerDown = false; return; }
        pointerDown = true; dragging = false; multiTouched = false;
        pressPos = lastPos = p;
    }

    void PointerMove(Vector2 p)
    {
        if (!pointerDown) return;
        if (!dragging && (p - pressPos).magnitude > DragThreshold) dragging = true;
        if (dragging) Rotate(p - lastPos);
        lastPos = p;
    }

    void PointerEnd(Vector2 p)
    {
        if (!pointerDown) return;
        pointerDown = false;
        if (!dragging && !multiTouched)
        {
            int k = PickHint(p);
            if (k >= 0) TryPlace(hintCells[k]);
        }
    }

    // 画面の点 → 押した「置ける場所の立方体」の番号（hintCells の何番目か）。無ければ -1。
    // 視線が通る立方体のうち、一番手前のものを選ぶ（石は小さいので、石の後ろの立方体も押せる）
    int PickHint(Vector2 screenPos)
    {
        if (board.GameOver || OverUI(screenPos)) return -1;
        if (!cam.pixelRect.Contains(screenPos)) return -1;
        Ray ray = cam.ScreenPointToRay(screenPos);
        float half = HintSize / 2f;
        int best = -1;
        float bestT = float.MaxValue;
        for (int k = 0; k < hintCells.Count; k++)
        {
            OseroBoard.FromIndex(hintCells[k], out int x, out int y, out int z);
            Vector3 c = CellPos(x, y, z);
            if (RayBox(ray, c - Vector3.one * half, c + Vector3.one * half, out float t) && t < bestT)
            {
                bestT = t;
                best = k;
            }
        }
        return best;
    }

    // 視線（ray）が箱（min〜max）に当たるか。当たったら手前の距離 t
    static bool RayBox(Ray ray, Vector3 min, Vector3 max, out float t)
    {
        float tMin = 0f, tMax = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float o = ray.origin[a], d = ray.direction[a];
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (o < min[a] || o > max[a]) { t = 0; return false; }
                continue;
            }
            float t1 = (min[a] - o) / d, t2 = (max[a] - o) / d;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin;
        return true;
    }

    // マウスを乗せた立方体を濃くして、中にうすい石を出す
    void SetHover(int k)
    {
        if (hoveredHint == k) return;
        if (hoveredHint >= 0 && hoveredHint < hintRends.Count) hintRends[hoveredHint].sharedMaterial = matHint;
        hoveredHint = k;
        if (k < 0) { hover.gameObject.SetActive(false); return; }
        hintRends[k].sharedMaterial = matHintHover;
        OseroBoard.FromIndex(hintCells[k], out int x, out int y, out int z);
        hover.gameObject.SetActive(true);
        hover.localPosition = CellPos(x, y, z);
        hover.localScale = Vector3.one * (focusOnly ? FocusScale : StoneScale);
        hoverRend.sharedMaterial = board.Turn == Stone.Black ? matHoverBlack : matHoverWhite;
    }

    void UpdateHover(Vector2 screenPos, bool valid)
    {
        SetHover(valid ? PickHint(screenPos) : -1);
    }

#if ENABLE_INPUT_SYSTEM
    // ---- 新しい Input System ----
    bool HandleTouch()
    {
        var touches = ETouch.Touch.activeTouches;
        if (touches.Count == 0) { lastPinchDist = -1f; return false; }
        lastTouchTime = Time.unscaledTime;
        SetHover(-1);

        if (touches.Count >= 2)
        {
            multiTouched = true;
            float d = (touches[0].screenPosition - touches[1].screenPosition).magnitude;
            if (lastPinchDist > 0 && d > 1f) Zoom(lastPinchDist / d);
            lastPinchDist = d;
            return true;
        }
        lastPinchDist = -1f;

        var tc = touches[0];
        if (multiTouched && pointerDown) { lastPos = tc.screenPosition; dragging = true; multiTouched = false; } // 2 本指から 1 本に戻ったとき、急に回らないように
        switch (tc.phase)
        {
            case UnityEngine.InputSystem.TouchPhase.Began: PointerBegin(tc.screenPosition); break;
            case UnityEngine.InputSystem.TouchPhase.Moved: PointerMove(tc.screenPosition); break;
            case UnityEngine.InputSystem.TouchPhase.Ended: PointerEnd(tc.screenPosition); break;
            case UnityEngine.InputSystem.TouchPhase.Canceled: pointerDown = false; break;
        }
        return true;
    }

    void HandleMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 p = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame) PointerBegin(p);
        else if (mouse.leftButton.isPressed) PointerMove(p);
        if (mouse.leftButton.wasReleasedThisFrame) PointerEnd(p);
        float s = mouse.scroll.ReadValue().y;
        if (s != 0 && !OverUI(p)) Zoom(s > 0 ? 0.9f : 1.1f);
        UpdateHover(p, !mouse.leftButton.isPressed);
    }

    void HandleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) ChangeLayer(1);
        if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) ChangeLayer(-1);
    }
#else
    // ---- 古い Input Manager ----
    bool HandleTouch()
    {
        if (Input.touchCount == 0) { lastPinchDist = -1f; return false; }
        lastTouchTime = Time.unscaledTime;
        SetHover(-1);

        if (Input.touchCount >= 2)
        {
            multiTouched = true;
            float d = (Input.GetTouch(0).position - Input.GetTouch(1).position).magnitude;
            if (lastPinchDist > 0 && d > 1f) Zoom(lastPinchDist / d);
            lastPinchDist = d;
            return true;
        }
        lastPinchDist = -1f;

        var tc = Input.GetTouch(0);
        if (multiTouched && pointerDown) { lastPos = tc.position; dragging = true; multiTouched = false; } // 2 本指から 1 本に戻ったとき、急に回らないように
        switch (tc.phase)
        {
            case TouchPhase.Began: PointerBegin(tc.position); break;
            case TouchPhase.Moved: PointerMove(tc.position); break;
            case TouchPhase.Ended: PointerEnd(tc.position); break;
            case TouchPhase.Canceled: pointerDown = false; break;
        }
        return true;
    }

    void HandleMouse()
    {
        Vector2 p = Input.mousePosition;
        if (Input.GetMouseButtonDown(0)) PointerBegin(p);
        else if (Input.GetMouseButton(0)) PointerMove(p);
        if (Input.GetMouseButtonUp(0)) PointerEnd(p);
        float s = Input.mouseScrollDelta.y;
        if (s != 0 && !OverUI(p)) Zoom(s > 0 ? 0.9f : 1.1f);
        UpdateHover(p, !Input.GetMouseButton(0));
    }

    void HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) ChangeLayer(1);
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) ChangeLayer(-1);
    }
#endif
}
