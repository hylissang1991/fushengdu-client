// 《浮生渡》编辑器工具 · 把「glb 转出的 FBX」导入成可用 Humanoid 角色 prefab
//
// 【链路】chars_rigged/*.glb（无 Draco、JPEG 贴图、41 骨）
//         → Blender 后台 glb2fbx.py → <name>.fbx    （AxAssets 之外，纯离线）
//         → 本脚本：配 ModelImporter(Human) → 自动/手动 Humanoid 映射 → 落 prefab + 材质
//
// 【为什么走 FBX 而不是 glTFast】
//   实测 14 个 glb 的 extensionsUsed 为 null（**没有 Draco**），贴图 mime 是 image/jpeg（**不是 WebP**），
//   所以唯一卡点是 Unity 2022.3 不认 .glb 容器。Blender 5.2（本机已装）能原样读出骨架+蒙皮，
//   导出 FBX 后 Unity 原生导入，无需联网装包。
//
// 【实测坑位记录】
//   · Blender 导出必须 add_leaf_bones=False，否则每根末端骨被加 "_end"，Humanoid 映射全废。
//   · Blender 5.2 的 glTF 导入会多带一个无蒙皮的 Icosphere 占位网格，导出前已清理。
//   · AvatarBuilder.BuildHumanAvatar 需要的是场景实例而不是资产，必须先 Instantiate。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.GlbRigImporter.Run -logFile <log>
//   （FBX 放在 Assets/Art/Characters/RiggedGLB/*.fbx，贴图放同目录 Tex/ 下）
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
// 注意：不要 using System.Diagnostics —— 会和 UnityEngine.Debug 撞名（CS0104）
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Fushengdu.Editor
{
    public static class GlbRigImporter
    {
        private const string FbxDir = "Assets/Art/Characters/RiggedGLB";
        private const string OutDir = "Assets/Resources/Art/Characters/rig_glb";

        // Tripo 骨名 → Unity Humanoid 槽位（与 RigBuilder.cs / TripoRigImporter.cs 同一张表）
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Hip", "Hips" }, { "Spine01", "Spine" }, { "Spine02", "Chest" },
            { "NeckTwist01", "Neck" }, { "Head", "Head" },
            { "L_Clavicle", "LeftShoulder" }, { "R_Clavicle", "RightShoulder" },
            { "L_Upperarm", "LeftUpperArm" }, { "L_Forearm", "LeftLowerArm" }, { "L_Hand", "LeftHand" },
            { "R_Upperarm", "RightUpperArm" }, { "R_Forearm", "RightLowerArm" }, { "R_Hand", "RightHand" },
            { "L_Thigh", "LeftUpperLeg" }, { "L_Calf", "LeftLowerLeg" }, { "L_Foot", "LeftFoot" },
            { "L_ToeBase", "LeftToes" },
            { "R_Thigh", "RightUpperLeg" }, { "R_Calf", "RightLowerLeg" }, { "R_Foot", "RightFoot" },
            { "R_ToeBase", "RightToes" },
        };

        [MenuItem("浮生渡/20. 导入 glb→FBX 带骨骼角色", priority = 200)]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            var guids = AssetDatabase.FindAssets("t:Model", new[] { FbxDir });
            Debug.Log("[GlbRig] 待导入 FBX 数 = " + guids.Length);
            int ok = 0, bad = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx")) continue;
                if (Process(path)) ok++; else bad++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log(string.Format("[GlbRig] 完成：{0} 成功 / {1} 失败", ok, bad));
            EditorApplication.Exit(bad > 0 ? 1 : 0);
        }

        private static bool Process(string fbxPath)
        {
            string name = Path.GetFileNameWithoutExtension(fbxPath);
            long fbxBytes = new FileInfo(fbxPath).Length;
            Debug.Log("========== [GlbRig] " + name + " ==========");
            Debug.Log(string.Format("[GlbRig] FBX {0}  大小 {1:F1} MB", fbxPath, fbxBytes / 1048576.0));

            // ---------- 1) 配导入器 ----------
            var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null) { Debug.LogError("[GlbRig] 无 ModelImporter：" + fbxPath); return false; }

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.isReadable = true;                                   // BakeMesh / 顶点取证需要
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importVisibility = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.generateSecondaryUV = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            var sw = Stopwatch.StartNew();
            imp.SaveAndReimport();
            sw.Stop();
            Debug.Log(string.Format("[GlbRig] Unity 导入耗时 {0:F1} s", sw.Elapsed.TotalSeconds));

            // ---------- 2) 自动 Humanoid 判定 ----------
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (src == null) { Debug.LogError("[GlbRig] 加载模型失败 " + fbxPath); return false; }

            var srcAnim = src.GetComponent<Animator>();
            bool autoHuman = srcAnim != null && srcAnim.avatar != null
                             && srcAnim.avatar.isValid && srcAnim.avatar.isHuman;
            Debug.Log("[GlbRig] Unity 自动 Humanoid 识别 = " + (autoHuman ? "成功" : "失败（走手动映射）"));

            // ---------- 3) 实例化后取证 + 修材质 ----------
            var inst = (GameObject)Object.Instantiate(src);
            inst.name = name;
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            var smrs = new List<SkinnedMeshRenderer>(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            Debug.Log("[GlbRig] SkinnedMeshRenderer 数 = " + smrs.Count);

            // —— 缩放到目标身高。此时 Animator 还没跑过，bounds 就是绑定姿势，
            //    把它记进 metrics，运行时可幂等地重新归一。
            if (smrs.Count > 0)
            {
                float bindH = smrs[0].bounds.size.y;
                var metrics = inst.AddComponent<Fushengdu.Gameplay.CharacterModelMetrics>();
                metrics.BindHeight = bindH;
                float k = bindH > 0.001f
                    ? Fushengdu.Gameplay.CharacterModels.TargetStandHeight / bindH
                    : 1f;
                inst.transform.localScale = new Vector3(k, k, k);
                Debug.Log(string.Format("[GlbRig] 绑定身高 {0:F3} → 缩放 {1:F4} → {2:F3} m",
                    bindH, k, smrs[0].bounds.size.y));
            }

            foreach (var smr in smrs)
            {
                var m = smr.sharedMesh;
                Debug.Log(string.Format(
                    "[GlbRig] SMR '{0}'：bones={1} 顶点={2} 子网格={3} 三角={4} bindposes={5} rootBone={6}",
                    smr.name, smr.bones.Length, m != null ? m.vertexCount : 0,
                    m != null ? m.subMeshCount : 0, TriCount(m),
                    m != null ? m.bindposes.Length : 0,
                    smr.rootBone == null ? "null" : smr.rootBone.name));
                if (m != null)
                {
                    Debug.Log(string.Format("[GlbRig]   局部 bounds size = {0}", m.bounds.size.ToString("F3")));
                    smr.localBounds = m.bounds;
                }
                smr.updateWhenOffscreen = true;
            }

            var mrStatic = inst.GetComponentsInChildren<MeshRenderer>(true);
            Debug.Log("[GlbRig] 非蒙皮 MeshRenderer 数 = " + mrStatic.Length + "（应为 0，说明没有多余占位网格）");

            // ---------- 4) 材质 + 贴图 ----------
            string texPath = FindTexture(name);
            Texture2D tex = string.IsNullOrEmpty(texPath) ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null)
            {
                var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
                Debug.Log(string.Format("[GlbRig] 贴图 {0}  {1}x{2} 格式={3} sRGB={4}",
                    texPath, tex.width, tex.height, tex.format, ti != null && ti.sRGBTexture));
            }
            else Debug.LogWarning("[GlbRig] 未找到贴图（name=" + name + "）");

            var shader = Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit");
            foreach (var smr in smrs)
            {
                var mat = new Material(shader) { name = "M_" + name };
                if (tex != null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetTexture("_MainTex", tex);
                    mat.SetColor("_Color", Color.white);
                }
                string matPath = Path.Combine(OutDir, name + "_mat.mat");
                AssetDatabase.CreateAsset(mat, matPath);
                smr.sharedMaterial = mat;
                // 保留 FBX 自带材质做对照，供后续核对
                Debug.Log("[GlbRig] 材质写入 " + matPath + "  shader=" + shader.name
                          + "  baseMap=" + (mat.GetTexture("_BaseMap") == null ? "无" : "有"));
            }

            // ---------- 5) 手动 Humanoid 映射（自动失败时） ----------
            Avatar avatar = (srcAnim != null && autoHuman) ? srcAnim.avatar : null;
            if (avatar == null)
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                var skeleton = new SkeletonBone[all.Length];
                for (int i = 0; i < all.Length; i++)
                    skeleton[i] = new SkeletonBone
                    {
                        name = all[i].name,
                        position = all[i].localPosition,
                        rotation = all[i].localRotation,
                        scale = all[i].localScale
                    };

                var names = new HashSet<string>();
                foreach (var t in all) names.Add(t.name);
                var human = new List<HumanBone>();
                int mapped = 0;
                foreach (var kv in Map)
                {
                    if (!names.Contains(kv.Key)) continue;
                    human.Add(new HumanBone
                    {
                        boneName = kv.Key,
                        humanName = kv.Value,
                        limit = new HumanLimit { useDefaultValues = true }
                    });
                    mapped++;
                }
                Debug.Log(string.Format("[GlbRig] 手动映射命中 {0}/{1} 根 human 骨（层级共 {2} 个 Transform）",
                    mapped, Map.Count, all.Length));

                var desc = new HumanDescription
                {
                    human = human.ToArray(),
                    skeleton = skeleton,
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f,
                    hasTranslationDoF = false
                };
                avatar = AvatarBuilder.BuildHumanAvatar(inst, desc);
                if (avatar != null)
                {
                    string avPath = Path.Combine(OutDir, name + ".avatar");
                    AssetDatabase.CreateAsset(avatar, avPath);
                    Debug.Log("[GlbRig] Avatar 落盘 " + avPath);
                }
            }

            bool valid = avatar != null && avatar.isValid && avatar.isHuman;
            Debug.Log("[GlbRig] Avatar = " + (valid ? "有效 Humanoid" : "无效（isHuman="
                + (avatar != null && avatar.isHuman) + " isValid=" + (avatar != null && avatar.isValid) + "）"));

            // ---------- 6) 朝向校正 ----------
            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // 复用已有 Controller（已接 19 个 Mixamo clip，root motion 已 Bake Into Pose），
            // 这样 prefab 本身就是"能跑动画"的，上层挂载时不必再接线。
            if (animator.runtimeAnimatorController == null)
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/Resources/Art/Characters/rig/Controllers/jianxiu_male.controller");
            CheckFacing(inst, animator);

            if (smrs.Count > 0)
            {
                var smr = smrs[0];
                var b = smr.bounds;
                Debug.Log(string.Format("[GlbRig] 世界 bounds = min{0} max{1}", b.min.ToString("F3"), b.max.ToString("F3")));
                Debug.Log(string.Format("[GlbRig] ★ 实测尺寸 X={0:F3} Y={1:F3} Z={2:F3}  —— 最大维度 {3:F3}",
                    b.size.x, b.size.y, b.size.z, Mathf.Max(b.size.x, b.size.y, b.size.z)));
                DumpBone(animator, "Hips", HumanBodyBones.Hips);
                DumpBone(animator, "Head", HumanBodyBones.Head);
                DumpBone(animator, "LeftFoot", HumanBodyBones.LeftFoot);
                DumpBone(animator, "RightFoot", HumanBodyBones.RightFoot);
                DumpBone(animator, "LeftHand", HumanBodyBones.LeftHand);
                DumpBone(animator, "RightHand", HumanBodyBones.RightHand);
            }

            // ---------- 7) 落 prefab ----------
            string prefabPath = Path.Combine(OutDir, name + ".prefab");
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
            if (saved == null) { Debug.LogError("[GlbRig] prefab 保存失败 " + prefabPath); return false; }
            Debug.Log("[GlbRig] ✔ prefab = " + prefabPath);
            // 注意：avatar 可能是 FBX 子资产或已落盘的 .avatar 资产，绝不能 Destroy，否则引用变 null
            return valid;
        }

        /// <summary>
        /// 【决策记录：为什么不再往根上烘朝向旋转】
        /// 旧版 FixFacing 按绑定姿势（Animator 未驱动）测得模型面向 -X，给 prefab 根烘 +90° Y 旋转。
        /// 但 Humanoid 动画一跑，身体会**自动对齐 rig 根节点的本地 +Z**（实测 animator.bodyRotation
        /// 与手骨轴向都证实），绑定姿势朝向随之失效 —— 烘进去的 +90° 在父空间里变成"侧着走"。
        /// 空场景取证没发现，是因为相机跟着根节点一起转，偏差被抵消；游戏内相机用玩家根节点
        /// 的 forward，实测对齐偏差 88.9°，才暴露。
        /// 现在根节点保持 identity：动画态身体=本地 +Z=父节点 forward，正好达标。
        /// 绑定姿势朝向仅记录日志留档（编辑器预览用），不参与修正。
        /// </summary>
        private static void CheckFacing(GameObject inst, Animator animator)
        {
            var lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (lh == null || rh == null)
            {
                Debug.LogWarning("[GlbRig] 取不到左右手骨，跳过朝向记录");
                return;
            }
            Vector3 left = (lh.position - rh.position).normalized;
            Vector3 fwd = Vector3.Cross(Vector3.up, left).normalized;
            float raw = Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up);
            Debug.Log(string.Format(
                "[GlbRig] 绑定姿势朝向={0}（与 +Z 差 {1:F1}°）→ 根节点保持 identity（动画态由 Humanoid 对齐）",
                fwd.ToString("F2"), raw));
        }

        private static void DumpBone(Animator a, string label, HumanBodyBones hb)
        {
            var t = a.GetBoneTransform(hb);
            Debug.Log(string.Format("[GlbRig]   {0,-10} = {1}", label,
                t == null ? "未映射" : t.position.ToString("F3")));
        }

        private static int TriCount(Mesh m)
        {
            if (m == null) return 0;
            int n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i);
            return n / 3;
        }

        /// <summary>在 Tex/ 下找与该角色同名的贴图（basecolor）。</summary>
        private static string FindTexture(string name)
        {
            // 减面变体叫 xxx_d02，贴图仍叫 xxx_basecolor —— 先把变体后缀剥掉再找
            var candidates = new List<string> { name };
            int us = name.LastIndexOf('_');
            if (us > 0 && name.Length > us + 1 && name[us + 1] == 'd')
                candidates.Add(name.Substring(0, us));

            string[] dirs = { Path.Combine(FbxDir, "Tex"), FbxDir };
            string[] exts = { ".jpg", ".png", ".jpeg", ".tga" };
            foreach (var cand in candidates)
            {
                foreach (var d in dirs)
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var e in exts)
                    {
                        string p = Path.Combine(d, cand + "_basecolor" + e);
                        if (File.Exists(p)) return p.Replace('\\', '/');
                    }
                }
            }
            return null;
        }

        private static bool Contains(string[] arr, string v)
        {
            for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return true;
            return false;
        }
    }
}
