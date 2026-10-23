// 《浮生渡》编辑器工具 · glb 导入结果的 Play 模式取证
//
// 【为什么必须进 Play 模式】batchmode 下直接 Camera.Render() 不会驱动 SkinnedMeshRenderer 的
// 蒙皮更新，渲出来是未初始化顶点（碎块），跟玩家实际看到的无关。必须让 PlayerLoop 真跑起来。
//
// 【本脚本产出什么】
//   _0_bindpose   关掉 Animator 后的真实绑定姿势（含俯视标记图）—— 验证朝向 / 身形
//   _1.._5        播 Idle 后的前/后/右/左/3÷4 视角
//   _6_URPLit     换 URP/Lit 材质对照 —— 排除自研着色器问题
//   _7_baked      BakeMesh 烘焙对照 —— 证明 CPU 蒙皮结果正确
//   _8_top_idle   播 Idle 后的俯视标记图
//   俯视图里：洋红方块 = GameObject.forward 方向，青色方块 = GameObject.right 方向
//
// 用法：
//   Unity.exe -batchmode -projectPath <proj>      ←注意不要加 -quit，否则进不了 Play 模式
//             -executeMethod Fushengdu.Editor.GlbImportShot.Run
//             -fdName qianjige_male_d02 -fdShot D:/.../shots/glb_import/pre -logFile <log>
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class GlbImportShot
    {
        private const string PrefabDir = "Assets/Resources/Art/Characters/rig_glb";
        private const string ControllerPath =
            "Assets/Resources/Art/Characters/rig/Controllers/jianxiu_male.controller";

        private static string _name = "qianjige_male_d02";
        private static string _prefix = "D:/AI_Workspace/浮生渡/shots/glb_import/pre";

        private static GameObject _subject;
        private static Animator _anim;
        private static SkinnedMeshRenderer _smr;
        private static int _stage;
        private static bool _armed;
        private static int _ticks;
        private static readonly List<Transform> _probe = new List<Transform>();
        private static Vector3 _fwd = Vector3.forward;
        private static Vector3 _center;
        private static float _height = 1f;
        private static GameObject _mkFwd, _mkRight;

        [MenuItem("浮生渡/21. glb 导入结果 Play 模式取证", priority = 210)]
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-fdName" && i + 1 < args.Length) _name = args[i + 1];
                if (args[i] == "-fdShot" && i + 1 < args.Length) _prefix = args[i + 1];
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_prefix));

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.48f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.25f, 0.21f);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.7f;
            sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.Euler(30f, -35f, 0f);

            var fill = new GameObject("Fill");
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.color = new Color(0.75f, 0.82f, 0.95f);
            fl.intensity = 0.6f;
            fl.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(15f, 145f, 0f);

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.localScale = new Vector3(20f, 1f, 20f);
            var pm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            pm.SetColor("_BaseColor", new Color(0.30f, 0.33f, 0.30f));
            plane.GetComponent<Renderer>().sharedMaterial = pm;

            string prefabPath = Path.Combine(PrefabDir, _name + ".prefab").Replace('\\', '/');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[GlbShot] prefab 缺失：" + prefabPath);
                EditorApplication.Exit(1);
                return;
            }

            // 注意：不能用 Instantiate(prefab, pos, Quaternion.identity) —— 第三个参数会
            // 覆盖 prefab 根上保存的朝向校正（实测被清零，角色又变回侧身）。
            _subject = Object.Instantiate(prefab);
            _subject.transform.position = Vector3.zero;
            _subject.name = _name;

            _anim = _subject.GetComponent<Animator>();
            if (_anim == null) _anim = _subject.AddComponent<Animator>();
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (ctrl != null) _anim.runtimeAnimatorController = ctrl;
            else Debug.LogWarning("[GlbShot] 找不到控制器 " + ControllerPath + "（将只验静止姿势）");
            _anim.applyRootMotion = false;
            _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _smr = _subject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_smr == null) { Debug.LogError("[GlbShot] 没有 SkinnedMeshRenderer"); EditorApplication.Exit(1); return; }
            _smr.updateWhenOffscreen = true;
            _height = _smr.bounds.size.y;
            _center = _smr.bounds.center;
            Debug.Log(string.Format("[GlbShot] 角色 '{0}'：SMR bounds size={1} center={2} 顶点={3} 根旋转={4}",
                _name, _smr.bounds.size.ToString("F3"), _center.ToString("F3"),
                _smr.sharedMesh.vertexCount, _subject.transform.rotation.eulerAngles.ToString("F1")));

            // —— 朝向：从左右手骨推 forward = up × left
            var lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (lh != null && rh != null)
            {
                Vector3 left = (lh.position - rh.position).normalized;
                _fwd = Vector3.Cross(Vector3.up, left).normalized;
                Debug.Log(string.Format("[GlbShot] 绑定姿势 左手{0} 右手{1} → left={2} → forward={3}",
                    lh.position.ToString("F2"), rh.position.ToString("F2"),
                    left.ToString("F2"), _fwd.ToString("F2")));
            }
            else Debug.LogWarning("[GlbShot] 取不到左右手骨");

            // —— 探针骨
            foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Head,
                                       HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                       HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var t = _anim.GetBoneTransform(hb);
                if (t != null) _probe.Add(t);
            }

            // —— 朝向标记物：洋红=forward，青色=right
            _mkFwd = Marker(new Color(1f, 0.1f, 0.9f));
            _mkRight = Marker(new Color(0.1f, 0.95f, 1f));

            _stage = 0;
            _armed = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
            Debug.Log("[GlbShot] 进入 Play 模式…");
        }

        private static GameObject Marker(Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(0.09f, 0.09f, 0.09f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private static void PlaceMarkers()
        {
            if (_mkFwd == null) return;
            Vector3 o = _subject.transform.position;
            _mkFwd.transform.position = o + _subject.transform.forward * 0.5f + Vector3.up * 1.15f;
            _mkRight.transform.position = o + _subject.transform.right * 0.5f + Vector3.up * 1.15f;
        }

        private static void Tick()
        {
            if (!_armed) return;
            // batchmode 不能加 -quit（Unity 会在进 Play 模式前就退出），靠本脚本自己 Exit。
            if (++_ticks > 1800)
            {
                Debug.LogError("[GlbShot] 等 Play 模式帧超时（ticks=" + _ticks + "）");
                _armed = false;
                EditorApplication.Exit(2);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (_stage == 0 && Time.frameCount > 25)
            {
                _stage = 1;
                Run1();
            }
        }

        private static void Run1()
        {
            var sb = new StringBuilder();
            float dist = Mathf.Max(2.2f, _height * 2.4f);
            Vector3 ctr = _center;
            Vector3 fwd = _subject.transform.forward;
            Vector3 right = _subject.transform.right;

            // ========== A. 真实绑定姿势（关掉 Animator）==========
            _anim.enabled = false;
            PlaceMarkers();
            Debug.Log("[GlbShot] [绑定姿势] 手骨=" + CurrentFacing());
            TopDown(_prefix + "_0a_bindpose_top.png");
            Shoot(_prefix + "_0b_bindpose_front.png", ctr + fwd * dist, ctr);
            _anim.enabled = true;

            // ========== B. Mixamo 动画重定向验证 ==========
            if (_anim.runtimeAnimatorController != null)
            {
                _anim.SetFloat("Speed", 0f);
                _anim.Play("Idle", 0, 0f);
                _anim.Update(0.016f);
                var a = Snap();
                Step(30);
                var b = Snap();
                float idleDelta = MaxDelta(a, b);

                _anim.SetFloat("Speed", 3f);
                _anim.Update(0.016f);
                var c = Snap();
                Step(30);
                var d = Snap();
                float walkDelta = MaxDelta(c, d);

                sb.AppendLine(string.Format("Mixamo 重定向：Idle 1s 骨骼最大位移 {0:F4} m | Walk 1s {1:F4} m", idleDelta, walkDelta));
                sb.AppendLine(string.Format("  → 待机{0} / 行走{1}",
                    idleDelta > 0.002f ? "在动 OK" : "不动 FAIL",
                    walkDelta > idleDelta * 1.2f ? "在动 OK" : "异常 FAIL"));

                _anim.SetFloat("Speed", 0f);
                _anim.Play("Idle", 0, 0f);
                _anim.Update(0.016f);
            }

            // ========== C. 播 Idle 后的四视角 + 对照 ==========
            PlaceMarkers();
            Debug.Log("[GlbShot] [Idle姿势] 手骨=" + CurrentFacing()
                      + " bodyRot=" + _anim.bodyRotation.eulerAngles.ToString("F1"));
            TopDown(_prefix + "_8_top_idle.png");

            Shoot(_prefix + "_1_front.png", ctr + fwd * dist, ctr);
            Shoot(_prefix + "_2_back.png", ctr - fwd * dist, ctr);
            Shoot(_prefix + "_3_right.png", ctr + right * dist, ctr);
            Shoot(_prefix + "_4_left.png", ctr - right * dist, ctr);
            Shoot(_prefix + "_5_threequarter.png", ctr + (fwd + right).normalized * dist, ctr);

            // URP/Lit 对照
            Material orig = null;
            if (_smr != null)
            {
                orig = _smr.sharedMaterial;
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    var lm = new Material(lit) { name = "M_LitProbe" };
                    lm.SetTexture("_BaseMap", orig != null ? orig.GetTexture("_BaseMap") : null);
                    lm.SetColor("_BaseColor", Color.white);
                    _smr.sharedMaterial = lm;
                    Shoot(_prefix + "_6_URPLit.png", ctr + fwd * dist, ctr);
                    _smr.sharedMaterial = orig;
                }
                sb.AppendLine(string.Format("材质：shader={0} baseMap={1}",
                    orig != null ? orig.shader.name : "-",
                    orig != null && orig.GetTexture("_BaseMap") != null
                        ? orig.GetTexture("_BaseMap").name + " "
                          + orig.GetTexture("_BaseMap").width + "x" + orig.GetTexture("_BaseMap").height
                        : "无"));

                // BakeMesh 对照
                var baked = new Mesh();
                _smr.BakeMesh(baked);
                var bgo = new GameObject("Baked");
                bgo.transform.SetPositionAndRotation(_smr.transform.position, _smr.transform.rotation);
                bgo.transform.localScale = _smr.transform.lossyScale;
                bgo.AddComponent<MeshFilter>().sharedMesh = baked;
                bgo.AddComponent<MeshRenderer>().sharedMaterial = _smr.sharedMaterial;
                _smr.enabled = false;
                _mkFwd.SetActive(false);
                _mkRight.SetActive(false);
                Shoot(_prefix + "_7_baked.png", ctr + fwd * dist, ctr);
                _mkFwd.SetActive(true);
                _mkRight.SetActive(true);
                _smr.enabled = true;
                Object.Destroy(bgo);
                sb.AppendLine("BakeMesh bounds size=" + baked.bounds.size.ToString("F3"));
            }

            Debug.Log("[GlbShot] 取证结果 " + _name + "\n" + sb);
            _armed = false;
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(0);
        }

        private static string CurrentFacing()
        {
            var lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (lh == null || rh == null) return "n/a";
            return "LH=" + lh.position.ToString("F2") + " RH=" + rh.position.ToString("F2");
        }

        /// <summary>俯视图：屏幕上方 = 世界 +Z，屏幕右方 = 世界 +X。</summary>
        private static void TopDown(string path)
        {
            var camGo = new GameObject("TopCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            cam.orthographic = true;
            cam.orthographicSize = 1.1f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            camGo.transform.position = _subject.transform.position + Vector3.up * 8f;
            camGo.transform.LookAt(_subject.transform.position, Vector3.forward);

            const int W = 700, H = 700;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            Debug.Log("[GlbShot] 出图 " + path);
        }

        private static void Step(int frames)
        {
            for (int i = 0; i < frames; i++) _anim.Update(1f / 30f);
        }

        private static List<Vector3> Snap()
        {
            var l = new List<Vector3>();
            foreach (var t in _probe) l.Add(t.position);
            return l;
        }

        private static float MaxDelta(List<Vector3> a, List<Vector3> b)
        {
            float m = 0f;
            for (int i = 0; i < a.Count && i < b.Count; i++)
                m = Mathf.Max(m, Vector3.Distance(a[i], b[i]));
            return m;
        }

        private static void Shoot(string path, Vector3 camPos, Vector3 lookAt)
        {
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            camGo.transform.position = camPos;
            camGo.transform.LookAt(lookAt, Vector3.up);

            const int W = 640, H = 860;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            Debug.Log("[GlbShot] 出图 " + path);
        }
    }
}
