// 《浮生渡》编辑器工具 · 玩家全状态体检
//
// 用途：玩家在游戏里躺倒，但编辑器里单独实例化 rig 预制体是站着 —— 差异必然在
// ① Player.prefab 的层级/组件/挂点变换，或 ② 某个具体动画状态。
// 本脚本一次给出两样东西：
//   A. Player.prefab 的完整层级树（名称 / 局部变换 / 组件）
//   B. 进 Play 模式遍历 Animator 全部状态，逐状态量「髋高 / 脚高 / 躯干朝向」并出图
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class PlayerAudit
    {
        private static GameObject _go;
        private static Animator _anim;
        private static SkinnedMeshRenderer _smr;
        private static Camera _cam;
        private static Transform _head, _hip, _foot;
        private static string[] _names;
        private static int _idx = -1, _wait;
        private static bool _armed;
        private static readonly StringBuilder Log = new StringBuilder();
        private const string Dir = "D:/AI_Workspace/浮生渡/shots/audit/";

        [MenuItem("浮生渡/21. 玩家全状态体检")]
        public static void Run()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Directory.CreateDirectory(Dir);

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
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.Euler(34f, -26f, 0f);

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.localScale = new Vector3(20f, 1f, 20f);
            var pm = new Material(Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit"));
            pm.SetColor("_Color", new Color(0.30f, 0.34f, 0.30f));
            plane.GetComponent<Renderer>().sharedMaterial = pm;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (prefab == null) { Debug.Log("[Audit] Player.prefab 缺失"); EditorApplication.Exit(1); return; }
            _go = (GameObject)Object.Instantiate(prefab);
            _go.transform.position = Vector3.zero;

            var camGo = new GameObject("AuditCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            _cam.fieldOfView = 38f;
            camGo.transform.position = new Vector3(2.0f, 1.35f, -3.3f);
            camGo.transform.LookAt(new Vector3(0f, 0.80f, 0f), Vector3.up);

            // —— A. 层级树
            Log.AppendLine("========== A. Player.prefab 层级 ==========");
            Dump(_go.transform, 0);

            _anim = _go.GetComponentInChildren<Animator>();
            _smr = _go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_anim == null || _smr == null)
            {
                Log.AppendLine("缺 Animator 或 SkinnedMeshRenderer，无法遍历状态");
                Debug.Log("[Audit]\n" + Log);
                EditorApplication.Exit(1);
                return;
            }

            foreach (var b in _smr.bones)
            {
                if (b == null) continue;
                if (b.name == "Head") _head = b;
                else if (b.name == "Hip" || b.name == "Pelvis") { if (_hip == null) _hip = b; }
                else if (b.name == "L_Foot") _foot = b;
            }
            Log.AppendLine(string.Format("SMR: bones={0} rootBone={1} | Animator.applyRootMotion={2} culling={3}",
                _smr.bones.Length, _smr.rootBone == null ? "null" : _smr.rootBone.name,
                _anim.applyRootMotion, _anim.cullingMode));
            Log.AppendLine("rest: Hip=" + Y(_hip) + " Head=" + Y(_head) + " Foot=" + Y(_foot));

            var ac = _anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (ac == null || ac.layers.Length == 0)
            {
                Log.AppendLine("AnimatorController 缺失");
                Debug.Log("[Audit]\n" + Log);
                EditorApplication.Exit(1);
                return;
            }
            var names = new List<string>();
            foreach (var s in ac.layers[0].stateMachine.states) names.Add(s.state.name);
            _names = names.ToArray();
            Log.AppendLine("========== B. 逐状态体检（" + _names.Length + " 个）==========");
            Log.AppendLine("状态 | Hip_y | Foot_y | 躯干倾角 | 判定");

            _idx = -1; _wait = 0; _armed = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!_armed || !EditorApplication.isPlaying) return;
            if (++_wait < 22) return;
            _wait = 0;

            if (_idx >= 0) Report(_names[_idx]);
            _idx++;
            if (_idx >= _names.Length) { Finish(); return; }
            _anim.Play(_names[_idx], 0, 0f);
            _anim.Update(0f);
        }

        private static void Report(string state)
        {
            float hipY = _hip != null ? _hip.position.y : float.NaN;
            float footY = _foot != null ? _foot.position.y : float.NaN;
            string tilt = "-";
            bool lying = false;
            if (_hip != null && _head != null)
            {
                Vector3 up = _head.position - _hip.position;
                float ang = Vector3.Angle(up, Vector3.up);
                tilt = ang.ToString("F0") + "°";
                lying = ang > 55f;   // 脊柱偏竖直方向超过 55° → 躺/趴
            }
            string verdict = lying ? "✗ 躺倒" : "✓ 站立";
            Log.AppendLine(string.Format("{0} | {1} | {2} | {3} | {4}", state,
                hipY.ToString("F3"), footY.ToString("F3"), tilt, verdict));
            Shoot(state);
        }

        private static void Shoot(string name)
        {
            const int W = 480, H = 480;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            File.WriteAllBytes(Dir + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
        }

        private static void Finish()
        {
            _armed = false;
            EditorApplication.update -= Tick;
            Debug.Log("[Audit]\n" + Log);
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(0);
        }

        private static void Dump(Transform t, int depth)
        {
            string pad = new string(' ', depth * 2);
            var comps = new StringBuilder();
            foreach (var c in t.GetComponents<Component>())
                comps.Append(c == null ? "null" : c.GetType().Name).Append(' ');
            Log.AppendLine(string.Format("{0}{1} pos={2} rot={3} scale={4} [{5}]",
                pad, t.name, t.localPosition.ToString("F3"),
                t.localEulerAngles.ToString("F1"), t.localScale.ToString("F3"), comps.ToString().Trim()));
            if (depth >= 4) return;   // 骨骼层太深，只到第 4 层
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
        }

        private static string Y(Transform t) { return t == null ? "-" : t.position.y.ToString("F3"); }
    }
}
