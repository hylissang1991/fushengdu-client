// 《浮生渡》编辑器工具 · 最小蒙皮实验 v2（可见四边形 + 旋转前后像素对比）
//
// 判定方式：v1 用 BakeMesh 只反映一条路径。这次渲两张图（旋转前/后），
// 若两张不同 → 渲染路径的蒙皮生效（问题在 rig 数据）；若相同 → 蒙皮真没生效。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RigMinimal
    {
        [MenuItem("浮生渡/17. 最小蒙皮实验")]
        public static void Run()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var b0 = new GameObject("Bone0").transform;
            var b1Go = new GameObject("Bone1");
            var b1 = b1Go.transform;
            b1.SetParent(b0, false);
            b1.localPosition = new Vector3(0f, 1f, 0f);

            // 可见四边形：下半绑 bone0，上半绑 bone1
            var mesh = new Mesh { name = "Minimal" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
            };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.uv = new[] { Vector2.zero, Vector2.one, Vector2.zero, Vector2.one };
            mesh.boneWeights = new BoneWeight[]
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

            var body = new GameObject("Body");
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { b0, b1 };
            smr.rootBone = null;
            smr.updateWhenOffscreen = true;
            smr.localBounds = mesh.bounds;
            var mat = new Material(Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_Color", new Color(1f, 0.3f, 0.3f));
            smr.sharedMaterial = mat;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.transform.rotation = Quaternion.Euler(30f, -30f, 0f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            cam.orthographic = true;
            cam.orthographicSize = 1.5f;
            camGo.transform.position = new Vector3(0f, 0.5f, 5f);
            camGo.transform.LookAt(new Vector3(0, 0.5f, 0), Vector3.up);

            const int W = 400, H = 400;
            byte[] a = Shoot(cam, W, H);
            b1.localRotation = Quaternion.Euler(0f, 0f, 90f); // 上半应绕 bone0 原点转 90°
            byte[] b = Shoot(cam, W, H);

            bool same = a.Length == b.Length;
            if (same) for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { same = false; break; }

            File.WriteAllBytes("D:/AI_Workspace/浮生渡/shots/rig_minimal_a.png", a);
            File.WriteAllBytes("D:/AI_Workspace/浮生渡/shots/rig_minimal_b.png", b);
            Debug.Log(string.Format("[最小实验v2] FDStylized：旋转前后渲染{0} → {1}",
                same ? "完全相同" : "不同",
                same ? "✗ 不蒙皮" : "✓ 蒙皮"));

            // —— 换 URP/Lit 再测一次（这次只比旋转前后，排除明暗差异的干扰）
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var litMat = new Material(lit) { name = "M_LitMinimal" };
                litMat.SetColor("_BaseColor", new Color(1f, 0.3f, 0.3f));
                smr.sharedMaterial = litMat;
                b1.localRotation = Quaternion.identity;
                byte[] c = Shoot(cam, W, H);
                b1.localRotation = Quaternion.Euler(0f, 0f, 90f);
                byte[] d = Shoot(cam, W, H);
                bool same2 = c.Length == d.Length;
                if (same2) for (int i = 0; i < c.Length; i++) if (c[i] != d[i]) { same2 = false; break; }
                Debug.Log(string.Format("[最小实验v2] URP/Lit：旋转前后渲染{0} → {1}",
                    same2 ? "完全相同" : "不同",
                    same2 ? "✗ 也不蒙皮（→ 必须自己写蒙皮进 FDStylized）" : "✓ 蒙皮（→ 换 URP/Lit 即可，或照它改 FDStylized）"));
                File.WriteAllBytes("D:/AI_Workspace/浮生渡/shots/rig_minimal_lit_a.png", c);
                File.WriteAllBytes("D:/AI_Workspace/浮生渡/shots/rig_minimal_lit_b.png", d);
            }
            EditorApplication.Exit(0);
        }

        private static byte[] Shoot(Camera cam, int w, int h)
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
            return tex.EncodeToPNG();
        }
    }
}
