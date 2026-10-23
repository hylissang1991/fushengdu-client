// 《浮生渡》编辑器工具 · 归墟侵蚀通道验证 + 洋红面积卡口
//
// 【两个功能】
//   A. 洋红卡口（CI 门禁）：扫描 Monsters 资产目录下所有 *_Erosion* 贴图，
//      统计 G 通道（洋红隙缝）像素占比，>5% 直接报错退出（形态图鉴 §9.2：
//      洋红面积严格 ≤5%——从"人眼判断"变成"流程卡口"，27 只归墟 BOSS 不会漏）。
//   B. 通道功能验证：程序化生成测试侵蚀遮罩，对同一只占位 BOSS 施加
//      常态 / B类半侵蚀 / A类全侵蚀 三档参数（team-lead 2026-10-22 统一参数表），
//      出「同机位同光照的原图 + 灰度图」成对证据 —— 这就是 §13.1-7 归墟灰度门禁
//      的雏形：真资产进来后换配置表即可直接当门禁图用。
//
// 【灰度口径】灰度图不是换材质渲的，而是**把渲染结果逐像素转亮度** ——
//   这才是"同机位同光照"的忠实灰度（换材质会丢掉阴影与自发光的关系）。
//
// 【MPB 注意】侵蚀参数走 MaterialPropertyBlock（绝不写进材质）：
//   RuntimeArt.Mat() 按 Color|emission 缓存共享材质且开 instancing，
//   写进材质会污染缓存键。代价是用了 MPB 的 renderer 不参与 SRP 合批 ——
//   27 只 BOSS 各有参数，本来也合不了，可接受。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.ErosionGate.Run
//             -fdOut D:/AI_Workspace/浮生渡/shots/erosion -logFile <log>
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Fushengdu.Gameplay;
using Object = UnityEngine.Object;

namespace Fushengdu.Editor
{
    public static class ErosionGate
    {
        private const float MagentaMaxRatio = 0.05f;   // 洋红 ≤5%（形态图鉴 §9.2 硬性）

        private static string _out = "D:/AI_Workspace/浮生渡/shots/erosion";

        [MenuItem("浮生渡/24. 侵蚀通道验证 + 洋红卡口", priority = 240)]
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-fdOut") _out = args[i + 1];
            Directory.CreateDirectory(_out);

            // ---------- A. 洋红卡口（CI）----------
            int gateFail = MagentaGate();
            // ---------- B. 三档功能验证 ----------
            ChannelProof();

            Debug.Log("[ErosionGate] 完成：洋红卡口 " + (gateFail == 0 ? "通过" : gateFail + " 张超标"));
            EditorApplication.Exit(gateFail);
        }

        // ---------------------------------------------------------------- A. 洋红卡口

        private static int MagentaGate()
        {
            // 目录尚不存在时输出 0 张 —— 门禁随真资产到位自动生效
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Art/Monsters" });
            int fail = 0, checkedTex = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).Contains("_Erosion")) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                if (!tex.isReadable)
                {
                    Debug.LogError("[ErosionGate][门禁] " + path + " 未开启 Read/Write，无法做洋红占比校验 —— 请在导入预设里开启");
                    fail++;
                    continue;
                }
                float ratio = ChannelRatio(tex, 1, 0.5f);   // G 通道 > 0.5 视作洋红隙缝
                checkedTex++;
                if (ratio > MagentaMaxRatio)
                {
                    Debug.LogError(string.Format(
                        "[ErosionGate][门禁] {0} 洋红占比 {1:P2} > {2:P0}（§9.2 面积红线）", path, ratio, MagentaMaxRatio));
                    fail++;
                }
                else Debug.Log(string.Format("[ErosionGate][门禁] {0} 洋红占比 {1:P2} ✓", path, ratio));
            }
            Debug.Log("[ErosionGate][门禁] 扫描 _Erosion 贴图 " + checkedTex + " 张（资产到位后本门禁自动覆盖全部 27 只归墟系）");
            return fail;
        }

        private static float ChannelRatio(Texture2D tex, int channel, float threshold)
        {
            Color[] px = tex.GetPixels();
            int hit = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i][channel] > threshold) hit++;
            return px.Length == 0 ? 0f : hit / (float)px.Length;
        }

        // ---------------------------------------------------------------- B. 三档验证

        private static void ChannelProof()
        {
            // 空场景 + 光
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.Euler(32f, -28f, 0f);

            // 程序化测试遮罩：R = 侵蚀序场（中心裂隙向外衰减），G = 洋红隙缝放射线
            Texture2D mask = BuildTestMask(256);
            File.WriteAllBytes(Path.Combine(_out, "test_erosion_mask.png"), mask.EncodeToPNG());

            // 三档实例（同一只 B06 占位骨架，保证对照公平；完整配置直接取自家族表）
            SilhouetteSheet.Recipe recipe = FindB06();
            if (recipe == null)
            {
                Debug.LogError("[ErosionGate] 家族表里找不到 B06");
                EditorApplication.Exit(1);
                return;
            }
            var rigs = new GameObject[3];
            var mats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                rigs[i] = SilhouetteSheet.Build(FindB06());
                // 间距 4m：正交相机视口宽 = 2×2.4×(1200/560) ≈ 10.3m，6m 间距时左右两只出框
                // （首跑的教训——"三档亮度不递减"其实是左右两只没进画面，度量被背景稀释）
                rigs[i].transform.position = new Vector3((i - 1) * 4f, 0f, 0f);
                // 同一张共享遮罩挂到材质上（贴图不是 RuntimeArt.Mat 的缓存键，允许 SetTexture）
                foreach (var r in rigs[i].GetComponentsInChildren<Renderer>())
                {
                    Material m = r.sharedMaterial;
                    if (m != null && m.HasProperty("_ErosionMap")) m.SetTexture("_ErosionMap", mask);
                }
            }

            // MPB 三档（统一参数表 · team-lead 2026-10-22）
            ApplyTier(rigs[0], 0f,   1.28f, 0.62f);   // 常态
            ApplyTier(rigs[1], 0.50f, 0.64f, 0.78f);   // 怪物 B 类半侵蚀
            ApplyTier(rigs[2], 1.00f, 0.45f, 0.85f);   // 怪物 A 类全侵蚀

            // 相机：正交，正面（角色朝 +Z，从 +Z 回看）
            var camGo = new GameObject("GateCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 2.4f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            camGo.transform.position = new Vector3(0f, 2.2f, 12f);
            camGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            const int W = 1200, H = 560;
            Texture2D color = RenderCam(cam, W, H);
            File.WriteAllBytes(Path.Combine(_out, "erosion_tiers_color.png"), color.EncodeToPNG());

            // 忠实灰度：逐像素转亮度（同机位同光照，只是显示为灰度）
            Texture2D gray = ToGray(color);
            File.WriteAllBytes(Path.Combine(_out, "erosion_tiers_gray.png"), gray.EncodeToPNG());

            // 量化：三块区域的平均亮度 —— 灰度门禁的判据是"明度通道可辨侵蚀"
            float l0 = Luma(color, 0), l1 = Luma(color, 1), l2 = Luma(color, 2);
            Debug.Log(string.Format(
                "[ErosionGate] 三档平均亮度（0=常态 1=B类半蚀 2=A类全蚀）：{0:F3} / {1:F3} / {2:F3}" +
                " → 明度递减 {3}", l0, l1, l2,
                (l0 > l1 && l1 > l2) ? "✓ 灰度下可辨" : "✗ 灰度不可辨，检查参数"));
            Debug.Log("[ErosionGate] 证据图：" + _out + "/erosion_tiers_color.png + erosion_tiers_gray.png + test_erosion_mask.png");
        }

        private static SilhouetteSheet.Recipe FindB06()
        {
            foreach (var r in SilhouetteSheet.Sk05())
                if (r.Id == "B06") return r;
            return null;
        }

        /// <summary>侵蚀参数走 MPB（绝不写进材质 —— RuntimeArt.Mat 缓存键污染风险）。</summary>
        private static void ApplyTier(GameObject go, float erosion01, float saturation, float shade)
        {
            if (erosion01 <= 0f) return;   // 常态不设 MPB，走材质默认值
            var pb = new MaterialPropertyBlock();
            pb.SetFloat("_Erosion01", erosion01);
            pb.SetFloat("_Saturation", saturation);
            pb.SetFloat("_Shade", shade);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(pb);
        }

        /// <summary>
        /// 程序化测试遮罩（仅验证用，真资产用美术出的 _Erosion 贴图）。
        /// R = 侵蚀序场：中心裂隙=1，随径向距离衰减 + 三条放射裂隙增亮；
        /// G = 洋红隙缝：只沿两条细放射线，面积刻意压在 5% 红线内。
        /// </summary>
        private static Texture2D BuildTestMask(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x, y) / (size - 1f) - new Vector2(0.5f, 0.5f);
                    float d = p.magnitude * 2f;                     // 0 中心 → 1 边缘
                    float r = Mathf.Clamp01(1f - d * 1.3f);
                    // 三条放射裂隙把序场往外"撕"
                    float ang = Mathf.Atan2(p.y, p.x);
                    float crack = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ang * 3f + 0.7f)), 24f) * Mathf.Clamp01(d * 1.4f);
                    r = Mathf.Clamp01(r + crack * 0.8f);
                    // 洋红隙缝：两条极细放射线（角宽极窄）
                    float seam = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ang * 2f - 1.1f)), 400f) * Mathf.Clamp01(d * 1.2f);
                    px[y * size + x] = new Color(r, seam, 0f);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D RenderCam(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            return tex;
        }

        private static Texture2D ToGray(Texture2D src)
        {
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
            Color[] px = src.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float y = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                px[i] = new Color(y, y, y);
            }
            dst.SetPixels(px);
            dst.Apply();
            return dst;
        }

        /// <summary>三等分区（左中右三只）的平均亮度。</summary>
        private static float Luma(Texture2D tex, int third)
        {
            Color[] px = tex.GetPixels();
            int x0 = tex.width * third / 3, x1 = tex.width * (third + 1) / 3;
            float sum = 0f; int n = 0;
            for (int y = 0; y < tex.height; y++)
                for (int x = x0; x < x1; x++)
                {
                    Color c = px[y * tex.width + x];
                    sum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    n++;
                }
            return n == 0 ? 0f : sum / n;
        }
    }
}
