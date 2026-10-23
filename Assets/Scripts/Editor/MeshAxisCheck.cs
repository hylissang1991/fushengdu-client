// 《浮生渡》编辑器工具 · 人物建模朝向体检
//
// 现象：游戏里角色趴/躺在地上。怀疑 CharacterMeshBaker 的"本来就是 Y-up"判断错了，
// 模型实际是 Z-up，烘焙时没扶正，于是站立模型变成俯卧。
// 本脚本按轴量真实尺寸与分层轮廓，不靠肉眼。
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class MeshAxisCheck
    {
        [MenuItem("浮生渡/22. 人物建模朝向体检")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== 人物建模朝向体检 ==========");

            foreach (string job in new[] { "jianxiu", "fashi", "mfashi", "daoke" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/Art/Characters/" + job + ".asset");
                if (m == null) { sb.AppendLine("[" + job + "] 缺 baked asset"); continue; }

                Bounds b = m.bounds;
                sb.AppendLine(string.Format("[{0}] 顶点={1} size=({2:F2}, {3:F2}, {4:F2}) min=({5:F2}, {6:F2}, {7:F2})",
                    job, m.vertexCount, b.size.x, b.size.y, b.size.z, b.min.x, b.min.y, b.min.z));

                // 沿每个轴做 8 层切片，看"截面宽度"的分布
                var v = m.vertices;
                for (int axis = 0; axis < 3; axis++)
                {
                    float lo = axis == 0 ? b.min.x : axis == 1 ? b.min.y : b.min.z;
                    float hi = axis == 0 ? b.max.x : axis == 1 ? b.max.y : b.max.z;
                    float span = Mathf.Max(hi - lo, 1e-4f);
                    var cnt = new int[8];
                    var wsum = new float[8];
                    foreach (var p in v)
                    {
                        float a = axis == 0 ? p.x : axis == 1 ? p.y : p.z;
                        int k = Mathf.Clamp((int)((a - lo) / span * 8f), 0, 7);
                        cnt[k]++;
                        // 该层在另外两轴上的"包围盒对角"近似截面尺寸
                        float u = axis == 0 ? p.y : p.x;
                        float w = axis == 2 ? p.y : p.z;
                        wsum[k] += Mathf.Abs(u) + Mathf.Abs(w);
                    }
                    var sb2 = new StringBuilder();
                    for (int k = 0; k < 8; k++) sb2.Append(cnt[k].ToString().PadLeft(6));
                    sb.AppendLine(string.Format("   沿 {0} 分 8 层顶点数：{1}", "XYZ"[axis], sb2));
                }

                string guess = b.size.y > b.size.x * 1.6f && b.size.y > b.size.z * 1.6f ? "Y-up（站立，正确）"
                             : b.size.z > b.size.x * 1.6f && b.size.z > b.size.y * 1.6f ? "Z-up（躺倒，需绕 X 扶正）"
                             : b.size.x > b.size.y * 1.6f && b.size.x > b.size.z * 1.6f ? "X-up（躺倒，需绕 Z 扶正）"
                             : "三轴接近，无法判定 / 可能是团状";
                sb.AppendLine("   → 判定：" + guess);
            }

            Debug.Log("[AxisCheck]\n" + sb);
            EditorApplication.Exit(0);
        }
    }
}
