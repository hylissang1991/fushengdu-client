// 《浮生渡》编辑器工具 · 导入 Tripo 绑骨 FBX 并映射为 Unity Humanoid
//
// 【背景】Mixamo 注册受阻，改走 Tripo（用户已有账号，旧项目 14 个角色从它导出）。
// Tripo 的骨名是 Root/Hip/Pelvis/L_Thigh/L_Calf/L_Foot/L_ToeBase/Waist/Spine01/Spine02/
// NeckTwist01/NeckTwist02/Head/L_Clavicle/L_Upperarm/L_Forearm/L_Hand…（41 骨），
// 不是 Mixamo 的 mixamorig:*，Unity 的自动 Humanoid 识别大概率配不上。
// 本工具：① 先试自动识别；② 失败就按下面的映射表手动写 HumanDescription；③ 验证 Avatar 有效。
//
// 【为什么值得这么做】Mixamo 动画（已导入 19 个，全是 Humanoid）能自动重定向到
// **任何** Humanoid 角色——映射成功就等于"一套动画驱动所有职业"。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.TripoRigImporter.Run -logFile <log>
//   （FBX 放在 Assets/Art/Characters/Rigged/<名字>.fbx）
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class TripoRigImporter
    {
        private const string Dir = "Assets/Art/Characters/Rigged";

        // Tripo 骨名 → Unity Humanoid 槽位（与旧项目 js/retarget.js 的 BONE_MAP 同构，
        // 那边是 mixamorig→Tripo，这边是 Tripo→Humanoid）
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Hip",          "Hips" },
            { "Spine01",      "Spine" },
            { "Spine02",      "Chest" },
            { "NeckTwist01",  "Neck" },
            { "Head",         "Head" },
            { "L_Clavicle",   "LeftShoulder" },
            { "R_Clavicle",   "RightShoulder" },
            { "L_Upperarm",   "LeftUpperArm" },
            { "L_Forearm",    "LeftLowerArm" },
            { "L_Hand",       "LeftHand" },
            { "R_Upperarm",   "RightUpperArm" },
            { "R_Forearm",    "RightLowerArm" },
            { "R_Hand",       "RightHand" },
            { "L_Thigh",      "LeftUpperLeg" },
            { "L_Calf",       "LeftLowerLeg" },
            { "L_Foot",       "LeftFoot" },
            { "L_ToeBase",    "LeftToes" },
            { "R_Thigh",      "RightUpperLeg" },
            { "R_Calf",       "RightLowerLeg" },
            { "R_Foot",       "RightFoot" },
            { "R_ToeBase",    "RightToes" },
        };

        [MenuItem("浮生渡/10. 导入 Tripo 绑骨 FBX", priority = 100)]
        public static void Run()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { Dir });
            if (guids.Length == 0)
            {
                Debug.LogWarning("[TripoRig] " + Dir + " 里没有 FBX——等用户上传后重跑");
                EditorApplication.Exit(0);
                return;
            }

            int ok = 0, bad = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Process(path)) ok++; else bad++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log(string.Format("[TripoRig] 完成：{0} 个 Humanoid 有效，{1} 个失败", ok, bad));
            EditorApplication.Exit(bad > 0 ? 1 : 0);
        }

        private static bool Process(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogError("[TripoRig] 无导入器 " + path); return false; }

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) { Debug.LogError("[TripoRig] 加载失败 " + path); return false; }

            // —— 先试 Unity 自动 Humanoid 识别
            imp.animationType = ModelImporterAnimationType.Human;
            imp.SaveAndReimport();
            var anim = go.GetComponent<Animator>();
            if (anim != null && anim.avatar != null && anim.avatar.isValid && anim.avatar.isHuman)
            {
                Debug.Log(string.Format("[TripoRig] {0}：自动识别为 Humanoid，无需手动映射", name));
                return true;
            }

            // —— 自动失败：按映射表手动写 HumanDescription
            var all = go.GetComponentsInChildren<Transform>(true);
            var bones = new List<string>();
            foreach (var t in all) bones.Add(t.name);

            var human = new HumanBone[Map.Count];
            int i = 0, mapped = 0;
            foreach (var kv in Map)
            {
                human[i] = new HumanBone
                {
                    boneName = kv.Key,
                    humanName = kv.Value,
                    limit = new HumanLimit { useDefaultValues = true }
                };
                if (bones.Contains(kv.Key)) mapped++;
                i++;
            }

            var skeleton = new SkeletonBone[all.Length];
            for (int b = 0; b < all.Length; b++)
            {
                var t = all[b];
                skeleton[b] = new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                };
            }

            var desc = new HumanDescription
            {
                human = human,
                skeleton = skeleton,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            // 用 SerializedObject 写进导入器的 HumanDescription（ModelImporter 没直接暴露这个属性）
            var so = new SerializedObject(imp);
            var prop = so.FindProperty("m_HumanDescription");
            if (prop == null)
            {
                Debug.LogError("[TripoRig] 找不到 m_HumanDescription（Unity 版本差异？）");
                return false;
            }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(desc), desc); // 保留结构占位，实际写入走下面的逐字段
            // HumanDescription 不是简单可 Json 序列化到 SerializedProperty 的类型，逐字段写：
            WriteHuman(prop, human);
            WriteSkeleton(prop, skeleton);
            so.ApplyModifiedProperties();
            imp.SaveAndReimport();

            anim = go.GetComponent<Animator>();
            bool valid = anim != null && anim.avatar != null && anim.avatar.isValid && anim.avatar.isHuman;
            Debug.Log(string.Format("[TripoRig] {0}：手动映射 {1}/{2} 骨 → Humanoid={3}",
                name, mapped, Map.Count, valid ? "有效" : "失败"));
            if (!valid) Debug.LogError("[TripoRig] 骨名对不上？FBX 实际骨名：" + string.Join(", ", bones.Take(50)));
            return valid;
        }

        private static void WriteHuman(SerializedProperty prop, HumanBone[] human)
        {
            var arr = prop.FindPropertyRelative("human");
            if (arr == null) return;
            arr.arraySize = human.Length;
            for (int i = 0; i < human.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("boneName").stringValue = human[i].boneName;
                el.FindPropertyRelative("humanName").stringValue = human[i].humanName;
                var limit = el.FindPropertyRelative("limit");
                if (limit != null) limit.FindPropertyRelative("useDefaultValues").boolValue = true;
            }
        }

        private static void WriteSkeleton(SerializedProperty prop, SkeletonBone[] skeleton)
        {
            var arr = prop.FindPropertyRelative("skeleton");
            if (arr == null) return;
            arr.arraySize = skeleton.Length;
            for (int i = 0; i < skeleton.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("name").stringValue = skeleton[i].name;
                var p = el.FindPropertyRelative("position");
                p.vector3Value = skeleton[i].position;
                var r = el.FindPropertyRelative("rotation");
                r.quaternionValue = skeleton[i].rotation;
                var s = el.FindPropertyRelative("scale");
                s.vector3Value = skeleton[i].scale;
            }
        }
    }
}
