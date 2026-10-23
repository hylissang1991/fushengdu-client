// 《浮生渡》编辑器工具 · 把用户给的人物建模烘焙成"标准人形"Mesh 资产
//
// 【为什么要有这一步】
// 用户提供的 glb 是 AI 生成的：① Unity 原生不认 glb（项目里没有 glTFast），已离线转成 OBJ；
// ② 模型是**躺着**的（身高沿 -Z、脸朝 +Y），且原点不在脚底、尺寸只有 0.8 单位；
// ③ 面数 11 万，直接放进场景每次都要在运行时算变换，调试成本高且容易踩坑。
// 所以这里一次性把"站起来 + 缩放到 1.8m + 脚底贴地 + 水平居中"**烘焙进顶点**，
// 运行时拿到的就是一个标准的、站立在 y=0 上的人形网格，后续相机锚点/动作/落点全部按标准人形写。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.CharacterMeshBaker.Bake -logFile <log>
//   -fdTargetHeight 1.8  （可选，默认 1.8m）
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class CharacterMeshBaker
    {
        // 职业 ↔ 建模文件名的映射（生成时文件名与形象并不保证对得上，按实测配色/体形定的，
        // 依据写在 docs/角色建模落地.md 里）
        private static readonly string[] Jobs = { "jianxiu", "daoke", "fashi", "mfashi" };

        /// <summary>
        /// 每个模型自带的"扶正旋转"。AI 生成的 4 个模型朝向并不统一，实测结论：
        ///  - jianxiu / fashi / mfashi：本来就是 Y-up 站着的（分层截面积呈"脚窄→躯干宽→头窄"）
        ///  - daoke：身高轴在 X 上（沿 Y 分层时是个 4.5m 厚的大平板），必须绕 Z 轴 +90° 扶正
        /// 判据来自 CharacterProbe 的轮廓日志，不是靠肉眼。
        /// </summary>
        public static Vector3 EulerOf(string job)
        {
            switch (job)
            {
                case "daoke": return new Vector3(0f, 0f, 90f);
                default: return Vector3.zero;
            }
        }

        [MenuItem("浮生渡/8. 烘焙人物建模资产", priority = 80)]
        public static void Bake()
        {
            float targetHeight = 1.8f;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-fdTargetHeight" && i + 1 < args.Length)
                    float.TryParse(args[i + 1], out targetHeight);
            }

            foreach (string job in Jobs)
            {
                string objPath = "Assets/Art/Characters/" + job + ".obj";
                string assetPath = "Assets/Resources/Art/Characters/" + job + ".asset";

                var src = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
                if (src == null)
                {
                    Debug.LogWarning("[烘焙] 跳过（找不到 OBJ）：" + objPath);
                    continue;
                }
                var mf = src.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                {
                    Debug.LogWarning("[烘焙] 跳过（无网格）：" + objPath);
                    continue;
                }

                Mesh outMesh = StandUp(mf.sharedMesh, targetHeight, EulerOf(job));
                AssetDatabase.CreateAsset(outMesh, assetPath);
                EditorUtility.SetDirty(outMesh);
                Debug.Log(string.Format("[烘焙] {0} -> {1} | 顶点 {2} 三角 {3} | 落地后 size=({4:F3},{5:F3},{6:F3}) min.y={7:F4}",
                    job, assetPath, outMesh.vertexCount, outMesh.triangles.Length / 3,
                    outMesh.bounds.size.x, outMesh.bounds.size.y, outMesh.bounds.size.z, outMesh.bounds.min.y));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>站起来（绕 X +90°）→ 缩放到目标身高 → 脚底贴 y=0、水平居中。返回新 Mesh。</summary>
        public static Mesh StandUp(Mesh src, float targetHeight, Vector3 euler)
        {
            var m = Object.Instantiate(src);
            m.name = src.name;

            // 【踩坑】一开始以为模型是躺着的（身高沿 -Z），加了 X+90° 把它放倒了。
            // 剖面数据证明 Y 才是身高轴：沿 Y 分层时最底层截面积只有 0.05（脚），
            // 中段 0.27~0.38（躯干/袍），顶端 0.06（头顶）——标准站立人形。模型本来就是 Y-up 站着的。
            var rot = Matrix4x4.Rotate(Quaternion.Euler(euler));
            var verts = (Vector3[])src.vertices.Clone();
            for (int i = 0; i < verts.Length; i++) verts[i] = rot.MultiplyPoint3x4(verts[i]);

            var b = new Bounds(verts[0], Vector3.zero);
            for (int i = 1; i < verts.Length; i++) b.Encapsulate(verts[i]);

            float s = targetHeight / Mathf.Max(0.0001f, b.size.y);
            var pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            for (int i = 0; i < verts.Length; i++) verts[i] = (verts[i] - pivot) * s;

            m.vertices = verts;

            var norms = src.normals;
            if (norms != null && norms.Length == verts.Length)
            {
                var n = (Vector3[])norms.Clone();
                for (int i = 0; i < n.Length; i++) n[i] = rot.MultiplyVector(n[i]).normalized;
                m.normals = n;
            }

            m.RecalculateBounds();
            return m;
        }
    }
}
