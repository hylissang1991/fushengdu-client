// 《浮生渡》领域层 · 属性体系与派生强度
// @trace 职业系统 GDD §3.1 / §3.2 / §4.2
// @trace D-14（属性成长只来自等级与装备，不得与任何付费项挂钩）
namespace Fushengdu.Core
{
    /// <summary>五维基础属性（职业系统 GDD §3.1）。</summary>
    public enum AttributeId
    {
        Strength = 0,   // 力量 STR · 物理伤害 / 坦克威胁
        Agility = 1,    // 敏捷 AGI · 物理敏系伤害 / 暴击
        Intellect = 2,  // 智识 INT · 法术伤害
        Vitality = 3,   // 体魄 VIT · 生命上限 / 坦克生存
        Will = 4        // 意志 WIL · 治疗强度 / 资源回复
    }

    /// <summary>本期垂直切片实现的职业（完整 8 职业不在本期范围）。</summary>
    public enum ClassId
    {
        None = 0,
        SwordCultivator = 1, // 剑修 · 东荒 · 物理 DPS · 敏捷 / 力量
        Berserker = 2        // 狂战士 · 阿斯加德 · 物理 DPS · 力量 / 体魄
    }

    /// <summary>独占资源条类型（职业系统 GDD §2.5，每职业 1 条，与生命条合计 2 条）。</summary>
    public enum ResourceType
    {
        None = 0,
        Qi = 1,   // 剑修 · 剑气 Qi 0–100
        Rage = 2  // 狂战士 · 怒气 Rage 0–100
    }

    /// <summary>一组五维属性值（单位：点）。</summary>
    public struct AttributeSet
    {
        public float Strength;
        public float Agility;
        public float Intellect;
        public float Vitality;
        public float Will;

        public float Get(AttributeId id)
        {
            switch (id)
            {
                case AttributeId.Strength: return Strength;
                case AttributeId.Agility: return Agility;
                case AttributeId.Intellect: return Intellect;
                case AttributeId.Vitality: return Vitality;
                case AttributeId.Will: return Will;
                default: return 0f;
            }
        }

        public void Set(AttributeId id, float value)
        {
            switch (id)
            {
                case AttributeId.Strength: Strength = value; break;
                case AttributeId.Agility: Agility = value; break;
                case AttributeId.Intellect: Intellect = value; break;
                case AttributeId.Vitality: Vitality = value; break;
                case AttributeId.Will: Will = value; break;
            }
        }
    }

    /// <summary>
    /// 派生强度输入：五维属性 + 装备提供的攻强/法强/治疗强度。
    /// 单位全部为「点」。
    /// </summary>
    public struct PowerInput
    {
        public AttributeSet Attributes;
        public float E_AP; // 装备攻强（点）
        public float E_SP; // 装备法强（点）
        public float E_HP; // 装备治疗强度（点）
    }

    public static class Attributes
    {
        /// <summary>职业主属性。</summary>
        public static AttributeId Primary(ClassId c)
        {
            // @trace 职业系统 GDD §3.2
            return c == ClassId.SwordCultivator ? AttributeId.Agility : AttributeId.Strength;
        }

        /// <summary>职业副属性。</summary>
        public static AttributeId Secondary(ClassId c)
        {
            // 剑修 = 力量；狂战士 = 体魄
            return c == ClassId.SwordCultivator ? AttributeId.Strength : AttributeId.Vitality;
        }

        /// <summary>
        /// 攻击强度 P_AP（物理职业）。
        /// 剑修 / 狂战士：P_AP = 主属性 × 1.20 + 副属性 × 0.40 + E_AP
        /// @trace 职业系统 GDD §4.2
        /// </summary>
        public static float PhysicalPower(ClassId c, in PowerInput input)
        {
            float main = input.Attributes.Get(Primary(c));
            float sub = input.Attributes.Get(Secondary(c));
            return main * 1.20f + sub * 0.40f + input.E_AP;
        }

        /// <summary>
        /// 法术强度 P_SP。本期两职业均为物理职业，返回 0；保留公式以对齐 GDD。
        /// 药师 / 神官：P_SP = INT × 0.60 + WIL × 0.50 + E_SP
        /// </summary>
        public static float SpellPower(in PowerInput input)
        {
            return input.Attributes.Intellect * 0.60f + input.Attributes.Will * 0.50f + input.E_SP;
        }

        /// <summary>
        /// 治疗强度 P_h。本期两职业不做治疗，返回 0；保留公式以对齐 GDD §4.2。
        /// P_h = WIL × 1.20 + INT × 0.40 + E_HP
        /// </summary>
        public static float HealingPower(in PowerInput input)
        {
            return input.Attributes.Will * 1.20f + input.Attributes.Intellect * 0.40f + input.E_HP;
        }

        /// <summary>
        /// 生命上限（点）。
        /// 【工程侧假设】GDD 未给 HP 公式，本期按 VIT × 12 + 等级 × 30 + 220 线性给定，
        /// 待 S-04 属性与成长 GDD 冻结后回填（同 GDD §3.2 的【假设】口径）。
        /// </summary>
        public static float MaxHealth(in AttributeSet a, int level)
        {
            return a.Vitality * 12f + level * 30f + 220f;
        }

        /// <summary>
        /// 护甲减伤率（0~1）。
        /// 【工程侧假设】GDD §2.6.1 槽 6 引用了「目标护甲减伤率」但未给公式，
        /// 本期采用 DT_armor = armor / (armor + 400 + 20 × 等级)，待 S-04 冻结后回填。
        /// </summary>
        public static float ArmorMitigation(float armor, int level)
        {
            float k = 400f + 20f * (level < 1 ? 1 : level);
            if (armor <= 0f) return 0f;
            float dt = armor / (armor + k);
            return dt > 0.75f ? 0.75f : dt;
        }
    }
}
