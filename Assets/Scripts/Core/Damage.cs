// 《浮生渡》领域层 · 伤害 / 治疗 / 减伤公式
// @trace 职业系统 GDD §4.1（基础公式）、§4.4（减伤乘算）
// @trace 概念文档 §9.2
namespace Fushengdu.Core
{
    public static class Damage
    {
        /// <summary>减伤总硬上限（职业系统 GDD §4.4【提案待确认】：DT_total ≤ 75%）。</summary>
        public const float MitigationHardCap = 0.75f;

        /// <summary>
        /// 伤害：Damage = (W × S + P) × K × M × R
        /// W = 武器 DPS（点/秒）；S = 武器速度（秒）；P = 攻击强度（点）；
        /// K = 技能系数；M = 乘区；R = 随机浮动 0.95~1.05
        /// </summary>
        public static float Compute(float weaponDps, float weaponSpeed, float power, float k, float m, float r)
        {
            float baseValue = weaponDps * weaponSpeed + power;
            float raw = baseValue * k * m * r;
            return raw < 0f ? 0f : raw;
        }

        /// <summary>治疗：Heal = P_h × K_h × M_h × R</summary>
        public static float ComputeHeal(float healingPower, float kh, float mh, float r)
        {
            float raw = healingPower * kh * mh * r;
            return raw < 0f ? 0f : raw;
        }

        /// <summary>
        /// 减伤乘算（GDD §4.4）：DT_total = 1 − (1−A)(1−B)(1−C)…
        /// 结果与硬上限 75% 取小，防止「无敌坦」主导策略。
        /// </summary>
        public static float CombineMitigations(params float[] mitigations)
        {
            if (mitigations == null || mitigations.Length == 0) return 0f;
            float remain = 1f;
            for (int i = 0; i < mitigations.Length; i++)
            {
                float dt = mitigations[i];
                if (dt < 0f) dt = 0f;
                else if (dt > 1f) dt = 1f;
                remain *= (1f - dt);
            }
            float total = 1f - remain;
            return total > MitigationHardCap ? MitigationHardCap : total;
        }

        /// <summary>
        /// 增伤乘区 M 的组装：M = (1+a1)(1+a2)… × (1 − DT_target) × (1 + 易伤_target)
        /// </summary>
        public static float BuildMultiplier(float targetMitigation, float targetVulnerability, params float[] bonuses)
        {
            float m = (1f - targetMitigation) * (1f + targetVulnerability);
            if (bonuses != null)
            {
                for (int i = 0; i < bonuses.Length; i++) m *= (1f + bonuses[i]);
            }
            return m < 0f ? 0f : m;
        }

        /// <summary>
        /// 狂战士独占机制「嗜血」：D_mult = 1 + B_max × (1 − HP_cur / HP_max)
        /// @trace 职业系统 GDD §2.6.2 / §4.3
        /// </summary>
        public static float BloodthirstMultiplier(float hpCur, float hpMax, float bMax)
        {
            if (hpMax <= 0f) return 1f;
            float missing = 1f - hpCur / hpMax;
            if (missing < 0f) missing = 0f;
            else if (missing > 1f) missing = 1f;
            return 1f + bMax * missing;
        }

        /// <summary>
        /// 剑修槽 6「天剑·斩虚」的等效无视护甲：
        /// K × (1 + 0.30 × 目标护甲减伤率)
        /// @trace 职业系统 GDD §2.6.1
        /// </summary>
        public static float ArmorPierceFactor(float targetArmorMitigation)
        {
            return 1f + 0.30f * targetArmorMitigation;
        }
    }
}
