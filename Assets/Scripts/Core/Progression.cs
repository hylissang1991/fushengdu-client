// 《浮生渡》领域层 · 等级与成长（本期 1–60 级）
// @trace 职业系统 GDD §3.2（属性体系）、S-04 属性与成长
// 【工程侧假设】GDD 未冻结经验曲线与每级属性成长数值，本文给出可复算初值，
// 待 S-04 冻结后回填（与 GDD §3.2「60 级裸装主属性 320 点【假设】」同一口径）。
namespace Fushengdu.Core
{
    /// <summary>垂直切片等级上限。</summary>
    public static class Progression
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 60;

        /// <summary>
        /// 升到下一级所需经验：XpToNext(L) = 200 + 60 × (L−1)^1.7
        /// L1→2: 200；L7→8: 1,406；L14→15: 4,360；L59→60: ≈61,260。
        /// 曲线沿用「本期 1–15 级」口径，1→60 累计量级显著增大（待 S-04 冻结后回填）。
        /// </summary>
        public static int XpToNext(int level)
        {
            if (level < MinLevel) level = MinLevel;
            if (level >= MaxLevel) return 0;
            double v = 200.0 + 60.0 * System.Math.Pow(level - 1, 1.7);
            int r = (int)System.Math.Round(v / 10.0) * 10;
            return r < 10 ? 10 : r;
        }

        /// <summary>击杀怪物经验：25 + 10 × 怪物等级（点）。</summary>
        public static int XpForMonster(int monsterLevel)
        {
            return 25 + 10 * monsterLevel;
        }

        /// <summary>任务完成经验：150 + 60 × 任务等级（点）。</summary>
        public static int XpForQuest(int questLevel)
        {
            return 150 + 60 * questLevel;
        }

        /// <summary>击杀普通怪物获得的渡银：4 + 怪物等级 × 3（点）。纯线性口径。</summary>
        public static int SilverForMonster(int monsterLevel)
        {
            return 4 + monsterLevel * 3;
        }

        // BOSS / 团本终boss 走「差异化保底」，区别于普通怪的纯线性：
        //  · 保底基数确保即便低等级 BOSS 也发放有意义的渡银；
        //  · 每级系数更高，奖励随等级显著放大。
        // 【工程侧假设】以下数值为可复算初值，待 S-04 经济数值冻结后回填（与 §3.2 同口径）。
        private const int BossSilverFloor         = 150;  // BOSS 渡银保底
        private const int BossSilverPerLevel      = 6;    // BOSS 每级系数
        private const int RaidFinalSilverFloor    = 800;  // 团本终boss 渡银保底
        private const int RaidFinalSilverPerLevel = 14;   // 团本终boss 每级系数

        /// <summary>BOSS 渡银：差异化保底 = 保底基数 + 怪物等级 × 系数。高于同等级普通怪。</summary>
        public static int SilverForBoss(int bossLevel)
        {
            return BossSilverFloor + bossLevel * BossSilverPerLevel;
        }

        /// <summary>团本（mythic D-26-myth）终 BOSS 渡银：最高保底，作为团本毕业战的额外奖励。</summary>
        public static int SilverForRaidFinalBoss(int bossLevel)
        {
            return RaidFinalSilverFloor + bossLevel * RaidFinalSilverPerLevel;
        }

        /// <summary>
        /// 每级属性成长：主属性 +5、副属性 +3、其余 +2（点/级）。
        /// L1 基础：主 20、副 12、其余 10；L60 主属性 ≈ 20 + 59×5 = 315（对齐 GDD §3.2「60 级裸装主属性 320 点【假设】」）。
        /// </summary>
        public static AttributeSet AttributesAt(ClassId c, int level)
        {
            int lv = level < MinLevel ? MinLevel : (level > MaxLevel ? MaxLevel : level);
            int gain = lv - 1;

            AttributeId primary = Attributes.Primary(c);
            AttributeId secondary = Attributes.Secondary(c);

            var a = new AttributeSet { Strength = 10, Agility = 10, Intellect = 10, Vitality = 10, Will = 10 };
            a.Set(secondary, 12);
            a.Set(primary, 20);

            for (int i = 0; i < 5; i++)
            {
                AttributeId id = (AttributeId)i;
                float add = id == primary ? 5f : (id == secondary ? 3f : 2f);
                a.Set(id, a.Get(id) + add * gain);
            }
            return a;
        }

        /// <summary>把 ActorState 的属性与生命上限刷新到指定等级。</summary>
        public static void ApplyLevel(ActorState actor, int level)
        {
            int lv = level < MinLevel ? MinLevel : (level > MaxLevel ? MaxLevel : level);
            actor.Level = lv;
            actor.BaseAttributes = AttributesAt(actor.Class, lv);
            float hpMax = Attributes.MaxHealth(actor.BaseAttributes, lv);
            // 保持当前血量百分比，避免升级瞬间「回满」造成节奏失真
            float ratio = actor.HpMax > 0f ? actor.Hp / actor.HpMax : 1f;
            actor.HpMax = hpMax;
            actor.Hp = hpMax * (ratio > 1f ? 1f : ratio);
        }
    }

    /// <summary>角色成长进度（可序列化，进本地存档）。</summary>
    public sealed class ProgressionState
    {
        public int Level = 1;
        public int Xp;

        /// <summary>累计击杀数（任务与埋点用）。</summary>
        public int TotalKills;

        /// <summary>加经验，返回升到的新等级（未升级返回原等级）。</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0) return Level;
            Xp += amount;
            while (Level < Progression.MaxLevel)
            {
                int need = Progression.XpToNext(Level);
                if (Xp < need) break;
                Xp -= need;
                Level++;
            }
            if (Level >= Progression.MaxLevel) Xp = 0;
            return Level;
        }

        /// <summary>当前等级进度比例 0~1（经验条用）。</summary>
        public float ProgressRatio()
        {
            int need = Progression.XpToNext(Level);
            return need <= 0 ? 1f : (float)Xp / need;
        }
    }
}
