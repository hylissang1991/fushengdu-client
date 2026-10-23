// 《浮生渡》领域层 · 技能结算器（服务端权威，纯 C#）
// @trace ADR-001 N-2 / N-3（客户端只发意图，服务端校验并结算）
// @trace 职业系统 GDD §2.6（逐技能公式）、§5（边缘情况）
// 本期结算只在「服务端」（LAN 场景中的主机）执行；客户端只读事件做表现。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    public enum CombatEventKind
    {
        None = 0,
        Damage,
        Heal,
        Death,
        ControlApplied,
        ControlImmune,
        BuffApplied,
        ResourceGain,
        Displacement,
        CastFailed
    }

    /// <summary>一次结算产出的事件（表现层与埋点共用）。</summary>
    public struct CombatEvent
    {
        public CombatEventKind Kind;
        public int SourceId;
        public int TargetId;
        public float Amount;
        public SkillId Skill;
        public ControlKind Control;
        public float FloatValue; // 位移距离 / 控制时长 / 新增资源量
    }

    public sealed class CombatResolver
    {
        private readonly Rng _rng;

        public CombatResolver(Rng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 执行一个技能。areaTargets 由调用方按半径与阵营预先筛选（服务端世界查询）。
        /// </summary>
        public CastFailReason Execute(
            ActorState caster,
            ActorState primaryTarget,
            IReadOnlyList<ActorState> areaTargets,
            SkillId skillId,
            List<CombatEvent> events)
        {
            SkillDef def = SkillTable.Get(skillId);
            if (def == null) return CastFailReason.UnknownSkill;

            CastFailReason fail = caster.Validate(def, primaryTarget);
            if (fail != CastFailReason.None) return fail;

            // 消耗资源（GDD §5.4 E-19：不足则置灰，不进 CD）
            if (def.ResourceDelta < 0f) caster.ChangeResource(def.ResourceDelta);

            switch (skillId)
            {
                // ---------------------------------------------------- 剑修
                case SkillId.Sword_QingFengSanDie:
                    SwordCombo(caster, primaryTarget, def, events);
                    break;
                case SkillId.Sword_JianQiZongHeng:
                    SwordQiBlast(caster, primaryTarget, areaTargets, def, events);
                    break;
                case SkillId.Sword_LiuYunBu:
                    SwordDash(caster, def, events);
                    break;
                case SkillId.Sword_DuanYueShi:
                    SwordStun(caster, primaryTarget, def, events);
                    break;
                case SkillId.Sword_WanJianGuiZong:
                    SwordAoe(caster, primaryTarget, areaTargets, def, events);
                    break;
                case SkillId.Sword_TianJianZhanXu:
                    SwordUltimate(caster, primaryTarget, def, events);
                    break;
                case SkillId.Sword_YuJianHuaXing:
                    EnterStance(caster, def, StanceId.YuJian, BuffId.YuJianStance, events);
                    break;

                // -------------------------------------------------- 狂战士
                case SkillId.Berserker_KuangLanShuangZhan:
                    BerserkerCombo(caster, primaryTarget, def, events);
                    break;
                case SkillId.Berserker_LieGuZhongJi:
                    BerserkerArmorBreak(caster, primaryTarget, def, events);
                    break;
                case SkillId.Berserker_ChongFengXianZhen:
                    BerserkerCharge(caster, areaTargets, def, events);
                    break;
                case SkillId.Berserker_ZhanHouHanDi:
                    BerserkerWarCry(caster, areaTargets, def, events);
                    break;
                case SkillId.Berserker_XueNuKuangJi:
                    BerserkerBloodStrike(caster, primaryTarget, def, events);
                    break;
                case SkillId.Berserker_ZhuShenHuangHun:
                    BerserkerExecute(caster, primaryTarget, def, events);
                    break;
                case SkillId.Berserker_YingLingZhanQu:
                    EnterStance(caster, def, StanceId.YingLing, BuffId.YingLingStance, events);
                    break;

                default:
                    return CastFailReason.UnknownSkill;
            }

            caster.StartCooldown(def);
            caster.MarkInCombat();
            return CastFailReason.None;
        }

        // ============================================================ 通用

        /// <summary>计算一次命中的最终伤害（含乘区、嗜血、浮动）。</summary>
        private float Hit(ActorState caster, ActorState target, float k, float extraBonus = 0f)
        {
            float dt = target != null ? target.TotalIncomingMitigation() : 0f;
            float bonus = caster.Buffs.OutgoingDamageBonusPct() + extraBonus;
            float m = Damage.BuildMultiplier(dt, 0f, bonus);

            // 狂战士独占机制「嗜血」：低血高伤（GDD §4.3）
            if (caster.Class == ClassId.Berserker)
                m *= Damage.BloodthirstMultiplier(caster.Hp, caster.HpMax, caster.BloodthirstBMax);

            return Damage.Compute(caster.WeaponDps, caster.WeaponSpeed, caster.PhysicalPower(), k, m, _rng.CombatRoll());
        }

        /// <summary>落地伤害并产出事件；处理吸血、受击资源、死亡。</summary>
        private void DealDamage(ActorState caster, ActorState target, float amount, SkillDef def, List<CombatEvent> events)
        {
            if (target == null || target.Dead || amount <= 0f) return;

            bool died = target.ApplyDamage(amount);
            target.MarkInCombat();

            events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Damage, SourceId = caster.Id, TargetId = target.Id,
                Amount = amount, Skill = def.Id
            });

            // 生成资源：技能指定值优先（GDD §2.5：剑修命中产 Qi / 狂战士造成伤害产 Rage）
            if (def.ResourceDelta > 0f)
            {
                float gained = caster.ChangeResource(def.ResourceDelta);
                if (gained > 0f)
                    events.Add(new CombatEvent { Kind = CombatEventKind.ResourceGain, TargetId = caster.Id, FloatValue = gained });
            }

            // 受击资源（GDD §2.5：剑修受击 +5 Qi / 狂战士受击 +8 Rage）
            float victimGain = target.ResourceId == ResourceType.Qi ? 5f : (target.ResourceId == ResourceType.Rage ? 8f : 0f);
            if (victimGain > 0f)
            {
                float g = target.ChangeResource(victimGain);
                if (g > 0f) events.Add(new CombatEvent { Kind = CombatEventKind.ResourceGain, TargetId = target.Id, FloatValue = g });
            }

            // 英灵战躯吸血 +25%（GDD §2.6.2）
            float lifesteal = caster.Buffs.LifestealPct();
            if (lifesteal > 0f)
            {
                float heal = amount * lifesteal;
                caster.ApplyHeal(heal);
                events.Add(new CombatEvent { Kind = CombatEventKind.Heal, SourceId = caster.Id, TargetId = caster.Id, Amount = heal, Skill = def.Id });
            }

            if (died)
                events.Add(new CombatEvent { Kind = CombatEventKind.Death, SourceId = caster.Id, TargetId = target.Id, Skill = def.Id });
        }

        private void ApplyControlTo(ActorState caster, ActorState target, SkillDef def, bool vsPlayer, List<CombatEvent> events)
        {
            if (target == null || target.Dead || def.Control == ControlKind.None) return;

            float dur = vsPlayer ? def.ControlDurationVsPlayer : def.ControlDurationVsMonster;
            if (dur <= 0f) return;

            bool applied = target.ApplyControl(def.Control, dur, vsPlayer);
            events.Add(new CombatEvent
            {
                Kind = applied ? CombatEventKind.ControlApplied : CombatEventKind.ControlImmune,
                SourceId = caster.Id, TargetId = target.Id, Control = def.Control,
                FloatValue = dur, Skill = def.Id
            });
        }

        private static bool IsPlayer(ActorState a)
        {
            return a != null && a.Faction == ActorFaction.Player;
        }

        // ============================================================ 剑修

        /// <summary>青锋三叠：三段连击，产 Qi 12/段。</summary>
        private void SwordCombo(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            // 流云步加成：整段合计 +0.20，按段均摊（口径待设计侧确认，保守取值）
            float bonusTotal = caster.Buffs.Has(BuffId.QingFengKBonus) ? caster.Buffs.TotalMagnitude(BuffId.QingFengKBonus) : 0f;
            float perHit = def.K.Length > 0 ? bonusTotal / def.K.Length : 0f;

            for (int i = 0; i < def.K.Length; i++)
            {
                if (target == null || target.Dead) break;
                float amount = Hit(caster, target, def.K[i] + perHit);
                DealDamage(caster, target, amount, def, events);
            }
            if (bonusTotal > 0f) caster.Buffs.Remove(BuffId.QingFengKBonus);
        }

        /// <summary>剑气纵横：穿透 2 目标，第 2 个起 ×0.70；软控减速 30%。</summary>
        private void SwordQiBlast(ActorState caster, ActorState primary, IReadOnlyList<ActorState> area, SkillDef def, List<CombatEvent> events)
        {
            int hit = 0;
            if (primary != null && !primary.Dead)
            {
                DealDamage(caster, primary, Hit(caster, primary, def.K[0]), def, events);
                ApplyControlTo(caster, primary, def, IsPlayer(primary), events);
                hit++;
            }
            if (area != null)
            {
                for (int i = 0; i < area.Count && hit < def.MaxTargets; i++)
                {
                    ActorState t = area[i];
                    if (t == null || t.Dead || t == primary) continue;
                    float k = hit >= 1 ? def.K[1] : def.K[0];
                    DealDamage(caster, t, Hit(caster, t, k), def, events);
                    ApplyControlTo(caster, t, def, IsPlayer(t), events);
                    hit++;
                }
            }
        }

        /// <summary>流云步：纯位移 8 m + 产 Qi 20 + 下一次青锋三叠 K 加成。</summary>
        private void SwordDash(ActorState caster, SkillDef def, List<CombatEvent> events)
        {
            caster.ChangeResource(def.ResourceDelta);
            caster.Buffs.Apply(BuffId.QingFengKBonus, def.EffectDuration, 0.20f, 1, 1);
            events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Displacement, SourceId = caster.Id, TargetId = caster.Id,
                FloatValue = def.Displacement, Skill = def.Id
            });
            events.Add(new CombatEvent { Kind = CombatEventKind.BuffApplied, TargetId = caster.Id, Skill = def.Id });
        }

        /// <summary>断岳式：硬控定身 + 护甲 −15% / 8 s。</summary>
        private void SwordStun(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            if (target == null || target.Dead) return;
            DealDamage(caster, target, Hit(caster, target, def.K[0]), def, events);
            ApplyControlTo(caster, target, def, IsPlayer(target), events);
            target.Buffs.Apply(BuffId.DuanYueArmorBreak, def.EffectDuration, 0.15f, 1, 1);
            events.Add(new CombatEvent { Kind = CombatEventKind.BuffApplied, TargetId = target.Id, Skill = def.Id });
        }

        /// <summary>万剑归宗：半径 6 m AOE；命中 ≥3 目标进入御剑姿态 6 s。</summary>
        private void SwordAoe(ActorState caster, ActorState primary, IReadOnlyList<ActorState> area, SkillDef def, List<CombatEvent> events)
        {
            int hitCount = 0;
            if (area != null)
            {
                for (int i = 0; i < area.Count && hitCount < def.MaxTargets; i++)
                {
                    ActorState t = area[i];
                    if (t == null || t.Dead) continue;
                    float k = hitCount == 0 ? def.K[0] : def.K[1];
                    DealDamage(caster, t, Hit(caster, t, k), def, events);
                    hitCount++;
                }
            }
            else if (primary != null && !primary.Dead)
            {
                DealDamage(caster, primary, Hit(caster, primary, def.K[0]), def, events);
                hitCount++;
            }

            // 命中 ≥3 目标进入御剑姿态 6 s（GDD §2.6.1）
            if (hitCount >= 3)
            {
                caster.Buffs.Apply(BuffId.YuJianStance, def.EffectDuration, 0f, 1, 1);
                events.Add(new CombatEvent { Kind = CombatEventKind.BuffApplied, TargetId = caster.Id, Skill = def.Id });
            }
        }

        /// <summary>天剑·斩虚：K 3.60 并等效无视 30% 护甲。</summary>
        private void SwordUltimate(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            if (target == null || target.Dead) return;
            float pierce = Damage.ArmorPierceFactor(target.ArmorMitigation());
            float amount = Hit(caster, target, def.K[0] * pierce);
            DealDamage(caster, target, amount, def, events);
        }

        // ========================================================== 狂战士

        /// <summary>狂澜双斩：两段，产 Rage 10/段。</summary>
        private void BerserkerCombo(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            for (int i = 0; i < def.K.Length; i++)
            {
                if (target == null || target.Dead) break;
                DealDamage(caster, target, Hit(caster, target, def.K[i]), def, events);
            }
        }

        /// <summary>裂骨重击：K 1.60 + 破甲 −15% / 8 s（可叠加至 −30%）。</summary>
        private void BerserkerArmorBreak(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            if (target == null || target.Dead) return;
            DealDamage(caster, target, Hit(caster, target, def.K[0]), def, events);
            // 层数上限 2（2 × 15% = 30%）
            target.Buffs.Apply(BuffId.LieGuArmorBreak, def.EffectDuration, 0.15f, 1, 2);
            events.Add(new CombatEvent { Kind = CombatEventKind.BuffApplied, TargetId = target.Id, Skill = def.Id });
        }

        /// <summary>冲锋陷阵：位移 12 m + 落地伤害。</summary>
        private void BerserkerCharge(ActorState caster, IReadOnlyList<ActorState> area, SkillDef def, List<CombatEvent> events)
        {
            caster.ChangeResource(def.ResourceDelta);
            events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Displacement, SourceId = caster.Id, TargetId = caster.Id,
                FloatValue = def.Displacement, Skill = def.Id
            });
            if (area != null)
            {
                for (int i = 0; i < area.Count; i++)
                {
                    ActorState t = area[i];
                    if (t == null || t.Dead) continue;
                    DealDamage(caster, t, Hit(caster, t, def.K[0]), def, events);
                }
            }
        }

        /// <summary>战吼·撼地：AOE 伤害 + 恐惧/击退 + 友方攻速 +5%。</summary>
        private void BerserkerWarCry(ActorState caster, IReadOnlyList<ActorState> area, SkillDef def, List<CombatEvent> events)
        {
            caster.ChangeResource(def.ResourceDelta);
            if (area == null) return;
            for (int i = 0; i < area.Count; i++)
            {
                ActorState t = area[i];
                if (t == null || t.Dead) continue;
                if (t.Faction == caster.Faction)
                {
                    // 友方：攻速 +5% / 8 s（GDD §2.6.2）
                    t.Buffs.Apply(BuffId.WarCryHaste, 8f, 0.05f, 1, 1);
                    events.Add(new CombatEvent { Kind = CombatEventKind.BuffApplied, TargetId = t.Id, Skill = def.Id });
                    continue;
                }
                DealDamage(caster, t, Hit(caster, t, def.K[0]), def, events);
                ApplyControlTo(caster, t, def, IsPlayer(t), events);
            }
        }

        /// <summary>血怒狂击：K 2.40 × (1 + 0.30 × 破甲层数)。</summary>
        private void BerserkerBloodStrike(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            if (target == null || target.Dead) return;
            int stacks = target.Buffs.StacksOf(BuffId.LieGuArmorBreak) + target.Buffs.StacksOf(BuffId.DuanYueArmorBreak);
            float k = def.K[0] * (1f + 0.30f * stacks);
            DealDamage(caster, target, Hit(caster, target, k), def, events);
        }

        /// <summary>诸神黄昏·终斩：目标 HP ≥30% → K 4.00；<30% → K 5.00（处决）。</summary>
        private void BerserkerExecute(ActorState caster, ActorState target, SkillDef def, List<CombatEvent> events)
        {
            if (target == null || target.Dead) return;
            float ratio = target.HpMax > 0f ? target.Hp / target.HpMax : 0f;
            float k = ratio < 0.30f ? def.K[1] : def.K[0];
            DealDamage(caster, target, Hit(caster, target, k), def, events);
        }

        // ============================================================ 姿态

        private void EnterStance(ActorState caster, SkillDef def, StanceId stance, BuffId buff, List<CombatEvent> events)
        {
            // E-6：变身期间再次点击槽 7 无效（外层 Validate 的 CD 校验已拦截，此处再兜一层）
            if (caster.Buffs.InStance(stance)) return;
            caster.Buffs.Apply(buff, def.EffectDuration, 0f, 1, 1);
            events.Add(new CombatEvent { Kind = CombatEventKind.BuffApplied, TargetId = caster.Id, Skill = def.Id });
        }
    }
}
