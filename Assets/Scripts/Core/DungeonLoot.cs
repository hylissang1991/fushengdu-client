// 《浮生渡》领域层 · 副本/团本掉落结算（数据驱动）
// 纯 C# 领域层（Fushengdu.Core），不引用 UnityEngine / UnityEditor。
// @trace D-06（5 人本掉落上限品质 4）/ D-19（5 人本产出一律拾取绑定）
// @trace D-26（10 人本上限次神话 6 / 20 人团本上限最高级神话 7，每团保底 1 件）
// @trace D-21（BOSS 专属唯一掉落）/ D-24（武器碎片 30 块换超神话武器）
// 本文件是 Slice 1 的「规则结算核心」：给定 LootRuleKey + bossId，返回本次应产出的若干件待分配掉落。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>
    /// 一次副本/团本运行的掉落累计状态（服务端持有，单次进入实例期间有效）。
    /// 用于跨 BOSS 的保底/上限判定（如 R20-1 团末保底 1 件最高级神话）。
    /// </summary>
    public sealed class DungeonLootRunState
    {
        /// <summary>本次运行是否已产出过最高级神话（品质 7）。团末保底仅在 0 命中时触发。</summary>
        public bool MythicDroppedThisRun;

        /// <summary>已击杀 BOSS 数（用于未来周帽/进度，本期仅作记录）。</summary>
        public int BossesKilled;

        /// <summary>本副本 BOSS 总数（用于未来周帽/进度，本期仅作记录）。</summary>
        public int TotalBosses;
    }

    /// <summary>
    /// 副本/团本掉落结算（数据驱动，严格按 GDD 红线）。
    /// 四类规则：D-06（5 人本）/ D-26-sub（10 人团本）/ D-26-myth（20 人团本）。
    /// </summary>
    public static class DungeonLoot
    {
        // —— 通用掉落占位件（按品质阶层）——
        private const int GenFine      = 6001; // 精良
        private const int GenRare      = 6002; // 稀有
        private const int GenEpic      = 6003; // 史诗
        private const int GenLegendary = 6004; // 传说
        public const int GenSubMythic  = 6005; // 次神话（品质 6）
        public const int GenMythic     = 6006; // 最高级神话（品质 7）

        // —— 武器碎片（D-24）——
        public const int WeaponShardItemDefId = 7001;
        public const int WeaponShardPerPlayer  = 2;

        // —— 关键掉率（GDD：D-06 P_unique=0.12；团本 P_unique_raid=0.20；次神话 0.10；最高级神话 0.02）——
        private const float P_Unique5      = 0.12f;
        private const float P_UniqueRaid   = 0.20f;
        private const float P_SubMythic    = 0.10f;
        private const float P_Mythic       = 0.02f;

        /// <summary>BossId → 专属唯一掉落 ItemDef id（U 系列，5001~5052）。无映射的 BOSS 不掉唯一。</summary>
        private static readonly Dictionary<int, int> BossUniqueItem = new Dictionary<int, int>
        {
            // 随机本工厂 B01–B22 → 401–422
            {401,5001},{402,5002},{403,5003},{404,5004},{405,5005},{406,5006},{407,5007},{408,5008},
            {409,5009},{410,5010},{411,5011},{412,5012},{413,5013},{414,5014},{415,5015},{416,5016},
            {417,5017},{418,5018},{419,5019},{420,5020},{421,5021},{422,5022},
            // 固定五人本 D60-1~8（主要 BOSS）
            {502,5023},{501,5024},{505,5025},{504,5026},{508,5027},{507,5028},{510,5029},{511,5030},
            {516,5031},{514,5032},{517,5033},{518,5034},{521,5035},{522,5036},{529,5037},{528,5038},
            // 10 人团本 R10-1(601~605) / R10-2(606~610)
            {601,5039},{602,5040},{603,5041},{605,5042},{606,5043},{607,5044},{608,5045},{610,5046},
            // 20 人团本 R20-1(611~616)
            {611,5047},{612,5048},{613,5049},{614,5050},{615,5051},{616,5052},
        };

        private static LootRollSession Make(int itemDefId, ItemQuality cap, bool isUnique = false, int uniqueDefId = 0)
        {
            return new LootRollSession
            {
                ItemDefId = itemDefId,
                QualityCap = cap,
                IsUnique = isUnique,
                UniqueItemDefId = uniqueDefId,
                Bind = BindType.OnPickup // 副本/团本产出自带拾取绑定（D-19 / 团本 GDD）
            };
        }

        /// <summary>
        /// 按 LootRuleKey 结算某 BOSS 的掉落，返回本次应掉落的若干件待分配掉落。
        /// isFinalBoss 用于触发团本终 BOSS 的额外保底件 / 团末保底。
        /// </summary>
        public static List<LootRollSession> RollBoss(string ruleKey, int bossId, bool isFinalBoss, Rng rng, DungeonLootRunState run)
        {
            var outDrops = new List<LootRollSession>();
            if (run != null) run.BossesKilled++;

            if (ruleKey == "D-06")
            {
                // —— 5 人本（D-06 + D-19）——
                // 红线 R-1：品质 ≥5 绝不出（W(5..8)=0）。保底 ≥1 件品质 2–3。
                outDrops.Add(Make(GenFine, ItemQuality.Rare));                 // 保底精良/稀有
                if (rng.NextFloat() < 0.35f) outDrops.Add(Make(GenRare, ItemQuality.Epic)); // 追加 L2（稀有/史诗）
                // 主要 BOSS 专属唯一掉落（P_unique = 0.12；品质 4 史诗，BoP）
                int uid;
                if (BossUniqueItem.TryGetValue(bossId, out uid) && rng.NextFloat() < P_Unique5)
                    outDrops.Add(Make(uid, ItemQuality.Epic, true, uid));
                return outDrops;
            }

            if (ruleKey == "D-26-sub")
            {
                // —— 10 人团本（D-26-sub）：上限次神话（品质 6）——
                int l3 = isFinalBoss ? 2 : 1; // 过程 BOSS ×1 件；终 BOSS ×2 件
                float legendChance = isFinalBoss ? 0.45f : 0.18f; // 终 BOSS 传说占比更高
                for (int i = 0; i < l3; i++)
                {
                    // 史诗 0.82 / 传说 0.18（过程）；终 BOSS 史诗 0.55 / 传说 0.45
                    int item = rng.NextFloat() < legendChance ? GenLegendary : GenEpic;
                    outDrops.Add(Make(item, ItemQuality.Legendary));
                }
                // 次神话判定（每 BOSS ≤1 件；随机职业原型 × 随机部位，本期用通用占位件）
                if (rng.NextFloat() < P_SubMythic)
                    outDrops.Add(Make(GenSubMythic, ItemQuality.SubMythic));
                // BOSS 专属唯一掉落（P_unique_raid = 0.20；品质 5 传说）
                int uid;
                if (BossUniqueItem.TryGetValue(bossId, out uid) && rng.NextFloat() < P_UniqueRaid)
                    outDrops.Add(Make(uid, ItemQuality.Legendary, true, uid));
                return outDrops;
            }

            if (ruleKey == "D-26-myth")
            {
                // —— 20 人团本（D-26-myth）：上限最高级神话（品质 7），每团保底 1 件 ——
                int l4 = isFinalBoss ? 4 : 2; // 过程 BOSS ×2 件传说；终 BOSS ×4 件传说
                for (int i = 0; i < l4; i++)
                    outDrops.Add(Make(GenLegendary, ItemQuality.Legendary));
                // 最高级神话判定（各大 BOSS 独立，P_mythic_boss = 0.02，品质 7）
                if (rng.NextFloat() < P_Mythic)
                {
                    outDrops.Add(Make(GenMythic, ItemQuality.Mythic));
                    if (run != null) run.MythicDroppedThisRun = true;
                }
                // BOSS 专属唯一掉落（P_unique_raid = 0.20；品质 5 传说）
                int uid;
                if (BossUniqueItem.TryGetValue(bossId, out uid) && rng.NextFloat() < P_UniqueRaid)
                    outDrops.Add(Make(uid, ItemQuality.Legendary, true, uid));
                // 团末保底 1 件品质 7（仅终 BOSS 结算；本周 0 命中时补 1 件，保底而非期望）
                if (isFinalBoss && (run == null || !run.MythicDroppedThisRun))
                    outDrops.Add(Make(GenMythic, ItemQuality.Mythic));
                return outDrops;
            }

            // 未知规则键：保守地不掉任何东西（避免越界出货）
            return outDrops;
        }
    }
}
