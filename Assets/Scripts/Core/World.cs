// 《浮生渡》世界层 · 东荒大陆区域与刷怪点定义
// 规划基准：《浮生渡-东荒大陆地图与区域规划-v1.md》（2026-10-24 用户确认 14 图/大陆）
// 与 MonsterTable 同构：纯 C# 数据层，不持有 Unity 类型，可在服务端权威运行。
// 刷怪密度：每个野外区域 **6 个不同物种**，每物种 **4 亚种（喽啰/精英/稀有/头目）**（用户指定）。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>区域类型。</summary>
    public enum RegionType
    {
        Capital = 0,       // 主城（安全区 / 中枢）
        Wild = 1,          // 野外
        RaidEntrance = 2   // 团本入口区
    }

    /// <summary>单个刷怪点：引用 MonsterTable 中的 MonsterDefId。</summary>
    public sealed class SpawnEntry
    {
        public int MonsterDefId;
        public int Count;      // 该区域同批刷新数量（设计值）
        public float Radius;   // 刷新半径（米）
        public SpawnEntry(int monsterDefId, int count, float radius)
        {
            MonsterDefId = monsterDefId;
            Count = count;
            Radius = radius;
        }
    }

    /// <summary>东荒大陆一张区域地图定义（对应规划文档中的 14 张区域图之一）。</summary>
    public sealed class RegionDef
    {
        public int Id;
        public string Name;
        public RegionType Type;
        public int LevelMin;
        public int LevelMax;
        public string Theme;
        public string[] Villages;    // 区域内聚落（非独立地图）
        public string[] Dungeons;    // 副本 / 团本落点（门牌名）
        public SpawnEntry[] Spawns;  // 刷怪（引用 MonsterDefId）

        public RegionDef(int id, string name, RegionType type, int levelMin, int levelMax,
            string theme, string[] villages, string[] dungeons, SpawnEntry[] spawns)
        {
            Id = id; Name = name; Type = type; LevelMin = levelMin; LevelMax = levelMax;
            Theme = theme; Villages = villages; Dungeons = dungeons; Spawns = spawns;
        }
    }

    /// <summary>
    /// 东荒大陆世界表（14 张区域图：1 主城 + 12 野外 + 1 团本入口区）。
    /// 刷怪点 MonsterDefId：野外 101–156（14 物种 × 4 亚种，id = 101 + 物种序号*4 + 亚种序号）/ 东荒独立稀有 161–162（X-DH-01/02，归老林·荒村）/ 守城 201–203 / 世界BOSS 301。
    /// **每个野外区域 = 6 个不同物种，每物种 4 亚种（喽啰/精英/稀有/头目）→ 共 24 条刷怪**。
    /// </summary>
    public sealed class WorldTable
    {
        private readonly Dictionary<int, RegionDef> _defs = new Dictionary<int, RegionDef>();

        private static WorldTable _default;
        public static WorldTable Default => _default ?? (_default = Build());

        // 亚种偏移：0 喽啰 / 1 精英 / 2 稀有 / 3 头目
        private static SpawnEntry S(int id, int count, float r) => new SpawnEntry(id, count, r);
        private static int FamBase(int fi) => 101 + fi * 4;

        /// <summary>拼接两段刷怪点（用于给区域追加独立稀有等特殊条目）。</summary>
        private static SpawnEntry[] Concat(SpawnEntry[] a, params SpawnEntry[] b)
        {
            var list = new List<SpawnEntry>(a);
            list.AddRange(b);
            return list.ToArray();
        }

        /// <summary>把若干物种（fi 列表）各展开为 4 亚种刷怪点。</summary>
        private static SpawnEntry[] RegionSpecies(int[] fis, float r)
        {
            var list = new List<SpawnEntry>();
            foreach (int fi in fis)
            {
                int b = FamBase(fi);
                list.Add(S(b + 0, 8, r)); // 喽啰
                list.Add(S(b + 1, 3, r)); // 精英
                list.Add(S(b + 2, 1, r)); // 稀有
                list.Add(S(b + 3, 1, r)); // 头目
            }
            return list.ToArray();
        }

        private static WorldTable Build()
        {
            var t = new WorldTable();

            // 1 · 云津渡（唯一主城，1–60 中枢；守城事件 CD-1~3）
            t.Add(new RegionDef(1, "云津渡", RegionType.Capital, 1, 60,
                "仙山云海渡口 · 符箓 · 朱砂鎏金",
                new string[0], new string[0],
                new[] { S(201, 1, 30f), S(202, 1, 30f), S(203, 1, 30f) }));

            // 2 · 青溪林（新手村：青溪村）— 6 物种：山贼/灰狼/灵兽/山猪/尸傀/纸人
            t.Add(new RegionDef(2, "青溪林", RegionType.Wild, 1, 10,
                "农田山村 · 溪流",
                new[] { "青溪村" }, new[] { "灰狼巢穴（5人本）" },
                RegionSpecies(new[] { 0, 3, 2, 4, 1, 5 }, 40f)));

            // 3 · 落雁坡（哨站，无村庄）— 6 物种：尸傀/山猪/灵兽/纸人/荒兵/蛛妖
            t.Add(new RegionDef(3, "落雁坡", RegionType.Wild, 8, 16,
                "草坡丘陵",
                new string[0], new string[0],
                RegionSpecies(new[] { 1, 4, 2, 5, 6, 8 }, 40f)));

            // 4 · 义庄岭（殡葬民俗剧情：义庄）— 6 物种：荒兵/蛛妖/尸傀/纸人/山魈/游方道人
            t.Add(new RegionDef(4, "义庄岭", RegionType.Wild, 14, 22,
                "义庄 · 坟岭",
                new[] { "义庄" }, new[] { "义庄地宫（5人本）" },
                RegionSpecies(new[] { 6, 8, 1, 5, 7, 9 }, 40f)));

            // 5 · 纸马泽（交易小集：纸马镇）— 6 物种：蛛妖/山魈/游方道人/纸人/荒兵/石敢当
            t.Add(new RegionDef(5, "纸马泽", RegionType.Wild, 18, 26,
                "水泽 · 纸扎民俗",
                new[] { "纸马镇" }, new string[0],
                RegionSpecies(new[] { 8, 7, 9, 5, 6, 10 }, 40f)));

            // 6 · 老林（废弃村：荒村；蛛母林副本）— 6 物种：山魈/游方道人/石敢当/水猴/荒兵/蛛妖
            //   + 老林·荒村独立稀有：山鬼(X-DH-01)/荒村傩面(X-DH-02)，各 1 只、大半径刷新
            t.Add(new RegionDef(6, "老林", RegionType.Wild, 22, 30,
                "藤蔓密林",
                new[] { "荒村" }, new[] { "蛛母林（5人本）" },
                Concat(RegionSpecies(new[] { 7, 9, 10, 11, 6, 8 }, 40f), S(161, 1, 60f), S(162, 1, 60f))));

            // 7 · 荒兵古战场 — 6 物种：石敢当/水猴/白骨/狐火/山魈/游方道人
            t.Add(new RegionDef(7, "荒兵古战场", RegionType.Wild, 28, 36,
                "古战场 · 枯骨",
                new string[0], new string[0],
                RegionSpecies(new[] { 10, 11, 12, 13, 7, 9 }, 45f)));

            // 8 · 山魈谷（补给营地：谷口营地）— 6 物种：水猴/白骨/狐火/石敢当/山魈/游方道人
            t.Add(new RegionDef(8, "山魈谷", RegionType.Wild, 32, 40,
                "峡谷",
                new[] { "谷口营地" }, new string[0],
                RegionSpecies(new[] { 11, 12, 13, 10, 7, 9 }, 45f)));

            // 9 · 道人观 — 6 物种：白骨/狐火/水猴/石敢当/游方道人/山魈
            t.Add(new RegionDef(9, "道人观", RegionType.Wild, 36, 44,
                "道观 · 符箓",
                new string[0], new string[0],
                RegionSpecies(new[] { 12, 13, 11, 10, 9, 7 }, 45f)));

            // 10 · 石敢当石林（驿站：石林驿；石阵副本）— 6 物种：狐火/白骨/水猴/石敢当/游方道人/山魈
            t.Add(new RegionDef(10, "石敢当石林", RegionType.Wild, 40, 48,
                "石林 · 镇石",
                new[] { "石林驿" }, new[] { "石阵（5人本）" },
                RegionSpecies(new[] { 13, 12, 11, 10, 9, 7 }, 45f)));

            // 11 · 水猴涧（猎营：涧口猎营；涧底水府副本）— 6 物种：狐火/白骨/水猴/石敢当/游方道人/蛛妖
            t.Add(new RegionDef(11, "水猴涧", RegionType.Wild, 44, 52,
                "山涧 · 瀑布",
                new[] { "涧口猎营" }, new[] { "涧底水府（5人本）" },
                RegionSpecies(new[] { 13, 12, 11, 10, 9, 8 }, 45f)));

            // 12 · 狐火荒原 — 6 物种：狐火/白骨/水猴/石敢当/游方道人/蛛妖
            t.Add(new RegionDef(12, "狐火荒原", RegionType.Wild, 48, 56,
                "荒原 · 狐火",
                new string[0], new string[0],
                RegionSpecies(new[] { 13, 12, 11, 10, 9, 8 }, 45f)));

            // 13 · 雷泽（世界BOSS：雷泽·夔牛 WB-DH-01）— BOSS 区，单列世界BOSS
            t.Add(new RegionDef(13, "雷泽", RegionType.Wild, 52, 58,
                "水泽 · 雷电（世界BOSS）",
                new[] { "雷泽渔村" }, new string[0],
                new[] { S(301, 1, 60f) }));

            // 14 · 归墟裂隙·外缘（团本 R10-1 归墟之眼入口；团本内怪待 MonsterTable 团本条目后补）
            t.Add(new RegionDef(14, "归墟裂隙·外缘", RegionType.RaidEntrance, 55, 60,
                "归墟裂隙 · 黑色晶体",
                new string[0], new[] { "团本 R10-1 归墟之眼·内环（10/20人）" },
                new SpawnEntry[0]));

            return t;
        }

        private void Add(RegionDef def)
        {
            _defs[def.Id] = def;
        }

        public RegionDef Get(int id)
        {
            RegionDef d;
            return _defs.TryGetValue(id, out d) ? d : null;
        }

        public IEnumerable<RegionDef> All()
        {
            return _defs.Values;
        }

        /// <summary>区域总数（应为 14）。</summary>
        public int Count => _defs.Count;
    }
}
