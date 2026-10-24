// 《浮生渡》编辑器工具 · 角色网格资产探针
// 用途：排查「玩家模型在打包后不渲染」——确认 prefab 里 SkinnedMeshRenderer 的 mesh 是否为 null，
//       以及 FBX 内部的 Mesh 子资产是否还在。
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod Fushengdu.Editor.ModelMeshProbe.Capture -logFile <log>
// 全量资产体检（15 prefab + 19 动画 FBX + 贴图 + LFS 指针扫描）：
//   -executeMethod Fushengdu.Editor.ModelMeshProbe.AuditAll
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class ModelMeshProbe
    {
        private const string PrefabDir = "Assets/Resources/Art/Characters/rig_glb";
        private const string FbxDir = "Assets/Art/Characters/RiggedGLB";
        private const string AnimDir = "Assets/Art/Animations/Mixamo";

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
                string prefabPath = PrefabDir + "/" + n + ".prefab";
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

                string fbxPath = FbxDir + "/" + n + ".fbx";
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

        // ---------------------------------------------------------------- 全量体检

        private const string LfsMagic = "version https://git-lfs.github.com/spec/v1";

        [MenuItem("浮生渡/24. 全量资产体检（prefab/动画/贴图/LFS）", priority = 231)]
        public static void AuditAll()
        {
            AuditPrefabs();
            AuditAnimationFbx();
            AuditTextures();
            AuditLfsPointers();
            Debug.Log("[Audit] === ALL DONE ===");
        }

        private static bool IsLfsPointer(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 4096) return false;
                using (var fs = File.OpenRead(path))
                {
                    byte[] buf = new byte[64];
                    int n = fs.Read(buf, 0, buf.Length);
                    string head = Encoding.ASCII.GetString(buf, 0, n);
                    return head.StartsWith(LfsMagic);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void AuditPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
            Debug.Log("[Audit] === PREFAB 数量=" + guids.Length + " ===");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string name = Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogError("[Audit] PREFAB|" + name + "|MISSING|-|-|FAIL");
                    continue;
                }

                var smrs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                {
                    Debug.LogError("[Audit] PREFAB|" + name + "|NO_SMR|0|0|FAIL");
                    continue;
                }

                for (int i = 0; i < smrs.Length; i++)
                {
                    var s = smrs[i];
                    var m = s.sharedMesh;
                    bool ok = m != null && m.vertexCount > 0 && s.sharedMaterials != null
                              && s.sharedMaterials.Length > 0 && s.bones != null && s.bones.Length > 0;
                    Debug.Log("[Audit] PREFAB|" + name + "|"
                        + (m != null ? m.name : "NULL") + "|"
                        + (m != null ? m.vertexCount : 0) + "|"
                        + (s.bones != null ? s.bones.Length : 0) + "|"
                        + (ok ? "OK" : "FAIL")
                        + "|smrIdx=" + i
                        + " mats=" + (s.sharedMaterials != null ? s.sharedMaterials.Length : -1)
                        + " rootBone=" + (s.rootBone != null ? s.rootBone.name : "null")
                        + " path=" + path);
                }

                var animator = prefab.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    var av = animator.avatar;
                    Debug.Log("[Audit] PREFAB_AVATAR|" + name + "|"
                        + (av != null ? (av.name + " human=" + av.isHuman + " valid=" + av.isValid) : "NULL"));
                }
            }
        }

        private static void AuditAnimationFbx()
        {
            string[] files = Directory.GetFiles(AnimDir, "*.fbx", SearchOption.TopDirectoryOnly);
            Debug.Log("[Audit] === ANIM FBX 数量=" + files.Length + " ===");
            foreach (string f in files)
            {
                string rel = f.Replace("\\", "/");
                string name = Path.GetFileNameWithoutExtension(rel);
                long size = new FileInfo(f).Length;
                bool ptr = IsLfsPointer(f);
                var all = AssetDatabase.LoadAllAssetsAtPath(rel);
                int clip = 0;
                int avatar = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is AnimationClip) clip++;
                    else if (all[i] is Avatar) avatar++;
                }
                bool ok = !ptr && size > 100 * 1024 && clip > 0;
                Debug.Log("[Audit] ANIM|" + name + "|" + size + "|" + clip + "|"
                    + (ok ? "OK" : "FAIL") + "|ptr=" + ptr + " avatar=" + avatar + " subs=" + all.Length);
            }
        }

        private static void AuditTextures()
        {
            string[] dirs =
            {
                "Assets/Art/Characters/RiggedGLB/Tex",
                "Assets/Art/Characters/RiggedTex",
                "Assets/Resources/Art/Characters"
            };
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir))
                {
                    Debug.LogError("[Audit] 贴图目录不存在：" + dir);
                    continue;
                }
                string[] files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                int count = 0;
                long total = 0;
                foreach (string f in files)
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".tga") continue;
                    string rel = f.Replace("\\", "/");
                    long size = new FileInfo(f).Length;
                    count++;
                    total += size;
                    bool ptr = IsLfsPointer(rel);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                    bool ok = !ptr && size > 1024 && tex != null && tex.width > 0 && tex.height > 0;
                    Debug.Log("[Audit] TEX|" + rel + "|" + size + "|"
                        + (tex != null ? (tex.width + "x" + tex.height) : "NULL") + "|"
                        + (ok ? "OK" : "FAIL") + "|ptr=" + ptr);
                }
                Debug.Log("[Audit] TEX_SUMMARY|" + dir + "|" + count + "|" + total + "|");
            }
        }

        private static void AuditLfsPointers()
        {
            string[] all = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories);
            int found = 0;
            foreach (string f in all)
            {
                if (f.EndsWith(".meta")) continue;
                if (IsLfsPointer(f))
                {
                    found++;
                    Debug.LogError("[Audit] LFS_POINTER|" + f.Replace("\\", "/"));
                }
            }
            Debug.Log("[Audit] LFS_SCAN|文件总数=" + all.Length + "|指针残留=" + found + "|");
        }
    }
}
