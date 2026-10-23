// 《浮生渡》编辑器工具 · 角色建模探针
// 目的：用户给的 4 个 glb（已离线转成 OBJ）到底是什么姿态、多大、朝哪，必须先用真实渲染确认，
//       否则"旋转 90° / 缩放 2.2 倍"这类参数只能靠猜。
// 做法：导入 → 打原始 bounds 到日志 → 统一归一化成 1.8m 高、脚底贴地 → 正面/侧面各拍一张。
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod Fushengdu.Editor.CharacterProbe.Run -fdShot <png前缀>
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class CharacterProbe
    {
        private static readonly string[] Jobs = { "jianxiu", "daoke", "fashi", "mfashi" };

        [MenuItem("浮生渡/7. 角色建模探针（姿态/尺寸/朝向）", priority = 70)]
        public static void Run()
        {
            string prefix = "D:/AI_Workspace/浮生渡/shots/char_probe";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "-fdShot" && i + 1 < args.Length) prefix = args[i + 1];

            var meshes = new List<Mesh>();
            var names = new List<string>();

            foreach (string job in Jobs)
            {
                string path = "Assets/Art/Characters/" + job + ".obj";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null)
                {
                    Debug.LogError("[探针] 找不到导入器：" + path);
                    continue;
                }
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importNormals = ModelImporterNormals.Import;
                mi.importAnimation = false;
                mi.importBlendShapes = false;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.SaveAndReimport();

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { Debug.LogError("[探针] 加载失败：" + path); continue; }
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { Debug.LogError("[探针] 无网格：" + path); continue; }

                Mesh m = mf.sharedMesh;
                Bounds b = m.bounds;
                Debug.Log(string.Format("[探针] {0,-8} 顶点={1,7} 三角={2,7} | size=({3:F3},{4:F3},{5:F3}) center=({6:F3},{7:F3},{8:F3}) min.y={9:F3}",
                    job, m.vertexCount, m.triangles.Length / 3,
                    b.size.x, b.size.y, b.size.z, b.center.x, b.center.y, b.center.z, b.min.y));
                meshes.Add(m);
                names.Add(job);
            }

            if (meshes.Count == 0) { Debug.LogError("[探针] 没有任何可用网格"); return; }

            // ---- 搭一个干净的临时场景
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

            // ---- 一排摆开，统一 1.8m 高、脚底贴 y=0
            Material mat = null;
            Shader sh = Shader.Find("Fushengdu/Stylized");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                mat = new Material(sh) { name = "M_Probe" };
                mat.SetColor("_Color", Color.white);
            }

            float span = 2.2f;
            float startX = -(meshes.Count - 1) * span * 0.5f;
            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh m = meshes[i];
                var go = new GameObject(names[i]);
                go.transform.position = new Vector3(startX + i * span, 0f, 0f);
                // 各模型朝向不统一，用烘焙表里那份实测"扶正旋转"
                go.transform.rotation = Quaternion.Euler(CharacterMeshBaker.EulerOf(names[i]));
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                if (mat != null)
                {
                    var tex = Resources.Load<Texture2D>("Art/Characters/" + names[i] + "_albedo");
                    var inst = new Material(mat);
                    if (tex != null) inst.SetTexture("_BaseMap", tex);
                    mr.sharedMaterial = inst;
                }
                NormalizeWorld(go, mr, 1.8f);
                Debug.Log("[探针] 轮廓 " + names[i] + " " + Silhouette(m, go.transform));
            }

            // 地面
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = new Vector3(0f, 0f, 0f);
            plane.transform.localScale = new Vector3(3f, 1f, 3f);
            if (mat != null)
            {
                var pm = new Material(mat);
                pm.SetColor("_Color", new Color(0.30f, 0.34f, 0.30f));
                plane.GetComponent<Renderer>().sharedMaterial = pm;
            }

            float width = meshes.Count * span + 1.2f;
            Shoot(prefix + "_front.png", new Vector3(0f, 1.25f, 8f), new Vector3(0f, 0.95f, 0f), width, 2.4f);
            Shoot(prefix + "_side.png", new Vector3(8f, 1.25f, 0f), new Vector3(0f, 0.95f, 0f), width, 2.4f);
            Shoot(prefix + "_back.png", new Vector3(0f, 1.25f, -8f), new Vector3(0f, 0.95f, 0f), width, 2.4f);

            // 每个模型单独一张证件照：用于和立绘参考图做"主色指纹"比对，确认职业↔模型映射
            // （生成时文件名和形象的对应关系并不保证正确，必须实测）
            string dir = Path.GetDirectoryName(prefix);
            for (int i = 0; i < meshes.Count; i++)
            {
                var go = GameObject.Find(names[i]);
                if (go == null) continue;
                var rend = go.GetComponent<Renderer>();
                Bounds wb = rend.bounds;
                Vector3 mid = new Vector3(wb.center.x, wb.center.y, wb.center.z);
                Shoot(Path.Combine(dir, "char_" + names[i] + ".png"),
                    new Vector3(mid.x, mid.y, mid.z + 3.2f), mid, 2.0f, 2.2f);
            }

            Debug.Log("[探针] 完成：" + prefix + "_front/_side/_back.png");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// 沿身高轴分 20 层，输出每层的"宽(X) / 厚(Z)"跨度。
        /// 站立人形的特征：顶端窄（头）→ 肩部变宽 → 袍摆最宽 → 底部收窄（脚）。
        /// 这个是"看不了图"时判断模型有没有站对的最可靠手段。
        /// </summary>
        public static string Silhouette(Mesh mesh, Transform t)
        {
            const int B = 20;
            var v = mesh.vertices;
            var mn = new float[B]; for (int i = 0; i < B; i++) mn[i] = float.MaxValue;
            var mx = new float[B]; for (int i = 0; i < B; i++) mx[i] = float.MinValue;
            var zn = new float[B]; for (int i = 0; i < B; i++) zn[i] = float.MaxValue;
            var zx = new float[B]; for (int i = 0; i < B; i++) zx[i] = float.MinValue;
            var cnt = new int[B];

            // 必须用**世界**高度：mesh.bounds 是未缩放的局部值，直接拿去除会把大多数顶点挤到最高层
            float height = Mathf.Max(0.0001f, mesh.bounds.size.y * t.localScale.y);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = t.TransformPoint(v[i]);
                int b = Mathf.FloorToInt((w.y / height) * B);
                if (b < 0) b = 0; if (b >= B) b = B - 1;
                if (w.x < mn[b]) mn[b] = w.x;
                if (w.x > mx[b]) mx[b] = w.x;
                if (w.z < zn[b]) zn[b] = w.z;
                if (w.z > zx[b]) zx[b] = w.z;
                cnt[b]++;
            }
            sb.Append("宽: ");
            for (int b = B - 1; b >= 0; b--) sb.Append((mx[b] - mn[b]).ToString("F2")).Append(' ');
            sb.Append("| 厚: ");
            for (int b = B - 1; b >= 0; b--) sb.Append((zx[b] - zn[b]).ToString("F2")).Append(' ');
            sb.Append("| 顶点: ");
            for (int b = B - 1; b >= 0; b--) sb.Append(cnt[b]).Append(' ');
            return sb.ToString();
        }

        /// <summary>缩放 + 落地：按**世界包围盒**算，模型带旋转也能对（脚底 y=0、水平居中到锚点）。</summary>
        public static void NormalizeWorld(GameObject go, Renderer rend, float targetHeight)
        {
            Vector3 anchor = go.transform.position;
            Bounds wb = rend.bounds;
            float h = Mathf.Max(0.0001f, wb.size.y);
            go.transform.localScale *= targetHeight / h;
            wb = rend.bounds; // 缩放后重取
            // 只抵消"相对锚点"的偏移，别把锚点本身（排位用的 X）也吃掉
            go.transform.position = new Vector3(
                anchor.x - (wb.center.x - anchor.x),
                anchor.y - wb.min.y,
                anchor.z - (wb.center.z - anchor.z));
        }

        private static void Shoot(string path, Vector3 camPos, Vector3 lookAt, float width, float height)
        {
            var camGo = new GameObject("ProbeCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(height * 0.5f, width * 0.5f / 1.7778f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            camGo.transform.position = camPos;
            camGo.transform.LookAt(lookAt, Vector3.up);

            const int W = 1600, H = 900;
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
            Debug.Log("[探针] 已写入 " + path);

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
        }
    }
}
