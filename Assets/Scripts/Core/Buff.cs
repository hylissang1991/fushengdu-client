// 《浮生渡》领域层 · 增益 / 减益容器
// @trace 职业系统 GDD §5.3（持续效果继承）、§4.5（控制与位移规则）
// 约束：DoT/HoT 不因距离清除，仅因时长 / 驱散 / 目标死亡清除（E-14）。
namespace Fushengdu.Core
{
    /// <summary>增益 / 减益标识。</summary>
    public enum BuffId
    {
        None = 0,

        // ---- 剑修 ----
        YuJianStance = 101,     // 御剑姿态：射程 +10 m、P_AP ×1.12、移速 +20%、免疫减速
        QingFengKBonus = 102,   // 流云步落地 2 s 内下一次青锋三叠 K +0.20
        DuanYueArmorBreak = 103,// 断岳式：护甲 −15% / 8 s

        // ---- 狂战士 ----
        YingLingStance = 201,   // 英灵战躯：吸血 +25%、受伤 −15%、免疫恐惧、移速 +15%
        LieGuArmorBreak = 202,  // 裂骨重击：护甲 −15% / 8 s，可叠加至 −30%
        WarCryHaste = 203,      // 战吼·撼地 友方：攻速 +5% / 8 s

        // ---- 通用 ----
        Slow = 301,             // 减速（软控）
        Silence = 302,          // 沉默（软控）
        DarkPhaseDamage = 303   // 713 光暗交替·暗期：玩家输出伤害 +25%（由 AffixRuntime 周期性施加/移除）
    }

    /// <summary>
    /// 一个增益 / 减益实例。magnitude 语义由 BuffId 决定（多为百分比小数，0.15 = +15%）。
    /// </summary>
    public struct BuffInstance
    {
        public BuffId Id;
        public float Remaining; // 秒
        public int Stacks;      // 层数
        public float Magnitude; // 每层量值
    }

    /// <summary>
    /// Buff 容器。零热路径分配：使用预分配数组 + 紧凑表，Tick 时不 new。
    /// @trace ADR-001 N-6（禁止在热路径分配）
    /// </summary>
    public sealed class BuffContainer
    {
        private const int Capacity = 24;
        private readonly BuffInstance[] _items = new BuffInstance[Capacity];
        private int _count;

        public int Count => _count;

        public void Clear()
        {
            _count = 0;
        }

        /// <summary>施加 / 刷新一个 buff。同 Id 取「时长刷新 + 层数累加」。</summary>
        public void Apply(BuffId id, float duration, float magnitude = 0f, int stacksToAdd = 1, int stackCap = 1)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id != id) continue;
                // 已有：刷新时长、累加层数（受上限约束）、量值取较大者
                if (_items[i].Remaining < duration) _items[i].Remaining = duration;
                int s = _items[i].Stacks + stacksToAdd;
                _items[i].Stacks = s > stackCap ? stackCap : s;
                if (magnitude > _items[i].Magnitude) _items[i].Magnitude = magnitude;
                return;
            }

            if (_count >= Capacity) return; // 满则丢弃（防御式，正常战斗不会触及）
            _items[_count].Id = id;
            _items[_count].Remaining = duration;
            _items[_count].Stacks = stacksToAdd > stackCap ? stackCap : stacksToAdd;
            _items[_count].Magnitude = magnitude;
            _count++;
        }

        public bool Has(BuffId id)
        {
            for (int i = 0; i < _count; i++) if (_items[i].Id == id) return true;
            return false;
        }

        public int StacksOf(BuffId id)
        {
            for (int i = 0; i < _count; i++) if (_items[i].Id == id) return _items[i].Stacks;
            return 0;
        }

        public float RemainingOf(BuffId id)
        {
            for (int i = 0; i < _count; i++) if (_items[i].Id == id) return _items[i].Remaining;
            return 0f;
        }

        /// <summary>取某 buff 的「总量值」= 层数 × 每层量值。</summary>
        public float TotalMagnitude(BuffId id)
        {
            for (int i = 0; i < _count; i++)
                if (_items[i].Id == id) return _items[i].Magnitude * _items[i].Stacks;
            return 0f;
        }

        public void Remove(BuffId id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id != id) continue;
                _items[i] = _items[_count - 1];
                _count--;
                return;
            }
        }

        /// <summary>推进时间；返回是否有 buff 到期（供表现层刷新图标）。</summary>
        public bool Tick(float dt)
        {
            bool changed = false;
            for (int i = _count - 1; i >= 0; i--)
            {
                _items[i].Remaining -= dt;
                if (_items[i].Remaining > 0f) continue;
                _items[i] = _items[_count - 1];
                _count--;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// 护甲削减百分比（护甲 −x%）。裂骨重击可叠加至 −30%（GDD §2.6.2）。
        /// </summary>
        public float ArmorReductionPct()
        {
            float sum = TotalMagnitude(BuffId.DuanYueArmorBreak) + TotalMagnitude(BuffId.LieGuArmorBreak);
            // 硬上限 30% —— GDD §2.6.2「可叠加至 −30%」
            return sum > 0.30f ? 0.30f : sum;
        }

        /// <summary>减速百分比（取最大值，不叠加）。</summary>
        public float SlowPct()
        {
            return TotalMagnitude(BuffId.Slow);
        }

        /// <summary>输出增伤百分比（进 M 乘区）。</summary>
        public float OutgoingDamageBonusPct()
        {
            // 713 光暗交替·暗期：玩家伤害 +25%（由 AffixRuntime 周期性施加 DarkPhaseDamage buff）。
            return TotalMagnitude(BuffId.DarkPhaseDamage);
        }

        /// <summary>受到伤害减免百分比（进减伤乘算）。</summary>
        public float IncomingMitigationPct()
        {
            // 英灵战躯 −15%
            return Has(BuffId.YingLingStance) ? 0.15f : 0f;
        }

        /// <summary>吸血百分比（造成伤害转化为治疗的比例）。</summary>
        public float LifestealPct()
        {
            return Has(BuffId.YingLingStance) ? 0.25f : 0f;
        }

        /// <summary>移速加成百分比。</summary>
        public float MoveSpeedBonusPct()
        {
            float v = 0f;
            if (Has(BuffId.YuJianStance)) v += 0.20f;
            if (Has(BuffId.YingLingStance)) v += 0.15f;
            return v;
        }

        /// <summary>免疫减速（御剑姿态）。注意：免疫减速 ≠ 免控（GDD §2.6.1 明确）。</summary>
        public bool ImmuneSlow()
        {
            return Has(BuffId.YuJianStance);
        }

        /// <summary>免疫恐惧（英灵战躯）。</summary>
        public bool ImmuneFear()
        {
            return Has(BuffId.YingLingStance);
        }

        public bool InStance(StanceId stance)
        {
            switch (stance)
            {
                case StanceId.YuJian: return Has(BuffId.YuJianStance);
                case StanceId.YingLing: return Has(BuffId.YingLingStance);
                default: return false;
            }
        }

        /// <summary>读取（用于 UI 与存档序列化）。</summary>
        public BuffInstance[] Snapshot()
        {
            var result = new BuffInstance[_count];
            for (int i = 0; i < _count; i++) result[i] = _items[i];
            return result;
        }
    }
}
