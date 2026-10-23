// 《浮生渡》编辑器工具 · 建模扶正方向探测
//
// jianxiu 实测 size=(1.13, 1.80, 1.86) —— Z 比身高还大，是 Z-up 躺倒的模型被按 Y 硬拉到 1.8。
// 扶正要绕 X 轴转，但 +90 还是 -90 决定"头朝上"还是"头朝下"，必须看图定。
// 本脚本把两种候选各渲一张（不做缩放归一，避免掩盖问题），并列打印 bounds。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RotateProbe
    {
        private const string Dir = "D:/AI_Workspace/浮生渡/shots/rot_probe/";

        [MenuItem("浮生渡/23. 建模扶正方向探测")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);

            var obj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/jianxiu.obj");
            if (obj == null) { Debug.Log("[Probe] 缺 jianxiu.obj"); EditorApplication.Exit(1); return; }
            var src = obj.GetComponentInChildren<MeshFilter>().sharedMesh;
            Debug.Log(string.Format("[Probe] 原始 size=({0:F2},{1:F2},{2:F2})", 
                src.bounds.size.x, src.bounds.size.y, src.bounds.size.z));

            foreach (int angle in new[] { 90, -90 })
            {
                var m = Object.Instantiate(src);
                var rot = Matrix4x4.Rotate(Quaternion.Euler(angle, 0f, 0f));
                var v = m.vertices;
                var n = m.normals;
                for (int i = 0; i < v.Length; i++) v[i] = rot.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < n.Length; i++) n[i] = rot.MultiplyVector(n[i]).normalized;
                m.vertices = v;
                m.normals = n;
                m.RecalculateBounds();

                Debug.Log(string.Format("[Probe] 绕X{0:+0;-0}° 后 size=({1:F2},{2:F2},{3:F2})",
                    angle, m.bounds.size.x, m.bounds.size.y, m.bounds.size.z));

                Shoot(m, "x" + (angle > 0 ? "p90" : "m90"));
                Object.DestroyImmediate(m);
            }
            EditorApplication.Exit(0);
        }

        private static void Shoot(Mesh mesh, string name)
        {
            var go = new GameObject("P_" + name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_Color", Color.white);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Characters/jianxiu_albedo.png");
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mr.sharedMaterial = mat;

            var sun = new GameObject("S").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.transform.rotation = Quaternion.Euler(30f, -35f, 0f);

            var camGo = new GameObject("C");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.20f, 0.24f);
            cam.fieldOfView = 34f;
            Bounds b = mesh.bounds;
            float d = Mathf.Max(b.size.y, b.size.x, b.size.z) * 2.1f + 1f;
            camGo.transform.position = b.center + new Vector3(d * 0.35f, b.size.y * 0.10f, d);
            camGo.transform.LookAt(b.center, Vector3.up);

            var rt = new RenderTexture(460, 620, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(460, 620, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 460, 620), 0, 0);
            t.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Dir + name + ".png", t.EncodeToPNG());

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(sun.gameObject);
        }
    }
}
