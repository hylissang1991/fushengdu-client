// 《浮生渡》编辑器工具 · 场景截图（用于离线目视 / 统计校验）
// 为什么要有这个：每次改着色器都重新打包 Windows 播放器要 1~2 分钟，而垂直切片阶段
// 需要反复看画面。这里在 Editor 里直接用临时相机渲染到 RenderTexture 并输出 PNG，
// 命令行即可调用，几秒出图。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj> -logFile <log>
//             -executeMethod Fushengdu.Editor.SceneShot.Capture
//             -fdShot <输出png> -fdShotEye x,y,z -fdShotLook x,y,z -fdShotFov 55
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class SceneShot
    {
        private static string Arg(string[] args, string key, string fallback)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == key && i + 1 < args.Length) return args[i + 1];
            }
            return fallback;
        }

        [MenuItem("浮生渡/4. 场景截图（Editor 内渲染）", priority = 40)]
        public static void Capture()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            // 关键：batchmode 里 Unity 可能以「空场景」启动（Library/LastSceneManagerSetup.txt 为空），
            // 那就截到一张纯色图。这里显式把目标场景打开再渲染。
            string scenePath = Arg(args, "-fdShotScene", "Assets/Scenes/NewcomerZone.unity");
            if (!string.IsNullOrEmpty(scenePath) && System.IO.File.Exists("D:/AI_Workspace/浮生渡/client/" + scenePath))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            string path = Arg(args, "-fdShot", "D:/AI_Workspace/浮生渡/Logs/shots/editor_shot.png");
            string eyeStr = Arg(args, "-fdShotEye", "0,6,-12");
            string lookStr = Arg(args, "-fdShotLook", "0,1.5,6");
            float fov = 55f;
            float.TryParse(Arg(args, "-fdShotFov", "55"), out fov);

            Vector3 eye = ParseVec(eyeStr, new Vector3(0f, 6f, -12f));
            Vector3 look = ParseVec(lookStr, new Vector3(0f, 1.5f, 6f));

            int width = 1280, height = 720;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            var camGo = new GameObject("ShotCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(Camera.main != null ? Camera.main : cam);
            cam.fieldOfView = fov;
            cam.farClipPlane = 1600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
            cam.transform.position = eye;
            cam.transform.LookAt(look);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            byte[] png = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, png);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);

            Debug.Log("[浮生渡] 场景截图已输出：" + path);
        }

        private static Vector3 ParseVec(string s, Vector3 fallback)
        {
            string[] parts = s.Split(',');
            if (parts.Length != 3) return fallback;
            float x, y, z;
            if (!float.TryParse(parts[0], out x)) return fallback;
            if (!float.TryParse(parts[1], out y)) return fallback;
            if (!float.TryParse(parts[2], out z)) return fallback;
            return new Vector3(x, y, z);
        }
    }
}
