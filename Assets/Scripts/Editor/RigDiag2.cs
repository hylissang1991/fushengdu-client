using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RigDiag2
    {
        [MenuItem("浮生渡/16. 蒙皮权重诊断")]
        public static void Dump()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/Art/Characters/rig/jianxiu_male_mesh.asset");
            if (mesh == null) { Debug.Log("[Diag2] mesh 资产加载失败"); EditorApplication.Exit(1); return; }
            var bw = mesh.boneWeights;
            int nonZero = 0;
            for (int i = 0; i < bw.Length; i++)
                if (bw[i].weight0 > 0f) nonZero++;
            Debug.Log($"[Diag2] 顶点 {mesh.vertexCount} | boneWeights 长度 {bw.Length} | weight0>0 的顶点 {nonZero} | bindposes {mesh.bindposes.Length} | 法线 {mesh.normals.Length} | UV {mesh.uv.Length}");
            if (bw.Length > 0)
                Debug.Log($"[Diag2] 顶点0: idx=({bw[0].boneIndex0},{bw[0].boneIndex1},{bw[0].boneIndex2},{bw[0].boneIndex3}) w=({bw[0].weight0:F2},{bw[0].weight1:F2},{bw[0].weight2:F2},{bw[0].weight3:F2})");
            EditorApplication.Exit(0);
        }
    }
}
