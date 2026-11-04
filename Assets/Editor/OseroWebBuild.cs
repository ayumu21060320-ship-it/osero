// Unity の上のメニューに「立体オセロ → Web 版を書き出す」を足す（Unity エディターの中だけで動く。ゲームには入らない）。
// 押すと、GitHub Pages で公開できる形（Gzip ＋ Decompression Fallback）で Web（WebGL）版を書き出す。
//   ・シーンがビルドに 1 つも入っていなければ、空のシーンを Assets/Scenes/Main.unity に作って入れる
//     （ゲームはシーンに何も置かなくても自動で起動するので、空でよい）
//   ・書き出し先のフォルダはその場で選ぶ。終わるとそのフォルダを開く
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OseroWebBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("立体オセロ/Web 版を書き出す")]
    static void Build()
    {
        // 開いているシーンに保存していない変更があれば、先に保存するか聞く
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            scenes = new[] { ScenePath };
        }

        string folder = EditorUtility.SaveFolderPanel("Web 版を書き出すフォルダを選ぶ（空のフォルダ）", "", "osero-web");
        if (string.IsNullOrEmpty(folder)) return;

        // GitHub Pages は Brotli 圧縮を正しく配れないので Gzip にし、ブラウザ側でも解凍できるようにする
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.productName = "立体オセロ";

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = folder,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log("Web 版を書き出した: " + folder);
            EditorUtility.RevealInFinder(Path.Combine(folder, "index.html"));
        }
        else
        {
            EditorUtility.DisplayDialog("立体オセロ", "Web 版の書き出しに失敗した。Console の赤い文字を見てください。", "OK");
        }
    }
}
