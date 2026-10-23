// 《浮生渡》领域层 · NPC 与城镇服务定义
// 与 MonsterTable / WorldTable 同构：纯 C# 数据层，不持有 Unity 类型，可在服务端权威运行。
// 数据基准：《浮生渡-东荒大陆地图与区域规划-v1.md》§2（云津渡主城）/ §3（野外 8 村庄）。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>NPC 职能（决定可提供的基础服务；数据层分类，与 Fushengdu.Gameplay.NpcRole 运行时角色区分）。</summary>
    public enum NpcFunction
    {
        QuestGiver = 0,      // 任务发布 / 剧情
        Trainer = 1,         // 训练师（技能 / 职业）
        Vendor = 2,          // 杂货商（售卖道具 / 材料）
        Repairs = 3,         // 修理
        Auctioneer = 4,      // 拍卖行
        GuildRegistrar = 5,  // 公会注册 / 管理
        PortalKeeper = 6,    // 传送（回城 / 跨区）
        Banker = 7,          // 银行（仓库）
        FlightMaster = 8,    // 飞行点（未来坐骑系统）
    }

    /// <summary>单个 NPC 定义（本期：东荒主城云津渡 + 野外 8 村庄服务点）。</summary>
    public sealed class NpcDef
    {
        public int Id;
        public string Name = "NPC";
        public string Title = "";       // 称号 / 职务（如「渡口守吏」）
        public NpcFunction Role;
        public int RegionId;            // 所属区域（对应 WorldTable 区域 Id）
        public string[] Services;       // 提供的服务标签（人类可读）
        public string Note = "";        // 备注（剧情 / 线索）

        public bool HasService(string tag)
        {
            if (Services == null) return false;
            foreach (var s in Services) if (s == tag) return true;
            return false;
        }
    }

    /// <summary>
    /// NPC 表（东荒）。Id 区间：
    ///   1001–1099 云津渡主城服务 NPC（唯一主城，1–60 中枢）
    ///   1101–1199 野外 8 村庄服务点（青溪村 / 义庄 / 纸马镇 / 荒村 / 谷口营地 / 石林驿 / 涧口猎营 / 雷泽渔村）
    /// </summary>
    public sealed class NpcTable
    {
        private readonly Dictionary<int, NpcDef> _defs = new Dictionary<int, NpcDef>();

        private static NpcTable _default;
        public static NpcTable Default => _default ?? (_default = Build());

        private static NpcTable Build()
        {
            var t = new NpcTable();

            // —— 云津渡（区域 Id 1，唯一主城 / 安全区 / 1–60 中枢）——
            // 规划 §2：传送 / 拍卖 / 公会 / 训练师 / 任务发布；另补银行 / 修理 / 杂货 标准主城服务。
            t.Add(new NpcDef
            {
                Id = 1001, Name = "渡口守吏", Title = "云津渡·传送官", Role = NpcFunction.PortalKeeper, RegionId = 1,
                Services = new[] { "传送", "回城", "跨区接驳" }, Note = "仙山云海渡口牌楼下，掌管东荒各区域传送阵。"
            });
            t.Add(new NpcDef
            {
                Id = 1002, Name = "市易令", Title = "云津渡·拍卖官", Role = NpcFunction.Auctioneer, RegionId = 1,
                Services = new[] { "拍卖行", "寄售" }, Note = "朱砂鎏金市集中央的高台。"
            });
            t.Add(new NpcDef
            {
                Id = 1003, Name = "结盟祭官", Title = "云津渡·公会官", Role = NpcFunction.GuildRegistrar, RegionId = 1,
                Services = new[] { "公会注册", "公会管理" }, Note = "符箓牌楼侧的盟誓坛。"
            });
            t.Add(new NpcDef
            {
                Id = 1004, Name = "演武教头", Title = "云津渡·训练师", Role = NpcFunction.Trainer, RegionId = 1,
                Services = new[] { "职业技能", "属性修炼" }, Note = "演武场中央，负责 1–60 全期职业训练。"
            });
            t.Add(new NpcDef
            {
                Id = 1005, Name = "引路仙官", Title = "云津渡·任务官", Role = NpcFunction.QuestGiver, RegionId = 1,
                Services = new[] { "主线任务", "日常任务", "剧情引导" }, Note = "云阶之上的接引仙官，发放东荒主线与回头任务。"
            });
            t.Add(new NpcDef
            {
                Id = 1006, Name = "金阙掌柜", Title = "云津渡·钱庄", Role = NpcFunction.Banker, RegionId = 1,
                Services = new[] { "银行", "仓库" }, Note = "金阙钱庄，掌管玩家仓储。"
            });
            t.Add(new NpcDef
            {
                Id = 1007, Name = "铁砧真人", Title = "云津渡·铁匠", Role = NpcFunction.Repairs, RegionId = 1,
                Services = new[] { "装备修理", "装备制造" }, Note = "主城铁匠铺，常驻修理点。"
            });
            t.Add(new NpcDef
            {
                Id = 1008, Name = "杂货仙商", Title = "云津渡·杂货商", Role = NpcFunction.Vendor, RegionId = 1,
                Services = new[] { "基础道具", "材料", "补给" }, Note = "售卖药水 / 卷轴 / 基础材料。"
            });

            // —— 野外 8 村庄服务点（区域 Id 见规划 §1 / §3）——
            // 1 · 青溪村（青溪林 Id 2，新手村：出生点 / 基础任务 / 修理）
            t.Add(new NpcDef
            {
                Id = 1101, Name = "青溪村村长", Title = "青溪村·引路人", Role = NpcFunction.QuestGiver, RegionId = 2,
                Services = new[] { "新手任务", "出生引导" }, Note = "新手村核心，发放 1–10 级起步任务。"
            });
            t.Add(new NpcDef
            {
                Id = 1102, Name = "青溪村铁匠", Title = "青溪村·修理", Role = NpcFunction.Repairs, RegionId = 2,
                Services = new[] { "装备修理" }, Note = "新手村唯一修理点。"
            });

            // 2 · 义庄（义庄岭 Id 4，任务点 / 殡葬民俗剧情）
            t.Add(new NpcDef
            {
                Id = 1103, Name = "义庄执事", Title = "义庄·知客", Role = NpcFunction.QuestGiver, RegionId = 4,
                Services = new[] { "剧情任务", "殡葬民俗" }, Note = "守义庄坟岭，承接殡葬民俗主线。"
            });

            // 3 · 纸马镇（纸马泽 Id 5，交易小集 / 纸扎工艺）
            t.Add(new NpcDef
            {
                Id = 1104, Name = "纸扎匠", Title = "纸马镇·工艺师", Role = NpcFunction.Vendor, RegionId = 5,
                Services = new[] { "纸扎道具", "材料" }, Note = "水泽交易小集的纸扎工艺商。"
            });

            // 4 · 荒村（老林 Id 6，废弃村：探索 / 隐藏任务 / 山鬼线索）
            t.Add(new NpcDef
            {
                Id = 1105, Name = "荒村残魂", Title = "荒村·隐秘", Role = NpcFunction.QuestGiver, RegionId = 6,
                Services = new[] { "隐藏任务", "山鬼线索" }, Note = "废弃村中徘徊的残魂，牵出 X-DH-01 山鬼线索。"
            });

            // 5 · 谷口营地（山魈谷 Id 8，补给营地 / 猎户任务）
            t.Add(new NpcDef
            {
                Id = 1106, Name = "谷口猎户", Title = "谷口营地·猎首", Role = NpcFunction.QuestGiver, RegionId = 8,
                Services = new[] { "猎户任务", "装备修理" }, Note = "峡谷补给营地，接猎户日常。"
            });

            // 6 · 石林驿（石敢当石林 Id 10，驿站 / 修整）
            t.Add(new NpcDef
            {
                Id = 1107, Name = "石林驿丞", Title = "石林驿·驿丞", Role = NpcFunction.Repairs, RegionId = 10,
                Services = new[] { "装备修理", "休整" }, Note = "石林中的驿站，供旅人修整。"
            });

            // 7 · 涧口猎营（水猴涧 Id 11，猎营 / 稀有怪线索）
            t.Add(new NpcDef
            {
                Id = 1108, Name = "涧口猎首", Title = "涧口猎营·首领", Role = NpcFunction.QuestGiver, RegionId = 11,
                Services = new[] { "猎营任务", "稀有线索" }, Note = "山涧猎营首领，提供 M-DH-12 水猴相关线索。"
            });

            // 8 · 雷泽渔村（雷泽 Id 13，世界BOSS前哨 / 补给）
            t.Add(new NpcDef
            {
                Id = 1109, Name = "雷泽渔霸", Title = "雷泽渔村·渔首", Role = NpcFunction.Vendor, RegionId = 13,
                Services = new[] { "补给", "装备修理" }, Note = "世界BOSS 雷泽·夔牛（WB-DH-01）前哨补给点。"
            });

            return t;
        }

        private void Add(NpcDef def) => _defs[def.Id] = def;

        public NpcDef Get(int id)
        {
            NpcDef d;
            return _defs.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>取某区域内的全部 NPC。</summary>
        public List<NpcDef> InRegion(int regionId)
        {
            var list = new List<NpcDef>();
            foreach (var d in _defs.Values) if (d.RegionId == regionId) list.Add(d);
            return list;
        }

        public IEnumerable<NpcDef> All() => _defs.Values;

        /// <summary>NPC 总数（应为 17：云津渡 8 + 野外 9）。</summary>
        public int Count => _defs.Count;
    }
}
