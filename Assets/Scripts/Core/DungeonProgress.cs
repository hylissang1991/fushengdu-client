// 《浮生渡》领域层 · 副本/团本进度存档（纯 C#，不引用 UnityEngine / UnityEditor）
// 数据结构与周帽映射放在 Core；序列化/Unity API 放在 Gameplay/DungeonProgressStore。
// @trace D-26（10 人本周上限次神话 6 件 / 20 人团本周上限最高级神话 7 件，每团保底 1 件）
// @trace 副本系统 GDD §2.5（首通 / 周帽 / 周保底）
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>
    /// 副本/团本进度存档（跨会话持久化到独立 JSON）。
    /// 全部为纯数据，不含任何 Unity / 网络类型；JSON 序列化由 Gameplay 层负责（HashSet/Dictionary 由 DTO 转换）。
    /// </summary>
    public sealed class DungeonProgressData
    {
        /// <summary>已首通副本 id 集合（首通奖励/标记用，跨周保留）。</summary>
        public HashSet<int> FirstClears = new HashSet<int>();

        /// <summary>本周已用顶级神话产出计数，按 dungeonId 记录，用于 D-26 周帽。</summary>
        public Dictionary<int, int> WeeklyCapUsed = new Dictionary<int, int>();

        /// <summary>本周是否已发团末保底 Mythic（按 dungeonId），对应 D-26-myth 每团保底 1 件。</summary>
        public Dictionary<int, bool> WeeklyMythicClaimed = new Dictionary<int, bool>();

        /// <summary>已解锁近路（可选）。</summary>
        public HashSet<int> Shortcuts = new HashSet<int>();

        /// <summary>上次周重置判定基准时间（本地时间 Ticks；用于「周一 05:00」重置）。</summary>
        public long LastResetTicks;
    }

    /// <summary>
    /// 周帽 / 周保底规则映射（数据驱动，写在 Core 内，零侵入冻结的 Core/Dungeon.cs）。
    /// 注意：此处的「周帽上限」指每个 dungeonId 每周可产出的「顶级神话」件数。
    /// </summary>
    public static class DungeonProgressRules
    {
        /// <summary>各规则键的每周顶级神话产出上限。返回 0 表示不限额（非团本规则）。</summary>
        public static int WeeklyCap(string ruleKey)
        {
            if (ruleKey == "D-26-sub") return 6;   // 10 人本：上限次神话（品质 6）6 件
            if (ruleKey == "D-26-myth") return 7;  // 20 人团本：上限最高级神话（品质 7）7 件
            return 0;                              // D-06 等 5 人本不限额（本期未设周帽）
        }

        /// <summary>各规则键计入周帽的「顶级品质」（其余品质不计入）。</summary>
        public static ItemQuality PremiumTier(string ruleKey)
        {
            if (ruleKey == "D-26-sub") return ItemQuality.SubMythic; // 10 人本顶级 = 次神话
            if (ruleKey == "D-26-myth") return ItemQuality.Mythic;   // 20 人团本顶级 = 最高级神话
            return ItemQuality.None;
        }
    }
}
