// 《浮生渡》领域层 · 掉落与拾取分配
// @trace D-19（5 人本产出一律拾取绑定；拾取 = 需要 / 掷骰子 / 放弃）
// @trace D-06（5 人本掉落上限品质 4 史诗；按品质 8 阶阶梯，绝不掉品质 ≥5）
// @trace D-26（10 人本不掉最高级神话、上限次神话；20 人团本才掉最高级神话，各大 BOSS 极低爆率 + 每团保底 1 件）
// @trace 副本系统 GDD §2.5.2 / §2.5.3 / EQ-1
// @trace 副本系统 GDD E-9：服务端掷骰，客户端提交的点数一律忽略（防外挂）
// @trace D-14：掉落率与战斗数值不得与任何付费项挂钩
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>掉落来源。</summary>
    public enum LootSource
    {
        World = 0,   // 野外 / 任务：凡品可交易、精良 BoE
        Party = 1    // 5 人本（本期 LAN 组队）：一律拾取绑定 BoP
    }

    /// <summary>拾取三选一。</summary>
    public enum LootChoice
    {
        None = 0,  // 未选择 / 超时
        Need = 1,  // 需要
        Greed = 2, // 掷骰子
        Pass = 3   // 放弃
    }

    public struct LootTableEntry
    {
        public int ItemDefId;
        public float Chance; // 0~1
    }

    /// <summary>掉落表（服务端持有，客户端不可见概率细节之外的结果）。</summary>
    public sealed class LootTable
    {
        private readonly List<LootTableEntry> _entries = new List<LootTableEntry>();
        private float _total;

        public void Add(int itemDefId, float chance)
        {
            _entries.Add(new LootTableEntry { ItemDefId = itemDefId, Chance = chance });
            _total += chance;
        }

        /// <summary>掷一次掉落；返回 defId，0 = 未命中。</summary>
        public int Roll(Rng rng, out float rollValue)
        {
            rollValue = rng.NextFloat();
            if (_entries.Count == 0) return 0;
            float acc = 0f;
            float scale = _total > 1f ? _total : 1f;
            for (int i = 0; i < _entries.Count; i++)
            {
                acc += _entries[i].Chance / scale;
                if (rollValue <= acc) return _entries[i].ItemDefId;
            }
            return 0;
        }

        public int Count => _entries.Count;
    }

    /// <summary>
    /// 一件待分配掉落（Pending 态，服务端持有 —— 副本 GDD §2.5.4）。
    /// </summary>
    public sealed class LootRollSession
    {
        private readonly Dictionary<int, LootChoice> _choices = new Dictionary<int, LootChoice>();
        private readonly Dictionary<int, int> _rolls = new Dictionary<int, int>();

        public int ItemDefId;
        public BindType Bind = BindType.OnPickup;
        public int TimeoutSeconds = 60;

        /// <summary>该件掉落允许的品质上限（用于 UI/校验，本期仅占位）。</summary>
        public ItemQuality QualityCap = ItemQuality.Transcendent;

        /// <summary>是否 BOSS 专属唯一掉落（D-21）。唯一掉落不受 EQ-1 连胜降级约束。</summary>
        public bool IsUnique;

        /// <summary>若为唯一掉落，对应的 U 系列 ItemDef id（0 = 非唯一）。</summary>
        public int UniqueItemDefId;

        public void SetChoice(int playerId, LootChoice choice)
        {
            _choices[playerId] = choice;
        }

        public LootChoice ChoiceOf(int playerId)
        {
            LootChoice c;
            return _choices.TryGetValue(playerId, out c) ? c : LootChoice.None;
        }

        /// <summary>「需要」按钮对本职业不可用时置灰（副本 GDD §2.5.2）。</summary>
        public static bool CanNeed(ItemDef def, ClassId playerClass)
        {
            if (def == null) return false;
            if (def.Slot == EquipSlot.None) return false; // 材料/消耗品不可 Need
            return def.RestrictToClass == ClassId.None || def.RestrictToClass == playerClass;
        }

        /// <summary>
        /// 结算：需要优先于掷骰子；同级之间比骰子（1–100，服务端掷）。
        /// 返回胜者 playerId；0 = 无人认领（进入自由拾取 / 自动销毁流程）。
        /// </summary>
        public int Resolve(Rng rng, IReadOnlyDictionary<int, ClassId> players,
            IReadOnlyDictionary<int, int> needWinStreak, out LootChoice winningTier)
        {
            winningTier = LootChoice.None;
            if (players == null) return 0;

            // 先掷骰（服务端），所有人都先有骰子点数，避免「先选先掷」的公平性质疑
            foreach (KeyValuePair<int, ClassId> kv in players)
            {
                _rolls[kv.Key] = rng.RangeInt(1, 100);
            }

            int winner = 0;
            int bestRoll = -1;
            foreach (KeyValuePair<int, ClassId> kv in players)
            {
                if (ChoiceOf(kv.Key) != LootChoice.Need) continue;
                int roll;
                if (!_rolls.TryGetValue(kv.Key, out roll)) continue;
                // EQ-1：连续「需要」胜利 2 次后，第 3 次起的「需要」自动降级为掷骰子
                int streak;
                if (needWinStreak != null && needWinStreak.TryGetValue(kv.Key, out streak) && streak >= 2) continue;
                if (roll > bestRoll) { bestRoll = roll; winner = kv.Key; }
            }
            if (winner != 0)
            {
                winningTier = LootChoice.Need;
                return winner;
            }

            bestRoll = -1;
            foreach (KeyValuePair<int, ClassId> kv in players)
            {
                LootChoice c = ChoiceOf(kv.Key);
                bool eligible = c == LootChoice.Greed;
                // 被降级的「需要」在此按掷骰子参与
                if (!eligible && c == LootChoice.Need)
                {
                    int streak;
                    if (needWinStreak != null && needWinStreak.TryGetValue(kv.Key, out streak) && streak >= 2) eligible = true;
                }
                if (!eligible) continue;
                int roll;
                if (!_rolls.TryGetValue(kv.Key, out roll)) continue;
                if (roll > bestRoll) { bestRoll = roll; winner = kv.Key; }
            }
            if (winner != 0) winningTier = LootChoice.Greed;
            return winner;
        }

        public int RollOf(int playerId)
        {
            int r;
            return _rolls.TryGetValue(playerId, out r) ? r : 0;
        }
    }

    /// <summary>掉落与拾取系统的对外门面（服务端调用）。</summary>
    public sealed class LootService
    {
        private readonly Rng _rng;
        private readonly Dictionary<int, LootTable> _tables = new Dictionary<int, LootTable>();

        public LootService(Rng rng)
        {
            _rng = rng;
        }

        /// <summary>注册怪物掉落表（key = 怪物定义 Id）。</summary>
        public void Register(int monsterDefId, LootTable table)
        {
            _tables[monsterDefId] = table;
        }

        public int RollDrop(int monsterDefId)
        {
            LootTable t;
            if (!_tables.TryGetValue(monsterDefId, out t)) return 0;
            float roll;
            return t.Roll(_rng, out roll);
        }

        /// <summary>
        /// 注册副本/团本掉落规则表（key = LootRuleKey，如 "D-06" / "D-26-sub" / "D-26-myth"）。
        /// 本期结算实际由 Core.DungeonLoot 按规则键数据驱动计算，此处仅保留规则注册入口。
        /// </summary>
        private readonly Dictionary<string, LootTable> _ruleTables = new Dictionary<string, LootTable>();

        public void RegisterRule(string ruleKey, LootTable table)
        {
            _ruleTables[ruleKey] = table;
        }

        /// <summary>
        /// 按副本规则键结算某 BOSS 的掉落（返回本次应掉落的若干件待分配掉落）。
        /// 由 Core.DungeonLoot 数据驱动实现（D-06 / D-26-sub / D-26-myth 四类结算）。
        /// </summary>
        public System.Collections.Generic.List<LootRollSession> RollDropByRule(
            string ruleKey, int bossId, bool isFinalBoss, DungeonLootRunState run)
        {
            return DungeonLoot.RollBoss(ruleKey, bossId, isFinalBoss, _rng, run);
        }

        /// <summary>
        /// 构造一件待分配掉落。组队来源（Party）一律拾取绑定（D-19）。
        /// </summary>
        public LootRollSession CreateSession(int itemDefId, LootSource source)
        {
            return new LootRollSession
            {
                ItemDefId = itemDefId,
                Bind = source == LootSource.Party ? BindType.OnPickup : BindType.OnEquip
            };
        }
    }
}
