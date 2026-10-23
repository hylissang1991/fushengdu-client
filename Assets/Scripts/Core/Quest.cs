// 《浮生渡》领域层 · 任务系统（本期 3–5 个任务链：击杀 / 采集 / 对话）
// @trace 职业系统 GDD S-12（任务系统）；上游 S-03 角色与存档
// 任务奖励是掉落之外第二条装备 / 经验通道；与付费零耦合（D-14）。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    public enum QuestType
    {
        Kill = 0,   // 击杀指定怪物
        Gather = 1, // 采集指定物品
        Talk = 2    // 与指定 NPC 对话
    }

    public sealed class QuestDef
    {
        public int Id;
        public string Name = "任务";
        public string Description = "";
        public int Level = 1;
        public QuestType Type = QuestType.Kill;

        // 目标标识：Kill → 怪物定义 Id；Gather → 物品定义 Id；Talk → NPC Id
        public int TargetId;
        public int RequiredCount = 1;

        public int NextQuestId; // 0 = 链末端
        public int PredecessorId; // 0 = 链首

        public int XpReward;
        public int SilverReward; // 渡银
        public int ItemRewardDefId; // 0 = 无
    }

    public sealed class QuestProgress
    {
        public int QuestId;
        public int Count;
        public bool TurnedIn;

        public bool IsComplete(QuestDef def)
        {
            return def != null && Count >= def.RequiredCount;
        }
    }

    public sealed class QuestLog
    {
        private readonly Dictionary<int, QuestProgress> _active = new Dictionary<int, QuestProgress>();
        private readonly HashSet<int> _done = new HashSet<int>();

        public IReadOnlyCollection<QuestProgress> Active => _active.Values;

        public static QuestTable Table { get; set; } = QuestTable.Default;

        public bool IsAccepted(int questId)
        {
            return _active.ContainsKey(questId);
        }

        public bool IsDone(int questId)
        {
            return _done.Contains(questId);
        }

        /// <summary>接取任务：前置必须已完成。</summary>
        public bool Accept(int questId)
        {
            QuestDef def = Table.Get(questId);
            if (def == null) return false;
            if (_active.ContainsKey(questId) || _done.Contains(questId)) return false;
            if (def.PredecessorId != 0 && !_done.Contains(def.PredecessorId)) return false;
            _active[questId] = new QuestProgress { QuestId = questId, Count = 0 };
            return true;
        }

        public void Abandon(int questId)
        {
            _active.Remove(questId);
        }

        /// <summary>击杀事件：推进所有匹配目标的击杀型任务。</summary>
        public void OnKill(int monsterDefId)
        {
            foreach (KeyValuePair<int, QuestProgress> kv in _active)
            {
                QuestDef def = Table.Get(kv.Key);
                if (def == null || def.Type != QuestType.Kill || def.TargetId != monsterDefId) continue;
                if (kv.Value.Count < def.RequiredCount) kv.Value.Count++;
            }
        }

        /// <summary>采集事件。</summary>
        public void OnGather(int itemDefId, int amount = 1)
        {
            foreach (KeyValuePair<int, QuestProgress> kv in _active)
            {
                QuestDef def = Table.Get(kv.Key);
                if (def == null || def.Type != QuestType.Gather || def.TargetId != itemDefId) continue;
                if (kv.Value.Count < def.RequiredCount) kv.Value.Count += amount;
            }
        }

        /// <summary>对话事件。</summary>
        public void OnTalk(int npcId)
        {
            foreach (KeyValuePair<int, QuestProgress> kv in _active)
            {
                QuestDef def = Table.Get(kv.Key);
                if (def == null || def.Type != QuestType.Talk || def.TargetId != npcId) continue;
                if (kv.Value.Count < def.RequiredCount) kv.Value.Count = def.RequiredCount;
            }
        }

        public bool CanTurnIn(int questId)
        {
            QuestProgress p;
            if (!_active.TryGetValue(questId, out p)) return false;
            return !p.TurnedIn && p.IsComplete(Table.Get(questId));
        }

        /// <summary>交任务：发放奖励并返回下一环任务 Id（0 = 无）。</summary>
        public int TurnIn(int questId)
        {
            if (!CanTurnIn(questId)) return 0;
            QuestDef def = Table.Get(questId);
            _active.Remove(questId);
            _done.Add(questId);
            return def != null ? def.NextQuestId : 0;
        }

        public void Restore(IEnumerable<QuestProgress> progress, IEnumerable<int> done)
        {
            _active.Clear();
            _done.Clear();
            if (progress != null) foreach (QuestProgress p in progress) _active[p.QuestId] = p;
            if (done != null) foreach (int id in done) _done.Add(id);
        }

        public IEnumerable<QuestProgress> SnapshotActive()
        {
            return new List<QuestProgress>(_active.Values);
        }

        public IEnumerable<int> SnapshotDone()
        {
            return new List<int>(_done);
        }
    }

    /// <summary>任务表。本期 5 环新手链（击杀 / 采集 / 对话混合）。</summary>
    public sealed class QuestTable
    {
        private readonly Dictionary<int, QuestDef> _defs = new Dictionary<int, QuestDef>();

        private static QuestTable _default;
        public static QuestTable Default => _default ?? (_default = Build());

        private static QuestTable Build()
        {
            var t = new QuestTable();
            // 环 1：与村长处者对话（引导）
            t.Add(new QuestDef
            {
                Id = 1, Name = "渡口初问", Description = "与新手村村长处者对话，了解渡口之事。", Level = 1,
                Type = QuestType.Talk, TargetId = 9001, RequiredCount = 1, NextQuestId = 2,
                XpReward = Progression.XpForQuest(1), SilverReward = 10
            });
            // 环 2：击杀林间灰狼 ×5（击杀型）
            t.Add(new QuestDef
            {
                Id = 2, Name = "林间狼患", Description = "清除新手村外的灰狼 ×5。", Level = 2,
                Type = QuestType.Kill, TargetId = 1, RequiredCount = 5, PredecessorId = 1, NextQuestId = 3,
                XpReward = Progression.XpForQuest(2), SilverReward = 25
            });
            // 环 3：采集灵草 ×6（采集型）
            t.Add(new QuestDef
            {
                Id = 3, Name = "灵草三株", Description = "在村外采集灵草 ×6。", Level = 4,
                Type = QuestType.Gather, TargetId = 3001, RequiredCount = 6, PredecessorId = 2, NextQuestId = 4,
                XpReward = Progression.XpForQuest(4), SilverReward = 40
            });
            // 环 4：击杀野猪王（精英）
            t.Add(new QuestDef
            {
                Id = 4, Name = "野猪王之患", Description = "击杀精英「獠牙野猪王」。", Level = 7,
                Type = QuestType.Kill, TargetId = 2, RequiredCount = 1, PredecessorId = 3, NextQuestId = 5,
                XpReward = Progression.XpForQuest(7), SilverReward = 90, ItemRewardDefId = 1011
            });
            // 环 5：向村长复命（收尾）
            t.Add(new QuestDef
            {
                Id = 5, Name = "渡口回报", Description = "回到村长处者处复命。", Level = 8,
                Type = QuestType.Talk, TargetId = 9001, RequiredCount = 1, PredecessorId = 4,
                XpReward = Progression.XpForQuest(8), SilverReward = 120, ItemRewardDefId = 2012
            });
            return t;
        }

        private void Add(QuestDef def)
        {
            _defs[def.Id] = def;
        }

        public QuestDef Get(int id)
        {
            QuestDef d;
            return _defs.TryGetValue(id, out d) ? d : null;
        }

        public IEnumerable<QuestDef> All()
        {
            return _defs.Values;
        }
    }
}
