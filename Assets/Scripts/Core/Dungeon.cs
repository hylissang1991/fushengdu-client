// 《浮生渡》领域层 · 副本 / 团本定义表
// 纯 C# 领域层（Fushengdu.Core），不引用 UnityEngine / UnityEditor。
// @trace 副本系统 GDD / 团本系统 GDD（#53 · Phase A 数据层）
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>副本 / 团本类型。</summary>
    public enum DungeonKind
    {
        Factory5,   // 5 人随机本工厂（档位 + 模板 + 主题 + BOSS 池 + 词缀随机抽取）
        Fixed5,     // 固定 5 人本（D60-1~8）
        Raid10,     // 10 人团本（R10-1 / R10-2）
        Raid20      // 20 人团本（R20-1）
    }

    /// <summary>副本 / 团本实例定义。</summary>
    public sealed class DungeonDef
    {
        public int Id;
        public string Name = "副本";
        public DungeonKind Kind;
        public int RequiredLevel;
        public int MinPlayers;
        public int MaxPlayers;
        public int[] BossIds = new int[0];        // → MonsterTable 副本/团本 BOSS id 段
        public string LootRuleKey = "";           // D-06 / D-26-sub / D-26-myth（掉落规则键，数值走 L1 下发）
        public int[] TemplatePool = new int[0];   // 工厂本模板池（Phase B 预留占位）
        public int[] ThemePool = new int[0];      // 工厂本主题池（Phase B 预留占位）
        public int[] BossPoolRef = new int[0];    // 工厂本 BOSS 池引用（Phase B 抽取）
        public int[] AffixPool = new int[0];      // 工厂本词缀池（Phase B 归墟侵蚀层预留）
        public int WingCount;                     // 团本分翼数：5人本=1 / 10人=2 / 20人=3
        public WaveSpawn[] Waves = new WaveSpawn[0];
    }

    /// <summary>副本内一波刷怪配置（Phase B 实例刷怪机使用，本次仅留结构）。</summary>
    public sealed class WaveSpawn
    {
        public int MonsterDefId;  // → MonsterTable id
        public int Count;
        public float Radius;
        public int Wing;
    }

    /// <summary>副本 / 团本定义表（#53 · Phase A 数据层核心交付）。</summary>
    public sealed class DungeonTable
    {
        private readonly Dictionary<int, DungeonDef> _defs = new Dictionary<int, DungeonDef>();

        private static DungeonTable _default;
        public static DungeonTable Default => _default ?? (_default = Build());

        private static DungeonTable Build()
        {
            var t = new DungeonTable();

            // —— 15 个工厂随机本（5 人，档位 15/18/…/57，BossPoolRef = 401~422 等 22 个 id）——
            // 701–714 / 801–808 / 901–906 为 Phase B 词缀/模板/主题表预留占位，仅存 int 数组。
            int[] bossPool     = { 401,402,403,404,405,406,407,408,409,410,411,412,413,414,415,416,417,418,419,420,421,422 };
            int[] affixPool    = { 701,702,703,704,705,706,707,708,709,710,711,712,713,714 };
            int[] templatePool = { 801,802,803,804,805,806,807,808 };
            int[] themePool    = { 901,902,903,904,905,906 };
            for (int d = 0; d < 15; d++)
            {
                t.Add(new DungeonDef
                {
                    Id = 1001 + d,
                    Name = "工厂本·档" + (d + 1),
                    Kind = DungeonKind.Factory5,
                    RequiredLevel = 15 + d * 3,
                    MinPlayers = 5,
                    MaxPlayers = 5,
                    LootRuleKey = "D-06",
                    BossPoolRef = bossPool,
                    AffixPool = affixPool,
                    TemplatePool = templatePool,
                    ThemePool = themePool,
                    WingCount = 1
                });
            }

            // —— 8 个固定 5 人本（D60-1~8，BossIds 指向 501–529）——
            t.Add(new DungeonDef { Id = 2001, Name = "D60-1", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 501, 502, 503 } });
            t.Add(new DungeonDef { Id = 2002, Name = "D60-2", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 504, 505, 506 } });
            t.Add(new DungeonDef { Id = 2003, Name = "D60-3", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 507, 508, 509 } });
            t.Add(new DungeonDef { Id = 2004, Name = "D60-4", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 510, 511, 512, 513 } });
            t.Add(new DungeonDef { Id = 2005, Name = "D60-5", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 514, 515, 516 } });
            t.Add(new DungeonDef { Id = 2006, Name = "D60-6", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 517, 518, 519, 520 } });
            t.Add(new DungeonDef { Id = 2007, Name = "D60-7", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 521, 522, 523, 524 } });
            t.Add(new DungeonDef { Id = 2008, Name = "D60-8", Kind = DungeonKind.Fixed5, RequiredLevel = 60, MinPlayers = 5, MaxPlayers = 5, LootRuleKey = "D-06", WingCount = 1, BossIds = new[] { 525, 526, 527, 528, 529 } });

            // —— 2 个 10 人团本（R10-1 / R10-2，WingCount = 2，BossIds 指向 601–610）——
            t.Add(new DungeonDef { Id = 3001, Name = "R10-1", Kind = DungeonKind.Raid10, RequiredLevel = 60, MinPlayers = 10, MaxPlayers = 10, LootRuleKey = "D-26-sub", WingCount = 2, BossIds = new[] { 601, 602, 603, 604, 605 } });
            t.Add(new DungeonDef { Id = 3002, Name = "R10-2", Kind = DungeonKind.Raid10, RequiredLevel = 60, MinPlayers = 10, MaxPlayers = 10, LootRuleKey = "D-26-sub", WingCount = 2, BossIds = new[] { 606, 607, 608, 609, 610 } });

            // —— 1 个 20 人团本（R20-1，WingCount = 3，BossIds 指向 611–616）——
            t.Add(new DungeonDef { Id = 4001, Name = "R20-1", Kind = DungeonKind.Raid20, RequiredLevel = 60, MinPlayers = 20, MaxPlayers = 20, LootRuleKey = "D-26-myth", WingCount = 3, BossIds = new[] { 611, 612, 613, 614, 615, 616 } });

            return t;
        }

        private void Add(DungeonDef def)
        {
            _defs[def.Id] = def;
        }

        public DungeonDef Get(int id)
        {
            DungeonDef d;
            return _defs.TryGetValue(id, out d) ? d : null;
        }

        public IEnumerable<DungeonDef> All()
        {
            return _defs.Values;
        }
    }
}
