// 《浮生渡》领域层 · 战斗单位状态（纯 C#，不含任何 Unity / 网络类型）
// @trace ADR-001 N-1（领域层不得继承 MonoBehaviour / NetworkBehaviour）
// 本对象是「服务器真相」：只有服务端（本期为 LAN 主机）可写入，客户端只读并表现。
using System;
namespace Fushengdu.Core
{
    public enum ActorFaction
    {
        Neutral = 0,
        Player = 1,
        Monster = 2
    }

    /// <summary>施法失败原因（供 UI 提示与客户端输入校验复用）。</summary>
    public enum CastFailReason
    {
        None = 0,
        UnknownSkill,
        WrongClass,
        OnCooldown,
        OnGlobalCooldown,
        NotEnoughResource,
        MissingStance,
        Dead,
        Controlled,
        NoTarget,
        OutOfRange,
        Silenced
    }

    /// <summary>
    /// 一个可战斗单位的完整状态。所有时间单位为秒，所有数值单位为「点」。
    /// </summary>
    public sealed class ActorState
    {
        // ------------------------------------------------------------ 身份
        public int Id;
        public string Name = "单位";
        public ClassId Class = ClassId.None;
        public ActorFaction Faction = ActorFaction.Neutral;
        public int Level = 1;
        public bool IsBoss;
        public bool IsElite;

        // ------------------------------------------------------------ 生存
        public float Hp;
        public float HpMax;
        public float Shield;                 // 吸收护盾（0 = 无；BOSS 阶段机 / 石肤词缀使用）
        public float DamageTakenMul = 1f;    // 受伤乘算（712 献祭词缀：未肃清祭坛守卫的终 BOSS = 0.4，即减伤 60%）

        /// <summary>
        /// 双体 / 合体真实共享血量钩子：非 null 时，ApplyDamage 在扣盾之后将伤害重定向到共享池（返回 true 表示已处理）。
        /// 仅由 Gameplay/SharedHpGroup 设置；Core 层本体保持纯 C#（仅持委托引用，不依赖 UnityEngine）。
        /// </summary>
        public System.Func<float, bool> SharedHpDamageSink;

        // ------------------------------------------------------------ 资源
        public ResourceType ResourceId = ResourceType.None;
        public float Resource;
        public float ResourceMax = 100f;

        // ------------------------------------------------------------ 属性与装备
        public AttributeSet BaseAttributes; // 等级裸装
        public float E_AP;                  // 装备攻强
        public float E_SP;                  // 装备法强（本期未用）
        public float E_HP;                  // 装备治疗强度（本期未用）
        public float WeaponDps;             // W 武器 DPS（点/秒）
        public float WeaponSpeed = 2.0f;    // S 武器速度（秒）
        public float Armor;                 // 护甲（点）

        // ------------------------------------------------------------ 运行时
        public readonly BuffContainer Buffs = new BuffContainer();
        public ControlKind Control = ControlKind.None;
        public float ControlRemaining;
        public bool Dead;
        public float RespawnTimer;

        // 冷却：按技能槽 1–7 索引（每槽固定 1 个技能）
        private readonly float[] _cooldowns = new float[8];
        public float GcdRemaining;
        public float OutOfCombatTimer;
        public float CastTimeRemaining;
        public SkillId PendingSkill = SkillId.None;

        // 控制递减（PvP）：同类控制 15 s 内 100% / 50% / 25% / 免疫（GDD §4.5）
        private readonly int[] _controlApplications = new int[8];
        private readonly float[] _controlResetTimers = new float[8];

        // 狂战士「嗜血」上限（GDD §2.6.2：B_max 基准 0.40，词缀上限 0.50）
        public float BloodthirstBMax = 0.40f;

        // ------------------------------------------------------------ 派生
        public float PhysicalPower()
        {
            var input = new PowerInput { Attributes = BaseAttributes, E_AP = E_AP, E_SP = E_SP, E_HP = E_HP };
            float p = Attributes.PhysicalPower(Class, input);
            // 御剑姿态：P_AP × 1.12（GDD §4.3）
            return Buffs.Has(BuffId.YuJianStance) ? p * 1.12f : p;
        }

        /// <summary>护甲（已计入破甲 buff，百分比削减，上限 30%）。</summary>
        public float EffectiveArmor()
        {
            return Armor * (1f - Buffs.ArmorReductionPct());
        }

        /// <summary>护甲减伤率（0~1）。</summary>
        public float ArmorMitigation()
        {
            return Attributes.ArmorMitigation(EffectiveArmor(), Level);
        }

        /// <summary>总减伤（护甲 + buff），乘算后受 75% 硬上限约束。</summary>
        public float TotalIncomingMitigation()
        {
            return Damage.CombineMitigations(ArmorMitigation(), Buffs.IncomingMitigationPct());
        }

        public bool IsInCombat => OutOfCombatTimer < 5.0f;

        public void MarkInCombat()
        {
            OutOfCombatTimer = 0f;
        }

        // ------------------------------------------------------------ 冷却
        public float CooldownOf(SkillSlot slot)
        {
            int i = (int)slot;
            return i >= 0 && i < _cooldowns.Length ? _cooldowns[i] : 0f;
        }

        public void StartCooldown(SkillDef def)
        {
            if (def == null) return;
            int i = (int)def.Slot;
            if (i < 0 || i >= _cooldowns.Length) return;
            _cooldowns[i] = def.Cooldown;
            if (def.UsesGcd) GcdRemaining = SkillTable.GlobalCooldown;
        }

        /// <summary>
        /// 重掷某技能的冷却（714 星序紊乱：每次施放时 CD 随机 ±20%）。
        /// factor 会被夹在 [0.5, 1.5] 防御越界；不改槽位与 GCD 触发逻辑。
        /// </summary>
        public void JitterCooldown(SkillDef def, float factor)
        {
            if (def == null) return;
            int i = (int)def.Slot;
            if (i < 0 || i >= _cooldowns.Length) return;
            if (factor < 0.5f) factor = 0.5f; else if (factor > 1.5f) factor = 1.5f;
            _cooldowns[i] = def.Cooldown * factor;
            if (def.UsesGcd) GcdRemaining = SkillTable.GlobalCooldown * factor;
        }

        // ------------------------------------------------------------ 资源
        /// <summary>增减资源，返回实际变化量。</summary>
        public float ChangeResource(float delta)
        {
            float before = Resource;
            Resource += delta;
            if (Resource < 0f) Resource = 0f;
            else if (Resource > ResourceMax) Resource = ResourceMax;
            return Resource - before;
        }

        // ------------------------------------------------------------ 生命
        /// <summary>施加已结算的伤害（已过减伤）。返回是否致死。</summary>
        public bool ApplyDamage(float amount)
        {
            if (Dead || amount <= 0f) return false;
            // 受伤乘算（712 献祭词缀减伤等）
            amount *= DamageTakenMul;
            // 先扣护盾（BOSS 阶段机 / 石肤词缀注入的吸收护盾）
            if (Shield > 0f)
            {
                float absorbed = amount < Shield ? amount : Shield;
                Shield -= absorbed;
                amount -= absorbed;
                if (amount <= 0f) return false;
            }
            // 双体 / 合体真实共享血量：伤害重定向到共享池（由 Gameplay/SharedHpGroup 处理血量同步与全灭）
            if (SharedHpDamageSink != null) return SharedHpDamageSink(amount);
            Hp -= amount;
            if (Hp > 0f) return false;
            Hp = 0f;
            Dead = true;
            // 死亡时清除持续效果（GDD §5.3 E-18 口径）
            Buffs.Clear();
            Control = ControlKind.None;
            ControlRemaining = 0f;
            return true;
        }

        public void ApplyHeal(float amount)
        {
            if (Dead || amount <= 0f) return;
            Hp += amount;
            if (Hp > HpMax) Hp = HpMax;
        }

        public void ResetToFull()
        {
            Hp = HpMax;
            Shield = 0f;
            Dead = false;
            RespawnTimer = 0f;
            Control = ControlKind.None;
            ControlRemaining = 0f;
            Buffs.Clear();
            for (int i = 0; i < _cooldowns.Length; i++) _cooldowns[i] = 0f;
            GcdRemaining = 0f;
        }

        // ------------------------------------------------------------ 控制
        /// <summary>
        /// 施加控制。返回是否生效（false = 被免疫或抗性归零）。
        /// 递减与抗性在此统一处理，保证 PvP/PvE 一致（GDD §4.5）。
        /// </summary>
        public bool ApplyControl(ControlKind kind, float baseDuration, bool isPvPTarget)
        {
            if (kind == ControlKind.None || baseDuration <= 0f) return false;

            // 免疫分支：御剑姿态免疫减速；英灵战躯免疫恐惧（≠ 免控）
            if (kind == ControlKind.Slow && Buffs.ImmuneSlow()) return false;
            if (kind == ControlKind.Fear && Buffs.ImmuneFear()) return false;

            float duration = baseDuration;

            // BOSS / 精英抗性（GDD §4.5）
            if (IsBoss)
            {
                // 击晕 / 恐惧类对 BOSS 完全免疫
                if (kind == ControlKind.Stun || kind == ControlKind.Fear) return false;
                duration *= 0.30f;
                if (kind == ControlKind.Slow) duration *= 0.50f;
            }
            else if (IsElite)
            {
                duration *= 0.60f;
                if (kind == ControlKind.Slow) duration *= 0.75f;
            }

            // PvP 同类控制递减（GDD §4.5 / E-27 按类型分别计时）
            if (isPvPTarget && kind != ControlKind.Slow)
            {
                int idx = (int)kind;
                if (_controlResetTimers[idx] <= 0f) _controlApplications[idx] = 0;
                _controlResetTimers[idx] = 15f;
                int n = _controlApplications[idx];
                if (n >= 3) return false; // 第 4 次起免疫（E-26：仍消耗 CD）
                float factor = n == 0 ? 1.0f : (n == 1 ? 0.5f : 0.25f);
                duration *= factor;
                _controlApplications[idx] = n + 1;
            }

            if (duration <= 0f) return false;

            // 硬控覆盖软控；同类刷新时长
            if (Control == ControlKind.None || Control == kind || IsHardControl(kind))
            {
                Control = kind;
                if (ControlRemaining < duration) ControlRemaining = duration;
            }
            else if (kind == ControlKind.Slow)
            {
                // 软控以 buff 形式叠加，不打断硬控
                Buffs.Apply(BuffId.Slow, duration, 0.30f, 1, 1);
            }
            return true;
        }

        public static bool IsHardControl(ControlKind kind)
        {
            return kind == ControlKind.Stun || kind == ControlKind.Fear;
        }

        public bool IsSilenced()
        {
            return Control == ControlKind.Silence || Buffs.Has(BuffId.Silence);
        }

        // ------------------------------------------------------------ 施法校验
        /// <summary>
        /// 服务端施法前置校验（ADR-001 N-3：所有请求必须做合法性校验，不信任客户端结论）。
        /// </summary>
        public CastFailReason Validate(SkillDef def, ActorState target)
        {
            if (def == null) return CastFailReason.UnknownSkill;
            if (Dead) return CastFailReason.Dead;
            if (def.Class != Class) return CastFailReason.WrongClass;

            // 硬控中不可施法；沉默阻断有施法条的法术（GDD §5.2 E-10）
            if (Control != ControlKind.None && IsHardControl(Control)) return CastFailReason.Controlled;
            if (IsSilenced() && def.CastTime > 0f) return CastFailReason.Silenced;

            if (CooldownOf(def.Slot) > 0f) return CastFailReason.OnCooldown;
            if (def.UsesGcd && GcdRemaining > 0f) return CastFailReason.OnGlobalCooldown;
            if (def.RequiredStance != StanceId.None && !Buffs.InStance(def.RequiredStance)) return CastFailReason.MissingStance;
            if (def.ResourceDelta < 0f && Resource + def.ResourceDelta < -0.0001f) return CastFailReason.NotEnoughResource;

            if (def.Shape == SkillShape.SingleTarget)
            {
                if (target == null || target.Dead) return CastFailReason.NoTarget;
                // 射程由调用方按世界坐标校验后传入；这里只做目标存在性校验
            }
            return CastFailReason.None;
        }

        // ------------------------------------------------------------ 时间推进
        public void Tick(float dt)
        {
            for (int i = 0; i < _cooldowns.Length; i++)
                if (_cooldowns[i] > 0f) _cooldowns[i] = _cooldowns[i] - dt > 0f ? _cooldowns[i] - dt : 0f;

            if (GcdRemaining > 0f) GcdRemaining = GcdRemaining - dt > 0f ? GcdRemaining - dt : 0f;
            if (CastTimeRemaining > 0f) CastTimeRemaining = CastTimeRemaining - dt > 0f ? CastTimeRemaining - dt : 0f;

            if (ControlRemaining > 0f)
            {
                ControlRemaining -= dt;
                if (ControlRemaining <= 0f)
                {
                    ControlRemaining = 0f;
                    Control = ControlKind.None;
                }
            }

            for (int i = 0; i < _controlResetTimers.Length; i++)
            {
                if (_controlResetTimers[i] <= 0f) continue;
                _controlResetTimers[i] -= dt;
                if (_controlResetTimers[i] <= 0f)
                {
                    _controlResetTimers[i] = 0f;
                    _controlApplications[i] = 0;
                }
            }

            if (Buffs.Tick(dt)) { /* 表现层可据此刷新图标 */ }

            // 脱战计时（资源衰减的触发条件，GDD §2.5）
            if (!Dead)
            {
                OutOfCombatTimer += dt;
                if (OutOfCombatTimer > 5.0f)
                {
                    // 剑修：脱战 5 s 后每秒 −5，归 0
                    // 狂战士：脱战 5 s 后每秒 −6，归 20
                    if (ResourceId == ResourceType.Qi)
                    {
                        ChangeResource(-5f * dt);
                    }
                    else if (ResourceId == ResourceType.Rage)
                    {
                        float target = 20f;
                        if (Resource > target)
                        {
                            Resource -= 6f * dt;
                            if (Resource < target) Resource = target;
                        }
                    }
                }
            }

            // 注意：复活倒计时**不在**这里推进。
            // 它由权威端通过 Core.RespawnRules.Advance 单点推进（见 CombatWorld.TickRespawns），
            // 避免客户端与服务端各减一次造成状态分叉（缺陷 N-1 修复）。
        }
    }
}
