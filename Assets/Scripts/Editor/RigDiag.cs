// 《浮生渡》编辑器工具 · rig prefab 内容诊断（一次性排障用）
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RigDiag
    {
        [MenuItem("浮生渡/15. rig 诊断")] public static void Dump()
        {
            foreach (string name in new[] { "jianxiu_male", "fashi_male" })
            {
                string path = "Assets/Resources/Art/Characters/rig/" + name + ".prefab";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { Debug.Log("[Diag] " + name + ": prefab 加载失败 " + path); continue; }

                var comps = go.GetComponents<Component>();
                string cs = "";
                foreach (var c in comps) cs += (c == null ? "null" : c.GetType().Name) + ", ";

                var an = go.GetComponent<Animator>();
                string anInfo = "无 Animator 组件";
                if (an != null)
                {
                    anInfo = string.Format("Animator: avatar={0} controller={1} culling={2}",
                        an.avatar == null ? "null" : (an.avatar.isValid ? "valid" : "INVALID"),
                        an.runtimeAnimatorController == null ? "null" : an.runtimeAnimatorController.name,
                        an.cullingMode);
                }

                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                string smrInfo = "无 SkinnedMeshRenderer";
                if (smr != null)
                {
                    smrInfo = string.Format("SkinnedMeshRenderer: mesh={0} bones={1} rootBone={2} mat={3}",
                        smr.sharedMesh == null ? "null" : smr.sharedMesh.name,
                        smr.bones == null ? "null" : smr.bones.Length.ToString(),
                        smr.rootBone == null ? "null" : smr.rootBone.name,
                        smr.sharedMaterial == null ? "null" : smr.sharedMaterial.name);
                }

                Debug.Log(string.Format("[Diag] {0}\n    组件: {1}\n    {2}\n    {3}", name, cs, anInfo, smrInfo));
            }
            EditorApplication.Exit(0);
        }
    }
}
