// 《浮生渡》编辑器工具 · Play 模式角色渲染验证
//
// 为什么需要它：batchmode 下 Camera.Render() 不会驱动 SkinnedMeshRenderer 的蒙皮更新，
// 直渲出来的是未初始化顶点（碎块），跟玩家实际看到的无关。
// 本脚本进入 Play 模式（关掉域重载以保留静态状态），让 PlayerLoop 真正跑起来，
// 在若干帧后截图 —— 这才是玩家看到的东西。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class PlayModeShot
    {
        private static Camera _cam;
        private static GameObject _subject;
        private static int _stage;
        private static bool _armed;
        private static string _dir = "D:/AI_Workspace/浮生渡/shots/";

        [MenuItem("浮生渡/19. Play 模式角色截图")]
        public static void Run()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

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

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Art/Characters/rig/jianxiu_male.prefab");
            if (prefab == null) { Debug.Log("[PlayShot] prefab 缺失"); EditorApplication.Exit(1); return; }
            _subject = (GameObject)Object.Instantiate(prefab);
            _subject.transform.position = Vector3.zero;

            var camGo = new GameObject("ShotCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            _cam.fieldOfView = 35f;
            camGo.transform.position = new Vector3(1.7f, 1.25f, -2.9f);
            camGo.transform.LookAt(new Vector3(0f, 0.86f, 0f), Vector3.up);

            // 先记录"静止姿势"（未进 Play、未驱动动画）的骨骼世界坐标，作为基准
            var restSmr = _subject.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log("[PlayShot] —— 静止姿势（rest pose）骨骼 ——");
            for (int i = 0; i < Mathf.Min(6, restSmr.bones.Length); i++)
                Debug.Log(string.Format("[PlayShot] rest bone[{0}] {1} = {2}", i,
                    restSmr.bones[i] == null ? "null" : restSmr.bones[i].name,
                    restSmr.bones[i] == null ? "-" : restSmr.bones[i].position.ToString("F3")));
            Debug.Log("[PlayShot] rest worldBounds=" + restSmr.bounds);

            _stage = 0;
            _armed = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
            Debug.Log("[PlayShot] 进入 Play 模式…");
        }

        private static void Tick()
        {
            if (!_armed || !EditorApplication.isPlaying) return;

            int f = Time.frameCount;
            if (_stage == 0 && f > 30) { Capture("play_t0"); CaptureBaked("play_t0_baked"); _stage = 1; }
            else if (_stage == 1 && f > 60) { Capture("play_t1"); CaptureBaked("play_t1_baked"); _stage = 2; }
            else if (_stage == 2 && f > 90)
            {
                Capture("play_t2");
                CaptureBaked("play_t2_baked");
                _stage = 3;
                _armed = false;
                EditorApplication.update -= Tick;

                var smr = _subject.GetComponentInChildren<SkinnedMeshRenderer>();
                var baked = new Mesh();
                smr.BakeMesh(baked);
                Debug.Log(string.Format("[PlayShot] Play 模式 3 帧已出图，baked.bounds={0}", baked.bounds));
                Debug.Log("[PlayShot] SMR worldBounds=" + smr.bounds + " | transform=" + smr.transform.position
                          + " scale=" + smr.transform.lossyScale);
                var sb = new System.Text.StringBuilder("[PlayShot] 组件：");
                foreach (var c in _subject.GetComponents<Component>())
                    sb.Append(c == null ? "null" : c.GetType().Name).Append(' ');
                sb.Append(" | 子物体 ").Append(_subject.transform.childCount).Append(" 个");
                Debug.Log(sb.ToString());
                // 骨骼世界坐标抽样：正常骨架应是一个 1m 高的人形
                for (int i = 0; i < Mathf.Min(6, smr.bones.Length); i++)
                    Debug.Log(string.Format("[PlayShot] bone[{0}] {1} world={2}", i,
                        smr.bones[i] == null ? "null" : smr.bones[i].name,
                        smr.bones[i] == null ? "-" : smr.bones[i].position.ToString("F3")));

                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }

        private static void Capture(string name)
        {
            const int W = 560, H = 700;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            File.WriteAllBytes(_dir + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
            Debug.Log("[PlayShot] 出图 " + name);
        }

        // 同一帧把"烘焙（CPU 已蒙皮）网格"单独渲一张，用于和 SMR 直出对比：
        // 两张都是碎块 → 骨架姿势坏了；烘焙是人形而直出是碎块 → 渲染路径坏了。
        private static void CaptureBaked(string name)
        {
            var smr = _subject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) return;
            var m = new Mesh();
            smr.BakeMesh(m);

            var go = new GameObject("BakedShot");
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.transform.localScale = smr.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = smr.sharedMaterial;
            smr.enabled = false;                 // 隐藏本体，避免遮蔽
            Capture(name);
            smr.enabled = true;
            Object.Destroy(go);
        }
    }
}
