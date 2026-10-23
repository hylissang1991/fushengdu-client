// 《浮生渡》表现层 · 角色外观：带骨骼的 rig_glb prefab
//
// 【链路】
//   chars_rigged/*.glb（41~42 骨、无 Draco、JPEG 贴图）
//     → Blender 5.2 离线 glb2fbx_batch.py  →  <原型>_<性别>.fbx
//     → Editor/GlbRigImporter（Human 绑定 + Humanoid 映射 + 统一缩放 + 挂 Controller）
//     → Assets/Resources/Art/Characters/rig_glb/<原型>_<性别>_d02.prefab
//     → 本文件在运行时按 (职业 → 原型) + 性别 加载并挂到角色身上
//
// 【与旧链路的区别 —— 也是"角色躺倒"的根因】
//   旧版加载 CharacterMeshBaker 烘焙出的静态 .asset：只有 MeshFilter + MeshRenderer，
//   **没有骨骼、没有 Animator**，角色只能当一个不动的雕像贴地滑行；而那个烘焙结果实测
//   size=(1.13, 1.80, 1.86)（Z 比身高还大），说明源模型本身是 Z-up 躺着的。
//   现在改成挂 SkinnedMeshRenderer + 骨骼层级 + Animator Controller。
//
// 【缩放】见 TargetStandHeight —— 全项目只有这一处定义目标身高。
using System.Collections.Generic;
using Fushengdu.Core;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 七个职业原型（职业系统 GDD v2 §7.2）。**外观与技能逻辑的主键**。
    /// 三大陆（东荒 / 奥林匹斯 / 阿斯加德）同后缀共享同一原型与同一套模型，
    /// 仅名字与配色/部件不同（GDD v2 C-2 / C-3）。
    /// </summary>
    public enum CharacterArchetype : byte
    {
        None = 0,
        QianJiGe = 1,     // 千机阁 · 物理 DPS · 远程弓
        TaiXuMen = 2,     // 太虚门 · 魔法 DPS · 单体持续
        JiShiTang = 3,    // 济世堂 · 治疗
        CangZhenJiao = 4, // 苍真教 · 魔法 DPS · 群体 AOE
        XueShaGu = 5,     // 血煞谷 · 刺客 DPS · 近战爆发
        ZhenYueZong = 6,  // 镇岳宗 · 坦克 / 物理 DPS 双修
        QingYunDian = 7   // 青云殿 · 物理 DPS · 近战剑
    }

    public static class CharacterModels
    {
        /// <summary>带骨骼 prefab 所在的 Resources 子目录。</summary>
        private const string Dir = "Art/Characters/rig_glb/";

        /// <summary>
        /// 资源名后缀：**用减面版**。
        /// 源模型每个约 98~99 万三角，14 个全量 = 1370 万三角，游戏里跑不动；
        /// 减面到 2% ≈ 1.9 万三角/个。全量版（无后缀）只用于画质取证。
        /// </summary>
        private const string Suffix = "_d02";

        /// <summary>
        /// 【唯一可调常量】角色站直后的目标身高（米）。
        /// 相机锚点、CharacterController(height=1.8)、脚下选中环都是按这个数调的，
        /// 要改身高只改这一处，然后重跑 Fushengdu.Editor.GlbRigImporter 让缩放重新烘进 prefab。
        /// 实测原始绑定高度 1.014~1.031，对应缩放系数约 1.75~1.78。
        /// </summary>
        public const float TargetStandHeight = 1.8f;

        private const string ControllerRes =
            "Art/Characters/rig/Controllers/jianxiu_male";

        private static readonly Dictionary<string, GameObject> Cache =
            new Dictionary<string, GameObject>();

        /// <summary>原型 → 资源名前缀（与 glb / FBX 文件名一一对应，无需额外映射表）。</summary>
        public static string PrefixOf(CharacterArchetype a)
        {
            switch (a)
            {
                case CharacterArchetype.QianJiGe: return "qianjige";
                case CharacterArchetype.TaiXuMen: return "taixumen";
                case CharacterArchetype.JiShiTang: return "jishitang";
                case CharacterArchetype.CangZhenJiao: return "cangzhenjiao";
                case CharacterArchetype.XueShaGu: return "xueshagu";
                case CharacterArchetype.ZhenYueZong: return "zhenyuezong";
                case CharacterArchetype.QingYunDian: return "qingyundian";
                default: return null;
            }
        }

        /// <summary>
        /// 职业 → 原型。
        /// v2 的 21 职业按 (id / 100, id % 100) 反推（东荒 1xx / 奥林匹斯 2xx / 阿斯加德 3xx）；
        /// 垂直切片期遗留的 1 / 2 两个枚举值也一并兼容。
        /// 大陆差异本轮不做（三大陆共用同一套模型）。
        /// </summary>
        public static CharacterArchetype ArchetypeOf(ClassId id)
        {
            int v = (int)id;
            if (v >= 100)
            {
                int a = v % 100;
                return (a >= 1 && a <= 7) ? (CharacterArchetype)a : CharacterArchetype.None;
            }
            // 旧枚举（垂直切片期）：剑修 → 青云殿，狂战士 → 血煞谷（GDD v2 §2.4）
            if (id == ClassId.SwordCultivator) return CharacterArchetype.QingYunDian;
            if (id == ClassId.Berserker) return CharacterArchetype.XueShaGu;
            return CharacterArchetype.None;
        }

        /// <summary>Resources 路径（不含后缀），供 GlbRigImporter 与运行时共用。</summary>
        public static string ResourceOf(string prefix, bool female)
        {
            return Dir + prefix + (female ? "_female" : "_male") + Suffix;
        }

        /// <summary>保留旧 API：职业 → 基础资源名（不含性别与后缀），仅用于日志与回退判定。</summary>
        public static string JobOf(ClassId id)
        {
            return PrefixOf(ArchetypeOf(id));
        }

        /// <summary>该职业是否有可用的带骨骼建模。</summary>
        public static bool Has(ClassId id)
        {
            string job = JobOf(id);
            if (job == null) return false;
            return LoadPrefab(job, false) != null;
        }

        /// <summary>
        /// 把带骨骼的角色 prefab 挂到 parent 上。
        /// 成功返回 Animator（供上层用 Speed 驱动移动动画），失败（缺资产）返回 null 交给调用方回退。
        /// </summary>
        public static Animator Attach(Transform parent, ClassId id, bool female)
        {
            string job = JobOf(id);
            if (job == null) return null;

            GameObject prefab = LoadPrefab(job, female);
            if (prefab == null) return null;

            // 【坑位 4】这里不能传 Quaternion.identity 作为 Instantiate 的第三参，
            // 那会覆盖 prefab 根上保存的朝向校正，角色会变回侧身。
            GameObject inst = Object.Instantiate(prefab, parent, false);
            inst.name = "Rig_" + job;

            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            if (anim.runtimeAnimatorController == null)
                anim.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(ControllerRes);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // 缩放兜底：导入器已把系数烘进 prefab 根，这里是幂等归一——
            // 就算 prefab 是旧版（没烘缩放）生成的，也能在运行时拉回目标身高。
            NormalizeScale(inst);
            return anim;
        }

        /// <summary>
        /// 运行时把实例缩放到目标身高（幂等：重复调用结果一致）。
        /// 用 prefab 上记录的未缩放绑定身高，而不是渲染器当前 bounds——
            /// 后者会被动画姿势影响，站着和走着量出来的数不一样。
        /// </summary>
        public static void NormalizeScale(GameObject inst)
        {
            if (inst == null) return;
            var metrics = inst.GetComponent<CharacterModelMetrics>();
            if (metrics == null || metrics.BindHeight <= 0.001f) return;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            float bindH = metrics.BindHeight;
            float k = TargetStandHeight / bindH;
            inst.transform.localScale = new Vector3(k, k, k);
            if (smr != null) Debug.Log(string.Format(
                "[浮生渡] 缩放：绑定身高 {0:F3} × {1:F4} → {2:F2} m", bindH, k, bindH * k));
        }

        private static GameObject LoadPrefab(string job, bool female)
        {
            string res = ResourceOf(job, female);
            GameObject go;
            if (Cache.TryGetValue(res, out go) && go != null) return go;
            go = Resources.Load<GameObject>(res);
            if (go == null)
            {
                // 性别资产缺失时回退到另一套，避免出现"裸玩家"
                string alt = ResourceOf(job, !female);
                go = Resources.Load<GameObject>(alt);
                if (go != null)
                    Debug.LogWarning("[浮生渡] 角色建模缺 " + res + "，已回退 " + alt);
            }
            Cache[res] = go;
            return go;
        }
    }
}
