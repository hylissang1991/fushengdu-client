// 《浮生渡》编辑器工具 · 14 个 Tripo 绑骨模型的联络图（识别用）
// 目的：旧项目留下的 14 个 rigged GLB（7 门派 × 男女）减面后渲染成两张联络图，
//       用来识别"哪个文件 = 哪个职业"（贴图和立绘 chars_ref/*.png 比对）。
// 用法：Unity.exe -batchmode -quit -projectPath <proj>
//       -executeMethod Fushengdu.Editor.RiggedContactSheet.Run -fdShot <png前缀>
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RiggedContactSheet
    {
        [MenuItem("浮生渡/11. 绑骨模型联络图", priority = 110)]
        public static void Run()
        {
            string prefix = "D:/AI_Workspace/浮生渡/shots/rigged_sheet";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "-fdShot" && i + 1 < args.Length) prefix = args[i + 1];

            const string Dir = "Assets/Art/Characters/Rigged";
            var names = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(p);
                if (!names.Contains(n)) names.Add(n);
            }
            names.Sort();
            if (names.Count == 0)
            {
                Debug.LogError("[联络图] " + Dir + " 里没有模型");
                EditorApplication.Exit(1);
                return;
            }

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.60f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.46f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.Euler(34f, -26f, 0f);

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.localScale = new Vector3(60f, 1f, 60f);
            var pm = new Material(Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit"));
            pm.SetColor("_Color", new Color(0.30f, 0.34f, 0.30f));
            plane.GetComponent<Renderer>().sharedMaterial = pm;

            // 每张图 7 个（门派顺序），分 2 行；OBJ 导入器会把 yaw 翻转，用 yaw 180 抵消，正面朝相机
            const int perRow = 7;
            float spanX = 2.6f, spanZ = 3.2f;
            var loaded = new List<GameObject>();
            for (int i = 0; i < names.Count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(
                    AssetDatabase.FindAssets("t:Model", new[] { Dir })[0]); // placeholder, replaced below
                path = Dir + "/" + names[i] + ".obj";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (src == null) { Debug.LogWarning("[联络图] 跳过 " + names[i]); continue; }
                var mf = src.GetComponentInChildren<MeshFilter>();
                if (mf == null) continue;

                int row = i / perRow, col = i % perRow;
                var go = new GameObject(names[i]);
                go.transform.position = new Vector3(
                    (col - (perRow - 1) * 0.5f) * spanX, 0f, -row * spanZ);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // OBJ 导入翻转补偿
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                // 配对同名贴图（从 GLB 抽出的 albedo），否则只是一片暗影，没法按配色认角色
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Characters/RiggedTex/" + names[i] + "_0.png");
                var mat = new Material(Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_Color", Color.white);
                if (tex != null) mat.SetTexture("_BaseMap", tex);
                mr.sharedMaterial = mat;
                Normalize(go, mf.sharedMesh, 1.8f);
                loaded.Add(go);
            }

            float width = perRow * spanX + 1f;
            float height = spanZ * 2 + 2.5f;
            Shoot(prefix + "_a.png", new Vector3(0f, 1.6f, 7f), new Vector3(0f, 1.1f, -spanZ * 0.5f), width, height);
            Shoot(prefix + "_b.png", new Vector3(0f, 1.6f, 7f + spanZ), new Vector3(0f, 1.1f, -spanZ * 1.5f), width, height);

            string order = string.Join(", ", names);
            Debug.Log("[联络图] 顺序（左→右，上排→下排）：" + order);
            Debug.Log("[联络图] 完成：" + prefix + "_a.png / _b.png");
            EditorApplication.Exit(0);
        }

        private static void Normalize(GameObject go, Mesh mesh, float targetHeight)
        {
            Vector3 anchor = go.transform.position;
            Bounds b = mesh.bounds;
            float h = Mathf.Max(0.0001f, b.size.y);
            go.transform.localScale = Vector3.one * (targetHeight / h);
            // 旋转 180 后 bounds 会变，用渲染器世界盒重算
            var rend = go.GetComponent<Renderer>();
            Bounds wb = rend.bounds;
            go.transform.position = new Vector3(
                anchor.x - (wb.center.x - anchor.x),
                anchor.y - wb.min.y,
                anchor.z - (wb.center.z - anchor.z));
        }

        private static void Shoot(string path, Vector3 camPos, Vector3 lookAt, float width, float height)
        {
            var camGo = new GameObject("SheetCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.20f, 0.21f, 0.24f);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(height * 0.5f, width * 0.5f / 1.7778f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            camGo.transform.position = camPos;
            camGo.transform.LookAt(lookAt, Vector3.up);

            const int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[联络图] 已写入 " + path);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
        }
    }
}
