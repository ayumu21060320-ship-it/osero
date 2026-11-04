// 立体オセロ（8×8×8）の画面と操作。シーンを再生すると自動で起動する（シーンに置く必要なし）。
// PC（マウス・横画面）とスマホのブラウザ（タッチ・縦画面）の両方で動かす。
// Web（WebGL）で使えない機能（別スレッド、PC のフォント）は使わない。
//
// 操作:
//   ・置ける場所は、どの段（高さ）でも薄い青の四角（立方体）で出る。それを押すと石を置く
//   ・中が見えにくいときは、右の 1〜8 のボタン（PC は W/S・↑/↓ キーでも）で段を 1 つに絞れる。「全部」で戻す
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

    // ---- 見た目の設定 ----
    static readonly Color BgColor = new Color(0.10f, 0.13f, 0.17f);
    static readonly Color BlackColor = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color WhiteColor = new Color(0.93f, 0.93f, 0.90f);
    static readonly Color LineColor = new Color(0.05f, 0.25f, 0.12f);
    static readonly Color PlateColor = new Color(0.10f, 0.55f, 0.28f, 0.45f);
    static readonly Color FrameColor = new Color(0.55f, 0.60f, 0.65f);
    static readonly Color HintColor = new Color(0.55f, 0.80f, 1.0f, 0.20f);      // 置ける場所（薄い青）
    static readonly Color HintHoverColor = new Color(0.55f, 0.85f, 1.0f, 0.55f); // マウスを乗せた置ける場所
    const float HintSize = 0.86f;      // 置ける場所の四角の大きさ（マス 1 に対して）
    const float AllViewScale = 0.6f;   // 全部の段を見ているときの石の大きさ
    const float ActiveScale = 0.82f;   // 段を絞ったときの、その段の石
    const float OtherScale = 0.35f;    // 段を絞ったときの、ほかの段の石（半透明）

    readonly OseroBoard board = new OseroBoard();
    readonly List<int> flipBuf = new List<int>();
    int layer = -1;         // -1 = 全部の段を見る（ふつう）。0〜7 = その段だけに絞る
    int lastMove = -1;
    readonly float[] changedAt = new float[OseroBoard.CellCount]; // 石が置かれた・裏返った時刻（ポンと動かす）

    // ---- 3D の物 ----
    Camera cam;
    Material matBlack, matWhite, matBlackGhost, matWhiteGhost, matHint, matHintHover, matPlate, matLine, matFrame,
             matHoverBlack, matHoverWhite, matLast;
    Mesh sphereMesh, cubeMesh;
    readonly Transform[] stoneObj = new Transform[OseroBoard.CellCount];
    readonly MeshRenderer[] stoneRend = new MeshRenderer[OseroBoard.CellCount];
    Transform layerRoot;           // 今の段の板と線（段を変えると上下に動く）
    readonly List<Transform> hints = new List<Transform>();      // 置ける場所の四角（使い回す）
    readonly List<MeshRenderer> hintRends = new List<MeshRenderer>();
    readonly List<int> hintCells = new List<int>();             // 今出している四角のマスの番号
    int hoveredHint = -1;
    Transform rootT;
    Transform hover; MeshRenderer hoverRend;
    Transform lastMark;

    // ---- カメラ ----
    float yaw = 30f, pitch = 50f, zoom = 1f;

    // ---- UI ----
    Font font;
    CanvasScaler scaler;
    Text statusText, messageText, helpText, restartButtonText;
    readonly Button[] layerButtons = new Button[N];
    readonly Text[] layerButtonTexts = new Text[N];
    Button allLayersButton;
    readonly List<RectTransform> uiAreas = new List<RectTransform>(); // ここを押しても石は置かない
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
        cam.backgroundColor = BgColor;
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 200f;

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
        if (glass == null) { Debug.LogError("シェーダー Osero3D/Glass が見つからない（Assets/Resources/OseroGlass.shader）"); glass = ghost; }
        if (solid == null) Debug.LogError("シェーダー Osero3D/Solid が見つからない（Assets/Resources/OseroSolid.shader）");
        if (ghost == null) Debug.LogError("シェーダー Osero3D/Ghost が見つからない（Assets/Resources/OseroGhost.shader）");
        matBlack = M(solid, BlackColor);
        matWhite = M(solid, WhiteColor);
        matBlackGhost = M(ghost, new Color(0.0f, 0.0f, 0.0f, 0.55f));
        matWhiteGhost = M(ghost, new Color(1f, 1f, 1f, 0.35f));
        matHoverBlack = M(ghost, new Color(0.05f, 0.05f, 0.05f, 0.6f));
        matHoverWhite = M(ghost, new Color(1f, 1f, 1f, 0.6f));
        matHint = M(glass, HintColor);
        matHintHover = M(glass, HintHoverColor);
        matPlate = M(ghost, PlateColor);
        matLine = M(solid, LineColor);
        matFrame = M(solid, FrameColor);
        matLast = M(solid, new Color(0.9f, 0.15f, 0.15f));
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

    // マス (x,y,z) の中心の位置。盤全体の真ん中が (0,0,0)
    static Vector3 CellPos(int x, int y, int z) => new Vector3(x - 3.5f, y - 3.5f, z - 3.5f);
    static float LayerY(int y) => y - 3.5f;

    void BuildBoard()
    {
        var root = new GameObject("Board").transform;
        rootT = root;

        // 外枠（立方体の 12 本の辺）
        const float h = N / 2f, t = 0.04f;
        for (int a = 0; a < 3; a++)
            for (int s1 = -1; s1 <= 1; s1 += 2)
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    var e = MakeObj("Frame", cubeMesh, matFrame, root);
                    Vector3 p = Vector3.zero, sc = Vector3.one * t;
                    if (a == 0) { p = new Vector3(0, s1 * h, s2 * h); sc.x = N + t; }
                    if (a == 1) { p = new Vector3(s1 * h, 0, s2 * h); sc.y = N + t; }
                    if (a == 2) { p = new Vector3(s1 * h, s2 * h, 0); sc.z = N + t; }
                    e.localPosition = p; e.localScale = sc;
                }

        // 今の段の板と線
        layerRoot = new GameObject("Layer").transform;
        layerRoot.SetParent(root, false);
        var plate = MakeObj("Plate", cubeMesh, matPlate, layerRoot);
        plate.localScale = new Vector3(N, 0.02f, N);
        plate.localPosition = new Vector3(0, -0.45f, 0);
        for (int i = 0; i <= N; i++)
        {
            float p = i - h;
            var lx = MakeObj("LineX", cubeMesh, matLine, layerRoot);
            lx.localPosition = new Vector3(0, -0.44f, p); lx.localScale = new Vector3(N, 0.03f, 0.04f);
            var lz = MakeObj("LineZ", cubeMesh, matLine, layerRoot);
            lz.localPosition = new Vector3(p, -0.44f, 0); lz.localScale = new Vector3(0.04f, 0.03f, N);
        }

        // 石（全部のマスに 1 つずつ作っておき、空きのマスでは隠す）
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            OseroBoard.FromIndex(i, out int x, out int y, out int z);
            var st = MakeObj("Stone", sphereMesh, matBlack, root);
            st.localPosition = CellPos(x, y, z);
            stoneObj[i] = st;
            stoneRend[i] = st.GetComponent<MeshRenderer>();
            st.gameObject.SetActive(false);
        }

        // 置ける場所の四角は Refresh で必要な数だけ作る

        // マウスを乗せた置ける場所に出す、うすい石
        hover = MakeObj("Hover", sphereMesh, matHoverBlack, root);
        hover.localScale = Vector3.one * AllViewScale;
        hoverRend = hover.GetComponent<MeshRenderer>();
        hover.gameObject.SetActive(false);

        // 最後に置いた石の印（石の上に乗せる小さな赤い点）
        lastMark = MakeObj("LastMove", sphereMesh, matLast, root);
        lastMark.gameObject.SetActive(false);
    }

    // =====================================================================
    // UI
    void BuildUI()
    {
        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0] : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var root = canvasGo.transform;

        // 上: 石の数と手番
        statusText = MakeText(root, "Status", 54, TextAnchor.UpperCenter);
        Place(statusText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(900, 140));

        // その下: パス・勝ち負けなどの知らせ
        messageText = MakeText(root, "Message", 48, TextAnchor.UpperCenter);
        messageText.color = new Color(1f, 0.85f, 0.3f);
        Place(messageText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(1000, 140));

        // 右: 段のボタン（上が 8 段目）
        var col = new GameObject("Layers", typeof(RectTransform)).GetComponent<RectTransform>();
        col.SetParent(root, false);
        Place(col, new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(150, (N + 1) * 96 + 60));
        uiAreas.Add(col);
        var head = MakeText(col, "LayerHead", 30, TextAnchor.MiddleCenter);
        head.text = "段";
        Place(head.rectTransform, new Vector2(0.5f, 1), Vector2.zero, new Vector2(150, 50));
        for (int i = 0; i < N; i++)
        {
            int y = i;
            var b = MakeButton(col, "Layer" + (y + 1), "", 34, () => SetLayer(y));
            Place((RectTransform)b.transform, new Vector2(0.5f, 1), new Vector2(0, -56 - (N - 1 - y) * 96), new Vector2(150, 88));
            layerButtons[y] = b;
            layerButtonTexts[y] = b.GetComponentInChildren<Text>();
        }
        allLayersButton = MakeButton(col, "AllLayers", "全部", 34, () => SetLayer(-1));
        Place((RectTransform)allLayersButton.transform, new Vector2(0.5f, 1), new Vector2(0, -56 - N * 96), new Vector2(150, 88));

        // 左下: 最初から
        var resetBtn = MakeButton(root, "Restart", "最初から", 34, OnRestart);
        Place((RectTransform)resetBtn.transform, new Vector2(0, 0), new Vector2(20, 100), new Vector2(260, 90));
        restartButtonText = resetBtn.GetComponentInChildren<Text>();
        uiAreas.Add((RectTransform)resetBtn.transform);

        // 一番下: 操作の説明
        helpText = MakeText(root, "Help", 28, TextAnchor.LowerLeft);
        helpText.color = new Color(1, 1, 1, 0.6f);
        Place(helpText.rectTransform, new Vector2(0, 0), new Vector2(24, 16), new Vector2(1000, 70));
    }

    Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    Button MakeButton(Transform parent, string name, string label, int size, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.27f, 0.33f, 0.92f);
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        var t = MakeText(go.transform, "Label", size, TextAnchor.MiddleCenter);
        t.text = label;
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return b;
    }

    // anchor の位置を基準に、pos だけずらした所に size の大きさで置く（基準点 = 枠の同じ側）
    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    void ShowMessage(string s, float seconds)
    {
        messageText.text = s;
        messageUntil = seconds > 0 ? Time.unscaledTime + seconds : float.MaxValue;
    }

    void OnRestart()
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
        layer = -1;
        for (int i = 0; i < changedAt.Length; i++) changedAt[i] = -10f;
        ShowMessage("", 0);
        Refresh();
    }

    // 段を絞る。-1 なら全部の段。今と同じ段のボタンをもう一度押したら全部に戻す
    void SetLayer(int y)
    {
        y = Mathf.Clamp(y, -1, N - 1);
        layer = (y == layer && y >= 0) ? -1 : y;
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
        int placed = cell;
        changedAt[placed] = now;
        foreach (int i in flipBuf) changedAt[i] = now;
        lastMove = placed;

        string moverName = mover == Stone.Black ? "黒" : "白";
        string otherName = mover == Stone.Black ? "白" : "黒";
        if (after == OseroBoard.After.Passed)
            ShowMessage(otherName + "は置ける所が無いのでパス。もう一度" + moverName + "の番", 4f);
        else if (after == OseroBoard.After.GameOver)
        {
            int b = board.Count(Stone.Black), w = board.Count(Stone.White);
            string result = b > w ? "黒の勝ち！" : w > b ? "白の勝ち！" : "引き分け";
            ShowMessage("終わり　" + result + "　黒 " + b + " 対 白 " + w, 0);
        }
        else
            ShowMessage(moverName + "が " + flipBuf.Count + " 個裏返した", 2f);
        Refresh();
    }

    // 盤・UI の表示を今の状態に合わせる
    void Refresh()
    {
        layerRoot.gameObject.SetActive(layer >= 0);
        if (layer >= 0) layerRoot.localPosition = new Vector3(0, LayerY(layer), 0);

        // 置ける場所の四角（全部の段、または絞った段）と、段ごとの置ける数
        SetHover(-1);
        hintCells.Clear();
        int[] perLayer = new int[N];
        int total = 0;
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            OseroBoard.FromIndex(i, out int x, out int y, out int z);
            if (board.GameOver || !board.IsLegal(x, y, z, board.Turn)) continue;
            perLayer[y]++;
            total++;
            if (layer < 0 || y == layer) hintCells.Add(i);
        }
        while (hints.Count < hintCells.Count)
        {
            var hn = MakeObj("Hint", cubeMesh, matHint, rootT);
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

        for (int y = 0; y < N; y++)
        {
            layerButtonTexts[y].text = (y + 1) + (perLayer[y] > 0 ? "  <size=26>(" + perLayer[y] + ")</size>" : "");
            var img = (Image)layerButtons[y].targetGraphic;
            img.color = y == layer ? new Color(0.15f, 0.55f, 0.30f, 0.95f)
                      : perLayer[y] > 0 ? new Color(0.30f, 0.34f, 0.40f, 0.92f)
                      : new Color(0.18f, 0.20f, 0.24f, 0.85f);
        }
        ((Image)allLayersButton.targetGraphic).color = layer < 0 ? new Color(0.15f, 0.55f, 0.30f, 0.95f)
                                                                  : new Color(0.30f, 0.34f, 0.40f, 0.92f);

        int b = board.Count(Stone.Black), w = board.Count(Stone.White);
        string turn = board.GameOver ? "対局終了" : (board.Turn == Stone.Black ? "● 黒の番" : "○ 白の番");
        string where = layer < 0 ? "置ける所 " + total : (layer + 1) + "段目に置ける所 " + perLayer[layer];
        statusText.text = "● 黒 " + b + "　　○ 白 " + w + "\n" + turn
            + (board.GameOver ? "" : "　<size=36>（" + where + "）</size>");

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
            if (layer < 0) { m = s == Stone.Black ? matBlack : matWhite; scale = AllViewScale; }
            else if (y == layer) { m = s == Stone.Black ? matBlack : matWhite; scale = ActiveScale; }
            else { m = s == Stone.Black ? matBlackGhost : matWhiteGhost; scale = OtherScale; }

            float t = now - changedAt[i];
            if (t >= 0 && t < 0.35f) scale *= 1f + 0.35f * Mathf.Sin(t / 0.35f * Mathf.PI);

            if (stoneRend[i].sharedMaterial != m) stoneRend[i].sharedMaterial = m;
            stoneObj[i].localScale = Vector3.one * scale;
        }

        if (lastMove >= 0)
        {
            OseroBoard.FromIndex(lastMove, out int x, out int y, out int z);
            float sc = layer < 0 ? AllViewScale : y == layer ? ActiveScale : OtherScale;
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
        float now = Time.unscaledTime;
        if (now > messageUntil && messageText.text != "") messageText.text = "";
        restartButtonText.text = (!board.GameOver && now < restartConfirmUntil) ? "もう一度押す" : "最初から";

        // 縦長（スマホ）は横幅、横長（PC）は高さに合わせて UI の大きさを決める
        scaler.matchWidthOrHeight = Screen.width > Screen.height ? 1f : 0f;

        bool touchUsed = HandleTouch();
        if (!touchUsed && now - lastTouchTime > 0.5f) HandleMouse();
        HandleKeys();

        UpdateStones();
        helpText.text = now - lastTouchTime < 5f
            ? "青い四角を押して置く\n1本指で回転・2本指で拡大　右のボタンで段を絞れる"
            : "青い四角を押して置く\nドラッグで回転・ホイールで拡大　W/S・↑/↓ で段を絞る";
    }

    void LateUpdate()
    {
        // 盤全体（半径約 7）が画面に収まるように距離を決める。縦長の画面ほど遠くから見る
        float vfov = cam.fieldOfView * Mathf.Deg2Rad;
        float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov / 2f) * cam.aspect);
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
        foreach (var rt in uiAreas)
            if (rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                return true;
        return false;
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

    // 画面の点 → 押した「置ける場所の四角」の番号（hintCells の何番目か）。無ければ -1。
    // 視線が通る四角のうち、一番手前のものを選ぶ（石は小さいので、石の後ろの四角も押せる）
    int PickHint(Vector2 screenPos)
    {
        if (board.GameOver || OverUI(screenPos)) return -1;
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

    // マウスを乗せた四角を濃くして、中にうすい石を出す
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
        if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) SetLayer(layer + 1);
        if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) SetLayer(layer - 1);
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
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) SetLayer(layer + 1);
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) SetLayer(layer - 1);
    }
#endif
}
