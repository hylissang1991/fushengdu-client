// 《浮生渡》领域层 · 技能定义（数据驱动）
// @trace D-28（三大陆 × 7 原型 = 21 命名职业；技能三大陆完全一致）
// @trace D-28 实现铁律：技能数值一律以 ClassArchetype（7 原型）为准，绝不以 ClassId（21 命名职业）为准
// @trace 职业系统 GDD §2.6（7 原型 × 7 技能全规格）
// 本期范围：剑修 + 狂战士 两原型各 7 技能（垂直切片范围，不得扩大）。
// 位移距离与控制时长为「常量」，任何装备/词缀/Buff 都不得修改（§2.4 红线）。
namespace Fushengdu.Core
{
    /// <summary>技能标识。</summary>
    public enum SkillId
    {
        None = 0,

        // 剑修（东荒 · 物理 DPS · 敏捷 · 剑气）
        Sword_QingFengSanDie = 101, // 槽 1 青锋三叠
        Sword_JianQiZongHeng = 102, // 槽 2 剑气纵横
        Sword_LiuYunBu = 103,       // 槽 3 流云步
        Sword_DuanYueShi = 104,     // 槽 4 断岳式
        Sword_WanJianGuiZong = 105, // 槽 5 万剑归宗
        Sword_TianJianZhanXu = 106, // 槽 6 天剑·斩虚
        Sword_YuJianHuaXing = 107,  // 槽 7 御剑化形

        // 狂战士（阿斯加德 · 物理 DPS · 力量 · 怒气）
        Berserker_KuangLanShuangZhan = 201, // 槽 1 狂澜双斩
        Berserker_LieGuZhongJi = 202,       // 槽 2 裂骨重击
        Berserker_ChongFengXianZhen = 203,  // 槽 3 冲锋陷阵
        Berserker_ZhanHouHanDi = 204,       // 槽 4 战吼·撼地
        Berserker_XueNuKuangJi = 205,       // 槽 5 血怒狂击
        Berserker_ZhuShenHuangHun = 206,    // 槽 6 诸神黄昏·终斩
        Berserker_YingLingZhanQu = 207      // 槽 7 英灵战躯
    }

    /// <summary>技能槽位（1–5 小技能 / 6 终极 / 7 变身+buff）。</summary>
    public enum SkillSlot
    {
        None = 0, Fill1 = 1, Fill2 = 2, Utility1 = 3, Utility2 = 4,
        Core = 5, Ultimate = 6, Transformation = 7
    }

    /// <summary>技能形态：单体 / 自身为中心 AOE / 指定地面点 AOE / 位移 / 自身 buff。</summary>
    public enum SkillShape
    {
        SingleTarget = 0,
        SelfAoe = 1,
        PointAoe = 2,
        Displacement = 3,
        SelfBuff = 4
    }

    /// <summary>控制类型（GDD §4.5 控制递减按类型分别计时）。</summary>
    public enum ControlKind
    {
        None = 0,
        Stun = 1,     // 定身 / 眩晕 / 击晕 —— 硬控
        Fear = 2,     // 恐惧 + 击退 —— 硬控
        Slow = 3,     // 减速 —— 软控
        Silence = 4   // 沉默 —— 软控
    }

    /// <summary>姿态（剑修独占机制「御剑姿态」/ 狂战士「英灵战躯」）。</summary>
    public enum StanceId
    {
        None = 0,
        YuJian = 1,   // 御剑姿态（剑修）
        YingLing = 2  // 英灵战躯（狂战士）
    }

    /// <summary>
    /// 技能静态定义。所有数值来自职业系统 GDD §2.6，改动必须回指 GDD 条目。
    /// </summary>
    public sealed class SkillDef
    {
        public SkillId Id;
        public ClassId Class;
        public SkillSlot Slot;
        public string Name;

        public float Cooldown;      // 秒；0 = 无 CD（受 GCD 限制）
        public float Range;         // 米
        public float Radius;        // 米（AOE）
        public float CastTime;      // 秒；0 = 瞬发
        public bool UsesGcd = true; // 变身（槽 7）不占 GCD —— GDD §5.2 E-11
        public SkillShape Shape;

        // 资源：ResourceDelta > 0 表示生成，< 0 表示消耗（GDD §2.5）
        public float ResourceDelta;

        // 伤害系数（多段技能按段位存放）
        public float[] K;

        // 位移（米）—— 常量，不可被任何装备/词缀修改（GDD §2.4 红线）
        public float Displacement;

        // 控制：对怪 / 对玩家 分别标注（GDD §2.6）
        public ControlKind Control = ControlKind.None;
        public float ControlDurationVsMonster;
        public float ControlDurationVsPlayer;

        // 附加效果时长（破甲 / 减速 / 姿态 / 变身）
        public float EffectDuration;

        // 前置条件：需要姿态
        public StanceId RequiredStance = StanceId.None;

        // 目标数量上限（穿透 / AOE 上限）
        public int MaxTargets = 1;

        /// <summary>是否为变身技能（槽 7）。</summary>
        public bool IsTransformation => Slot == SkillSlot.Transformation;

        /// <summary>是否为终极技能（槽 6）。</summary>
        public bool IsUltimate => Slot == SkillSlot.Ultimate;
    }
}
