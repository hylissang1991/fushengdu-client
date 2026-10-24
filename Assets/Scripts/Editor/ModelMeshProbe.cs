// 《浮生渡》编辑器工具 · 角色网格资产探针
// 用途：排查「玩家模型在打包后不渲染」——确认 prefab 里 SkinnedMeshRenderer 的 mesh 是否为 null，
//       以及 FBX 内部的 Mesh 子资产是否还在。
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod Fushengdu.Editor.ModelMeshProbe.Capture -logFile <log>
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class ModelMeshProbe
    {
        [MenuItem("浮生渡/23. 角色网格资产探针", priority = 230)]
        public static void Capture()
        {
            string[] names =
            {
                "qingyundian_male_d02",
                "qingyundian_female_d02",
                "xueshagu_male_d02",
                "qianjige_male_d02"
            };

            foreach (string n in names)
            {
                string prefabPath = "Assets/Resources/Art/Characters/rig_glb/" + n + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError("[MeshProbe] prefab 缺失：" + prefabPath);
                    continue;
                }

                var smrs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Debug.Log("[MeshProbe] " + n + "  SkinnedMeshRenderer 数=" + smrs.Length);
                for (int i = 0; i < smrs.Length; i++)
                {
                    var s = smrs[i];
                    Debug.Log("[MeshProbe]   [" + i + "] name=" + s.name
                        + " mesh=" + (s.sharedMesh != null
                            ? (s.sharedMesh.name + " v=" + s.sharedMesh.vertexCount)
                            : "NULL")
                        + " mats=" + (s.sharedMaterials != null ? s.sharedMaterials.Length : -1)
                        + " bones=" + (s.bones != null ? s.bones.Length : -1)
                        + " rootBone=" + (s.rootBone != null ? s.rootBone.name : "null"));
                }

                string fbxPath = "Assets/Art/Characters/RiggedGLB/" + n + ".fbx";
                var all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                int meshCount = 0;
                int clipCount = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is Mesh) meshCount++;
                    else if (all[i] is AnimationClip) clipCount++;
                }
                Debug.Log("[MeshProbe]   fbx=" + fbxPath + " 子资产=" + all.Length
                    + " Mesh=" + meshCount + " Clip=" + clipCount);
            }

            Debug.Log("[MeshProbe] 探针结束");
        }
    }
}
