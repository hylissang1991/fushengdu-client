// 《浮生渡》编辑器工具 · BakeMesh 决定性蒙皮验证
//
// 之前的"最小实验"用 Camera.Render() 逐像素比对，但 batchmode 下 SkinnedMeshRenderer
// 根本不会被 Update（没有 PlayerLoop），所以"渲染完全相同"是**测试方法失效**，不是不蒙皮。
//
// 本脚本绕过渲染：直接 Smr.BakeMesh() 取蒙皮后的真实顶点。
//   ① 最小 4 顶点 rig：转骨头 → 烘焙顶点变化？
//   ② 真角色 prefab：分帧步进 Animator → 烘焙顶点变化？
// 顶点变 = 蒙皮数据与数学都对；渲染就一定会跟着变（CPU 蒙皮把形变后的 VB 交给任意着色器）。
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RigBakeTest
    {
        [MenuItem("浮生渡/18. BakeMesh 蒙皮验证")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== BakeMesh 蒙皮验证 ==========");

            sb.AppendLine(Minimal());
            sb.AppendLine("----");
            foreach (string n in new[] { "jianxiu_male", "fashi_male" })
                sb.AppendLine(Character(n));

            Debug.Log("[BakeTest]\n" + sb.ToString());
            EditorApplication.Exit(0);
        }

        // ① 最小 rig
        private static string Minimal()
        {
            var b0 = new GameObject("B0").transform;
            var b1 = new GameObject("B1").transform;
            b1.SetParent(b0, false);
            b1.localPosition = new Vector3(0f, 1f, 0f);

            var mesh = new Mesh { name = "Min" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
            };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            };
            mesh.bindposes = new[]
            {
                Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one).inverse,
                Matrix4x4.TRS(new Vector3(0, 1, 0), Quaternion.identity, Vector3.one).inverse,
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();

            var go = new GameObject("MinBody");
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { b0, b1 };
            smr.rootBone = null;
            smr.updateWhenOffscreen = true;
            smr.sharedMaterial = new Material(Shader.Find("Fushengdu/Stylized")
                                              ?? Shader.Find("Universal Render Pipeline/Lit"));

            var baked = new Mesh();
            b1.localRotation = Quaternion.identity;
            smr.BakeMesh(baked);
            var p0 = baked.vertices;

            b1.localRotation = Quaternion.Euler(0f, 0f, 90f);
            smr.BakeMesh(baked);
            var p1 = baked.vertices;

            float d = MaxDelta(p0, p1);
            var s = new StringBuilder();
            s.AppendLine("[最小 rig] 顶点数 " + p0.Length + " | 转 90° 后最大位移 " + d.ToString("F4"));
            s.AppendLine("  旋转前 v2=" + V(p0.Length > 2 ? p0[2] : Vector3.zero)
                         + "  →  旋转后 v2=" + V(p1.Length > 2 ? p1[2] : Vector3.zero));
            s.AppendLine("  判定：" + (d > 0.01f
                ? "✓ 蒙皮生效（rig 数据与 Unity 蒙皮数学都正确）"
                : "✗ 真的不蒙皮"));
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(b0.gameObject);
            return s.ToString();
        }

        // ② 真角色
        private static string Character(string name)
        {
            string path = "Assets/Resources/Art/Characters/rig/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return "[" + name + "] prefab 缺失：" + path;

            var go = Object.Instantiate(prefab);
            go.name = name;
            var animator = go.GetComponent<Animator>();
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var s = new StringBuilder();

            if (smr == null) { s.AppendLine("[" + name + "] ✗ 没有 SkinnedMeshRenderer"); return s.ToString(); }
            if (animator == null || animator.avatar == null)
                s.AppendLine("[" + name + "] ⚠ Animator/Avatar 缺失（只能测绑定姿势）");

            // 骨骼数组体检
            int nullBones = 0;
            var bones = smr.bones;
            for (int i = 0; i < bones.Length; i++) if (bones[i] == null) nullBones++;
            s.AppendLine(string.Format("[{0}] SMR: mesh={1} 顶点={2} bones={3} bindposes={4} 空骨={5}",
                name, smr.sharedMesh != null ? smr.sharedMesh.name : "null",
                smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0,
                bones.Length, smr.sharedMesh != null ? smr.sharedMesh.bindposes.Length : 0, nullBones));

            var baked = new Mesh();
            smr.BakeMesh(baked);
            var a = baked.vertices;
            s.AppendLine("  原始 mesh.bounds   = " + smr.sharedMesh.bounds);
            s.AppendLine("  烘焙后 baked.bounds = " + baked.bounds);
            _lastBaked = baked;
            _lastMat = smr.sharedMaterial != null ? smr.sharedMaterial
                : new Material(Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit"));

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                for (int i = 0; i < 20; i++) animator.Update(1f / 30f);
                smr.BakeMesh(baked);
                var b = baked.vertices;
                for (int i = 0; i < 20; i++) animator.Update(1f / 30f);
                smr.BakeMesh(baked);
                var c = baked.vertices;

                float d1 = MaxDelta(a, b), d2 = MaxDelta(b, c);
                s.AppendLine(string.Format("[{0}] 烘焙顶点最大位移：绑定姿势→0.67s = {1:F4} | 0.67s→1.33s = {2:F4}",
                    name, d1, d2));
                s.AppendLine("  采样顶点0：绑定=" + V(a[0]) + " | 0.67s=" + V(b[0]) + " | 1.33s=" + V(c[0]));
                s.AppendLine("  判定：" + ((d1 > 0.005f || d2 > 0.005f)
                    ? "✓ 网格随动画变形（蒙皮链路完全正常）"
                    : "✗ 网格不变形（骨骼动、肉不动）"));

                // 视觉验证：把烘焙出的形变网格真的渲成图（MeshRenderer 不依赖 SMR 的 Update，
                // 所以在 batchmode 里能可靠出图；它用的就是 SMR 渲染时的同一份形变顶点）
                string dir = "D:/AI_Workspace/浮生渡/shots/";
                var shot = new Mesh { name = name };
                smr.BakeMesh(shot); Shoot(shot, name + "_bake_t0", dir);
                for (int i = 0; i < 20; i++) animator.Update(1f / 30f);
                smr.BakeMesh(shot); Shoot(shot, name + "_bake_t1", dir);
                for (int i = 0; i < 20; i++) animator.Update(1f / 30f);
                smr.BakeMesh(shot); Shoot(shot, name + "_bake_t2", dir);
                s.AppendLine("  出图：" + dir + name + "_bake_t{0,1,2}.png（同一动作的三个连续时刻）");
            }

            Object.DestroyImmediate(go);
            return s.ToString();
        }

        // 把一个已烘焙（已形变）的网格渲成 PNG。直接使用 BakeMesh 产出的网格，
        // 法线/UV/拓扑全部自洽，不要再手工重算。
        private static void Shoot(Mesh mesh, string file, string dir)
        {
            var go = new GameObject("Shot_" + file);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _lastMat;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.5f;
            sun.transform.rotation = Quaternion.Euler(30f, -35f, 0f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
            cam.fieldOfView = 32f;
            Bounds b = mesh.bounds;
            float d = Mathf.Max(b.size.y, b.size.x) * 1.45f + 0.4f;
            camGo.transform.position = b.center + new Vector3(d * 0.28f, b.size.y * 0.02f, d);
            camGo.transform.LookAt(b.center, Vector3.up);

            var rt = new RenderTexture(560, 700, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(560, 700, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 560, 700), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            System.IO.File.WriteAllBytes(dir + file + ".png", tex.EncodeToPNG());

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(sun.gameObject);
        }

        private static Mesh _lastBaked;
        private static Material _lastMat;

        private static float MaxDelta(Vector3[] a, Vector3[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1f;
            float m = 0f;
            for (int i = 0; i < a.Length; i++) m = Mathf.Max(m, Vector3.Distance(a[i], b[i]));
            return m;
        }

        private static string V(Vector3 v)
        {
            return string.Format("({0:F3},{1:F3},{2:F3})", v.x, v.y, v.z);
        }
    }
}
