// 《浮生渡》编辑器工具 · rig 角色播放验证（用数字证明动画真的在跑）
//
// 【为什么这样验】"能播放"不等于"播对了"。Mixamo 动画重定向到 Tripo 骨架，
// 中间任何一环错（绑定姿势、骨骼映射、轴向）都会表现为角色不动或姿势诡异。
// 所以：分段时间步进 Animator，采样关键骨的**世界坐标**，动 = 数字变化；
// 再渲染等间隔帧做接触 sheet，肉眼复核。
//
// 用法：Unity.exe -batchmode -quit -projectPath <proj>
//       -executeMethod Fushengdu.Editor.RigVerify.Run -fdShot <png前缀> -logFile <log>
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RigVerify
    {
        private static readonly string[] ProbeBones = { "Head", "L_Hand", "R_Hand", "L_Foot", "R_Foot", "Hip" };

        [MenuItem("浮生渡/14. rig 播放验证", priority = 140)]
        public static void Run()
        {
            string prefix = "D:/AI_Workspace/浮生渡/shots/rig_verify";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "-fdShot" && i + 1 < args.Length) prefix = args[i + 1];

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

            var sb = new StringBuilder();
            int failures = 0;

            foreach (string name in new[] { "jianxiu_male", "fashi_male" })
            {
                string path = "Assets/Resources/Art/Characters/rig/" + name + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine(name + ": prefab 缺失"); failures++; continue; }

                var go = Object.Instantiate(prefab, new Vector3(0, 0, 0), Quaternion.identity);
                var animator = go.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || animator.runtimeAnimatorController == null)
                {
                    sb.AppendLine(name + ": Animator/Avatar/Controller 缺失");
                    failures++;
                    Object.DestroyImmediate(go);
                    continue;
                }

                // —— 找探针骨
                var bones = new Dictionary<string, Transform>();
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    if (System.Array.IndexOf(ProbeBones, t.name) >= 0 && !bones.ContainsKey(t.name))
                        bones[t.name] = t;

                animator.Play("Idle", 0, 0f);
                animator.Update(0.016f);

                // —— 阶段 1：Idle 原地站 1 秒，看有没有呼吸/待机位移
                var before = Sample(bones);
                Step(animator, 30); // 1 秒 @30fps
                var afterIdle = Sample(bones);
                float idleDelta = MaxDelta(before, afterIdle);

                // —— 阶段 2：Speed=3 走 1 秒，位移应该明显大于 idle
                animator.SetFloat("Speed", 3f);
                var beforeWalk = Sample(bones);
                Step(animator, 30);
                var afterWalk = Sample(bones);
                float walkDelta = MaxDelta(beforeWalk, afterWalk);

                bool idleOk = idleDelta > 0.005f;
                bool walkOk = walkDelta > idleDelta * 1.2f;
                if (!idleOk || !walkOk) failures++;

                sb.AppendLine(string.Format(
                    "{0}: 骨 {1} 根 | Idle 1s 最大位移 {2:F3} m（应 >0.005） | Walk 1s {3:F3} m（应 >Idle×1.2） → {4}{5}",
                    name, bones.Count, idleDelta, walkDelta,
                    idleOk ? "待机在动 " : "待机不动 ", walkOk ? "行走在动" : "行走异常"));

                // —— 渲染：idle 第 0 帧 / idle 第 1 秒 / walk 第 1 秒（同一角度，看变化）
                animator.Play("Idle", 0, 0f); animator.Update(0.016f);
                Shoot(prefix + "_" + name + "_1_idle_t0.png", go);
                Step(animator, 30);
                Shoot(prefix + "_" + name + "_2_idle_t1.png", go);

                // —— 对照组：换成 URP/Lit 材质再渲一张。
                //    若 Lit 会变形而我们的 FDStylized 不会 → 着色器没写蒙皮，数据是好的
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                Material original = null;
                if (smr != null)
                {
                    original = smr.sharedMaterial;
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null)
                    {
                        var lm = new Material(lit) { name = "M_LitProbe" };
                        lm.SetTexture("_BaseMap", original != null ? original.GetTexture("_BaseMap") : null);
                        lm.SetColor("_BaseColor", Color.white);
                        smr.sharedMaterial = lm;
                    }
                }
                Shoot(prefix + "_" + name + "_4_URPLit.png", go);
                if (smr != null && original != null) smr.sharedMaterial = original;

                animator.SetFloat("Speed", 3f);
                Step(animator, 30);
                Shoot(prefix + "_" + name + "_3_walk_t2.png", go);

                Object.DestroyImmediate(go);
            }

            Debug.Log("[RigVerify]\n" + sb.ToString());
            Debug.Log(string.Format("[RigVerify] 结论：{0}", failures == 0 ? "PASS（动画在驱动骨骼）" : "FAIL " + failures + " 项"));
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static void Step(Animator animator, int frames)
        {
            for (int i = 0; i < frames; i++) animator.Update(1f / 30f);
        }

        private static Dictionary<string, Vector3> Sample(Dictionary<string, Transform> bones)
        {
            var d = new Dictionary<string, Vector3>();
            foreach (var kv in bones) d[kv.Key] = kv.Value.position;
            return d;
        }

        private static float MaxDelta(Dictionary<string, Vector3> a, Dictionary<string, Vector3> b)
        {
            float m = 0f;
            foreach (var kv in a)
                if (b.TryGetValue(kv.Key, out var p))
                    m = Mathf.Max(m, Vector3.Distance(kv.Value, p));
            return m;
        }

        private static void Shoot(string path, GameObject subject)
        {
            var camGo = new GameObject("VerifyCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.20f, 0.21f, 0.24f);
            cam.orthographic = true;
            cam.orthographicSize = 1.6f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            camGo.transform.position = new Vector3(subject.transform.position.x + 2.2f, 1.4f, subject.transform.position.z + 3.2f);
            camGo.transform.LookAt(subject.transform.position + Vector3.up * 0.95f, Vector3.up);

            const int W = 720, H = 1080;
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
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
        }
    }
}
