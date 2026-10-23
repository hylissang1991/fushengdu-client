// 《浮生渡》领域层 · BOSS 阶段定义表（纯 C#，不引用 UnityEngine / UnityEditor）
// 数据驱动：按 bossId 查表得到阶段序列。阈值按「剩余血量百分比」降序，index0 = 满血阶段。
// 阶段效果（增益 / 召唤 / 分场 / 联动）由 Gameplay/BossPhaseController 解释执行，本文件只持有数据。
// 设计依据：副本 GDD §2.2.3（R10 终 BOSS 三阶段）、团本 GDD §2.3.3（R20 终 BOSS 四阶段）、§2.2.3 / §2.3.3（双体 / 合体）。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>
    /// 单个 BOSS 阶段。进入条件为「当前剩余血量百分比 ≤ HpThresholdPct」。
    /// 阶段顺序按 HpThresholdPct 降序排列（index0 满血）。
    /// </summary>
    public sealed class BossPhase
    {
        /// <summary>进入本阶段所需剩余血量百分比（0~1）。</summary>
        public float HpThresholdPct = 1f;

        /// <summary>阶段标签（UI / 日志展示用）。</summary>
        public string Label = "阶段";

        /// <summary>进入本阶段时一次性乘到 WeaponDps 上的系数（1 = 不改动）。</summary>
        public float AtkMul = 1f;

        /// <summary>进入本阶段时一次性乘到 Armor 上的系数（1 = 不改动）。</summary>
        public float ArmorMul = 1f;

        /// <summary>进入本阶段时给 BOSS 注入一层吸收护盾（= 20% 最大生命）。</summary>
        public bool GrantShield;

        /// <summary>进入本阶段时在 BOSS 附近召唤的小怪 MonsterDef id 列表。</summary>
        public int[] SpawnAddsDefIds = new int[0];

        /// <summary>与 SpawnAddsDefIds 一一对应的每只小怪数量（缺省按 1 处理）。</summary>
        public int[] SpawnAddsCounts = new int[0];

        /// <summary>分场标记：进入本阶段后周期性对未「激活」区域玩家造成 AOE（简化为全场周期 DoT + 日志）。</summary>
        public bool ArenaSplit;

        /// <summary>双体 / 合体联动：共享血量（或近似）的其它 bossId 列表。非空时解释器做联动处理。</summary>
        public int[] LinkedBossIds = new int[0];

        /// <summary>本阶段周期 AOE / 终结技伤害量（0 = 不触发周期 AOE）。</summary>
        public float AoeDamage;

        /// <summary>周期 AOE 触发间隔（秒）。</summary>
        public float AoeInterval = 3f;
    }

    /// <summary>某 BOSS 的完整阶段序列。</summary>
    public sealed class BossPhaseProfile
    {
        public int BossId;
        public List<BossPhase> Phases = new List<BossPhase>();
    }

    /// <summary>
    /// BOSS 阶段表。以 bossId 为键存放阶段序列。
    /// 未显式登记的 BOSS 走 1 阶段默认档（单体承伤，无增益）。
    /// </summary>
    public sealed class BossPhaseTable
    {
        public static readonly BossPhaseTable Default = new BossPhaseTable();

        private readonly Dictionary<int, BossPhaseProfile> _byBoss = new Dictionary<int, BossPhaseProfile>();

        public BossPhaseTable()
        {
            Build();
        }

        public BossPhaseProfile Get(int bossId)
        {
            BossPhaseProfile p;
            if (_byBoss.TryGetValue(bossId, out p)) return p;
            return SinglePhase(bossId);
        }

        private static BossPhaseProfile SinglePhase(int bossId)
        {
            var p = new BossPhaseProfile { BossId = bossId };
            p.Phases.Add(new BossPhase { HpThresholdPct = 1f, Label = "单体承伤" });
            return p;
        }

        private void Add(int bossId, params BossPhase[] phases)
        {
            var p = new BossPhaseProfile { BossId = bossId };
            foreach (var ph in phases) p.Phases.Add(ph);
            _byBoss[bossId] = p;
        }

        private void Build()
        {
            // —— 605【终】内环之瞳（R10-1 终 BOSS，副本 GDD §2.2.3）：3 阶段 ——
            // P1 单体承伤；P2 分场（场地裂两半、5+5、中场同步激活）；P3 全场收束 AOE + 终结技 + 召唤少量小怪。
            Add(605,
                new BossPhase { HpThresholdPct = 1.00f, Label = "P1·单体承伤", AtkMul = 1.00f, ArmorMul = 1.00f },
                new BossPhase { HpThresholdPct = 0.66f, Label = "P2·分场(5+5同步)", AtkMul = 1.15f, ArmorMul = 1.05f, ArenaSplit = true, AoeDamage = 60f, AoeInterval = 5f },
                new BossPhase { HpThresholdPct = 0.33f, Label = "P3·全场收束AOE+终结技", AtkMul = 1.30f, ArmorMul = 1.10f, AoeDamage = 140f, AoeInterval = 4f, SpawnAddsDefIds = new[] { 3 }, SpawnAddsCounts = new[] { 4 } }
            );

            // —— 610【终】封印核心·未名（R10-2 终 BOSS，团本 GDD §2.3.3）：3 阶段 ——
            // P1 三域机制轮换每 45s；P2 核心暴露 12s 输出检测（未达标全团重伤）；P3 全场崩解三色结界站错即死。
            // 数值简化：各阶段附带 Atk 递增 + 周期 AOE（终结技近似）。
            Add(610,
                new BossPhase { HpThresholdPct = 1.00f, Label = "P1·三域轮换(每45s)", AtkMul = 1.00f, ArmorMul = 1.00f, AoeDamage = 40f, AoeInterval = 45f },
                new BossPhase { HpThresholdPct = 0.66f, Label = "P2·核心暴露(12s输出检测)", AtkMul = 1.20f, ArmorMul = 1.08f, AoeDamage = 80f, AoeInterval = 6f },
                new BossPhase { HpThresholdPct = 0.33f, Label = "P3·全场崩解三色结界", AtkMul = 1.35f, ArmorMul = 1.12f, AoeDamage = 160f, AoeInterval = 4f, SpawnAddsDefIds = new[] { 3 }, SpawnAddsCounts = new[] { 3 } }
            );

            // —— 616【终】归墟之眼（R20-1 终 BOSS，团本 GDD §2.3.3）：4 阶段 ——
            // 逐阶段递增 AtkMul（1 / 1.15 / 1.3 / 1.5），末阶段 ArenaSplit + 大 AOE。
            Add(616,
                new BossPhase { HpThresholdPct = 1.00f, Label = "P1·初临", AtkMul = 1.00f, ArmorMul = 1.00f },
                new BossPhase { HpThresholdPct = 0.75f, Label = "P2·复现三域(一)", AtkMul = 1.15f, ArmorMul = 1.05f, AoeDamage = 60f, AoeInterval = 6f },
                new BossPhase { HpThresholdPct = 0.50f, Label = "P3·复现三域(二)", AtkMul = 1.30f, ArmorMul = 1.10f, AoeDamage = 100f, AoeInterval = 5f },
                new BossPhase { HpThresholdPct = 0.25f, Label = "P4·终焉·全场收束", AtkMul = 1.50f, ArmorMul = 1.15f, ArenaSplit = true, AoeDamage = 200f, AoeInterval = 4f, SpawnAddsDefIds = new[] { 3 }, SpawnAddsCounts = new[] { 6 } }
            );

            // —— 611 终焉先驱（双体）：2 个 bossId 共享血量（近似 → 任一阵亡其余 Enrage）——
            // 阶段用 1 阶段默认 + LinkedBossIds 联动。
            Add(611, new BossPhase { HpThresholdPct = 1f, Label = "双体·承伤", LinkedBossIds = new[] { 611, 612 } });
            Add(612, new BossPhase { HpThresholdPct = 1f, Label = "双体·承伤", LinkedBossIds = new[] { 611, 612 } });

            // —— 613 三域残骸·合体：3 个 bossId 共享血量（近似 → 任一阵亡其余 Enrage）——
            Add(613, new BossPhase { HpThresholdPct = 1f, Label = "合体·承伤", LinkedBossIds = new[] { 613, 614, 615 } });
            Add(614, new BossPhase { HpThresholdPct = 1f, Label = "合体·承伤", LinkedBossIds = new[] { 613, 614, 615 } });
            Add(615, new BossPhase { HpThresholdPct = 1f, Label = "合体·承伤", LinkedBossIds = new[] { 613, 614, 615 } });
        }
    }
}
