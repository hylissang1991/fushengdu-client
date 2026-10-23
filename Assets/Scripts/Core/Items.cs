// 《浮生渡》领域层 · 物品 / 装备 / 背包
// @trace D-27（装备品质 8 阶：凡品/精良/稀有/史诗/传说/次神话/最高级神话/超神话）
// @trace D-25（80 战勋 = 1 件成品最高级神话装备，不含武器）
// @trace D-24（超神话武器 T0：任何副本不产出成品，仅 30 块武器碎片 NPC 兑换）
// @trace 经济与掉落系统 GDD §2.3.1（品质 8 阶）、§2.3.2（层级与绑定）
// @trace D-14：装备只来自游戏内行为（掉落 / 任务），付费零战力；不得有任何付费入口。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>
    /// 品质 8 阶（D-27；经济 GDD §2.3.1，与美术圣经 v4 §3.3 色板一一对应）。
    /// 阶 6 次神话 / 阶 7 最高级神话 为 2026-10-23 用户制度新增；
    /// 阶 8 超神话仅主手武器槽，任何副本不产出成品，仅 30 块武器碎片 NPC 兑换（D-24）。
    /// </summary>
    public enum ItemQuality
    {
        None = 0,          // 无 / 未定义（用于「无顶级品质计入周帽」等判空）
        Common = 1,        // 凡品
        Fine = 2,          // 精良
        Rare = 3,          // 稀有
        Epic = 4,          // 史诗
        Legendary = 5,     // 传说
        SubMythic = 6,     // 次神话（D-26：10 人团本产出上限）
        Mythic = 7,        // 最高级神话（D-26：20 人团本产出上限；D-25：80 战勋可兑，不含武器）
        Transcendent = 8   // 超神话（D-24：T0 毕业武器，仅碎片兑换）
    }

    /// <summary>装备部位。</summary>
    public enum EquipSlot
    {
        None = 0,
        Weapon = 1,
        Head = 2,
        Chest = 3,
        Hands = 4,
        Legs = 5,
        Feet = 6,
        Trinket = 7,
        Offhand = 8   // 副手（盾 / 圣印 / 法器 / 灯 等；副本团本唯一掉落 U-07/U-16/U-29/U-42/U-43/U-52 等）
    }

    /// <summary>绑定类型。</summary>
    public enum BindType
    {
        None = 0,        // 可交易
        OnEquip = 1,     // 装备后绑定 BoE
        OnPickup = 2     // 拾取绑定 BoP（D-19：5 人本产出一律 BoP）
    }

    /// <summary>物品静态定义。</summary>
    public sealed class ItemDef
    {
        public int Id;
        public string Name = "物品";
        public ItemQuality Quality = ItemQuality.Common;
        public EquipSlot Slot = EquipSlot.None;
        public int Level = 1;

        // 装备数值（全部为「点」）
        public float E_AP;        // 装备攻强
        public float E_SP;        // 装备法强
        public float E_HP;        // 装备治疗强度
        public float Armor;       // 护甲
        public float WeaponDps;   // 武器 DPS（点/秒），仅 Slot == Weapon 有效
        public float WeaponSpeed = 2.0f;

        public ClassId RestrictToClass = ClassId.None; // 「需要」按钮的可用判定依据
        public int VendorPrice;                        // 卖店价（渡银）

        /// <summary>
        /// 特效标签（D-21 / A7 / A8 词缀类占位）。
        /// 本期仅落「标签 + 占位」：写入 GDD 原文特效定位 + 词缀类（如 [A5] / [A7剑修] / [A8]），
        /// 具体数值结算不在本期实现（留给战斗侧数值系统）。
        /// </summary>
        public string EffectTag = "";
    }

    /// <summary>物品实例（背包中的一格）。</summary>
    public sealed class ItemInstance
    {
        public int Uid;
        public int DefId;
        public bool Bound;

        public ItemInstance() { }

        public ItemInstance(int uid, int defId, bool bound)
        {
            Uid = uid;
            DefId = defId;
            Bound = bound;
        }
    }

    /// <summary>
    /// 背包 + 已穿戴装备。所有变更只在服务端生效（ADR-001 N-2）。
    /// </summary>
    public sealed class Inventory
    {
        public const int BagCapacity = 40;

        private readonly List<ItemInstance> _bag = new List<ItemInstance>();
        private readonly Dictionary<EquipSlot, int> _equipped = new Dictionary<EquipSlot, int>();
        private int _nextUid = 1;

        public IReadOnlyList<ItemInstance> Bag => _bag;

        public int Add(int defId, bool bound)
        {
            if (_bag.Count >= BagCapacity) return 0;
            var inst = new ItemInstance(_nextUid++, defId, bound);
            _bag.Add(inst);
            return inst.Uid;
        }

        public bool Remove(int uid)
        {
            for (int i = 0; i < _bag.Count; i++)
            {
                if (_bag[i].Uid != uid) continue;
                _bag.RemoveAt(i);
                return true;
            }
            return false;
        }

        public ItemInstance Find(int uid)
        {
            for (int i = 0; i < _bag.Count; i++) if (_bag[i].Uid == uid) return _bag[i];
            return null;
        }

        /// <summary>穿戴：返回被替换下来的物品 uid（0 = 无）。</summary>
        public int Equip(int uid, ItemDef def)
        {
            if (def == null || def.Slot == EquipSlot.None) return 0;
            ItemInstance inst = Find(uid);
            if (inst == null) return 0;

            int displaced = 0;
            if (_equipped.TryGetValue(def.Slot, out displaced)) _equipped.Remove(def.Slot);
            _equipped[def.Slot] = uid;

            // BoE：装备后绑定
            if (def.Slot != EquipSlot.None) inst.Bound = true;
            return displaced;
        }

        public bool Unequip(EquipSlot slot)
        {
            return _equipped.Remove(slot);
        }

        public bool TryGetEquipped(EquipSlot slot, out int uid)
        {
            return _equipped.TryGetValue(slot, out uid);
        }

        /// <summary>汇总装备提供的属性（刷新 ActorState 用）。</summary>
        public void Aggregate(ItemTable table, out float eAP, out float eSP, out float eHP,
            out float armor, out float weaponDps, out float weaponSpeed)
        {
            eAP = 0f; eSP = 0f; eHP = 0f; armor = 0f; weaponDps = 0f; weaponSpeed = 2.0f;
            foreach (KeyValuePair<EquipSlot, int> kv in _equipped)
            {
                ItemInstance inst = Find(kv.Value);
                if (inst == null) continue;
                ItemDef def = table.Get(inst.DefId);
                if (def == null) continue;
                eAP += def.E_AP;
                eSP += def.E_SP;
                eHP += def.E_HP;
                armor += def.Armor;
                if (def.Slot == EquipSlot.Weapon)
                {
                    weaponDps = def.WeaponDps;
                    weaponSpeed = def.WeaponSpeed;
                }
            }
        }

        /// <summary>
        /// 清空背包与装备槽，并把 uid 计数器复位到 1。
        /// 复位是为了让「存档回放」得到与存档时一致的 uid，否则读档后 Find(uid) 会失配、
        /// 导致已穿戴装备全部丢失（缺陷 N-5 的连带问题）。
        /// </summary>
        public void Clear()
        {
            _bag.Clear();
            _equipped.Clear();
            _nextUid = 1;
        }
    }

    /// <summary>物品表（本期为内置静态表，后续可由服务端热配置下发 —— ADR-001 §4.3）。</summary>
    public sealed class ItemTable
    {
        private static readonly Dictionary<int, ItemDef> _defs = new Dictionary<int, ItemDef>();

        static ItemTable()
        {
            // 武器（W / S 直接进伤害公式 —— 职业系统 GDD §2.7.1）
            Add(new ItemDef { Id = 1001, Name = "青锋长剑", Quality = ItemQuality.Common, Slot = EquipSlot.Weapon, Level = 1, WeaponDps = 12f, WeaponSpeed = 2.0f, E_AP = 6f, RestrictToClass = ClassId.SwordCultivator, VendorPrice = 12 });
            Add(new ItemDef { Id = 1002, Name = "铁脊重刃", Quality = ItemQuality.Common, Slot = EquipSlot.Weapon, Level = 1, WeaponDps = 14f, WeaponSpeed = 2.2f, E_AP = 6f, RestrictToClass = ClassId.Berserker, VendorPrice = 12 });
            Add(new ItemDef { Id = 1011, Name = "流刃·霜锋", Quality = ItemQuality.Fine, Slot = EquipSlot.Weapon, Level = 6, WeaponDps = 26f, WeaponSpeed = 2.0f, E_AP = 14f, RestrictToClass = ClassId.SwordCultivator, VendorPrice = 45 });
            Add(new ItemDef { Id = 1012, Name = "裂骨双刃", Quality = ItemQuality.Fine, Slot = EquipSlot.Weapon, Level = 6, WeaponDps = 30f, WeaponSpeed = 2.2f, E_AP = 14f, RestrictToClass = ClassId.Berserker, VendorPrice = 45 });
            Add(new ItemDef { Id = 1021, Name = "渡口·断雲", Quality = ItemQuality.Rare, Slot = EquipSlot.Weapon, Level = 12, WeaponDps = 44f, WeaponSpeed = 2.0f, E_AP = 26f, RestrictToClass = ClassId.SwordCultivator, VendorPrice = 120 });
            Add(new ItemDef { Id = 1022, Name = "渡口·饮血", Quality = ItemQuality.Rare, Slot = EquipSlot.Weapon, Level = 12, WeaponDps = 50f, WeaponSpeed = 2.2f, E_AP = 26f, RestrictToClass = ClassId.Berserker, VendorPrice = 120 });

            // 护甲部件
            AddArmor(2001, "粗布头巾", ItemQuality.Common, EquipSlot.Head, 1, 3f, 2f);
            AddArmor(2002, "粗布衣", ItemQuality.Common, EquipSlot.Chest, 1, 5f, 4f);
            AddArmor(2011, "劲装束发", ItemQuality.Fine, EquipSlot.Head, 5, 8f, 7f);
            AddArmor(2012, "劲装胸甲", ItemQuality.Fine, EquipSlot.Chest, 5, 12f, 11f);
            AddArmor(2013, "劲装护腕", ItemQuality.Fine, EquipSlot.Hands, 5, 6f, 6f);
            AddArmor(2021, "渡口束冠", ItemQuality.Rare, EquipSlot.Head, 11, 16f, 16f);
            AddArmor(2022, "渡口胸铠", ItemQuality.Rare, EquipSlot.Chest, 11, 24f, 24f);
            AddArmor(2023, "渡口战靴", ItemQuality.Rare, EquipSlot.Feet, 11, 14f, 14f);

            // 消耗品（绷带）
            Add(new ItemDef { Id = 3001, Name = "草木绷带", Quality = ItemQuality.Common, Slot = EquipSlot.None, Level = 1, VendorPrice = 3 });

            // —— 副本/团本 BOSS 专属唯一掉落（D-21 / 团本 GDD §2.5.3，U-01 ~ U-52）——
            // id 段：5001(U-01) ~ 5052(U-52)；品质：5 人本=史诗(4)，团本=传说(5)；全部本期仅占位（绑定由掉落上下文决定，一律 BoP）。
            AddDungeonUniques();

            // —— 副本/团本通用掉落占位件（按品质阶层，供掉落结算引用）——
            Add(new ItemDef { Id = 6001, Name = "副本·精良装备",     Quality = ItemQuality.Fine,      Slot = EquipSlot.Trinket, Level = 60, EffectTag = "通用掉落占位·精良",   VendorPrice = 10 });
            Add(new ItemDef { Id = 6002, Name = "副本·稀有装备",     Quality = ItemQuality.Rare,      Slot = EquipSlot.Trinket, Level = 60, EffectTag = "通用掉落占位·稀有",   VendorPrice = 20 });
            Add(new ItemDef { Id = 6003, Name = "副本·史诗装备",     Quality = ItemQuality.Epic,      Slot = EquipSlot.Trinket, Level = 60, EffectTag = "通用掉落占位·史诗",   VendorPrice = 40 });
            Add(new ItemDef { Id = 6004, Name = "团本·传说装备",     Quality = ItemQuality.Legendary, Slot = EquipSlot.Trinket, Level = 60, EffectTag = "通用掉落占位·传说",   VendorPrice = 80 });
            Add(new ItemDef { Id = 6005, Name = "团本·次神话装备",   Quality = ItemQuality.SubMythic, Slot = EquipSlot.Trinket, Level = 60, EffectTag = "通用掉落占位·次神话", VendorPrice = 160 });
            Add(new ItemDef { Id = 6006, Name = "团本·最高级神话装备", Quality = ItemQuality.Mythic,   Slot = EquipSlot.Trinket, Level = 60, EffectTag = "通用掉落占位·最高级神话", VendorPrice = 320 });

            // —— 武器碎片（D-24：20 人本统一碎片，不分类别/职业，30 块换任意一件超神话武器；任何副本不产成品）——
            Add(new ItemDef { Id = 7001, Name = "武器碎片", Quality = ItemQuality.Common, Slot = EquipSlot.None, Level = 60, EffectTag = "D-24 超神话武器兑换材料(统一碎片,30块换任意一件)", VendorPrice = 0 });
        }

        private static void Add(ItemDef def)
        {
            _defs[def.Id] = def;
        }

        private static void AddArmor(int id, string name, ItemQuality q, EquipSlot slot, int level, float armor, float eap)
        {
            Add(new ItemDef
            {
                Id = id, Name = name, Quality = q, Slot = slot, Level = level,
                Armor = armor, E_AP = eap, VendorPrice = 5 + level * 3
            });
        }

        /// <summary>
        /// 副本/团本 BOSS 专属唯一掉落（D-21 / 团本 GDD §2.5.3，U-01 ~ U-52）。
        /// 本期仅落「标签 + 占位」：特效写 GDD 原文定位，数值结算留给战斗侧。
        /// </summary>
        private static void AddUnique(int id, string name, ItemQuality q, EquipSlot slot, int level, string tag)
        {
            Add(new ItemDef
            {
                Id = id, Name = name, Quality = q, Slot = slot, Level = level,
                EffectTag = tag, VendorPrice = 50 + level
            });
        }

        /// <summary>
        /// 副本/团本 BOSS 专属唯一掉落数据（D-21 + 团本 GDD §2.5.3，U-01 ~ U-52）。
        /// 编号与 BossId 一一对应由 Core.DungeonLoot 的 BossUniqueItem 维护。
        /// 品质：5 人本（工厂 + 固定）= 史诗(4)；团本 = 传说(5)。槽位按 GDD 原表。
        /// 本期仅落「标签 + 占位」：特效写 GDD 原文定位，数值结算留给战斗侧（U-5 / A7 / A8 层级约束已在 GDD 校验通过）。
        /// </summary>
        private static void AddDungeonUniques()
        {
            // —— A 组：随机本工厂 BOSS 池（U-01 ~ U-22，品质 4 史诗，Level 随档位 15+i*2）——
            AddUnique(5001, "蚀岩胫甲",     ItemQuality.Epic, EquipSlot.Legs,    15, "受击时生成石肤护盾(P_h×0.60,CD20s) [A5]");
            AddUnique(5002, "枯骨符帽",     ItemQuality.Epic, EquipSlot.Head,    17, "持续伤害(DoT)类技能伤害+12% [A1]");
            AddUnique(5003, "獠牙重刃",     ItemQuality.Epic, EquipSlot.Weapon,  19, "位移/突进类技能K+10% [A1]");
            AddUnique(5004, "影袭护腕",     ItemQuality.Epic, EquipSlot.Hands,   21, "背击(目标背面120°扇区)暴击率+8% [A4]");
            AddUnique(5005, "云篆法杖",     ItemQuality.Epic, EquipSlot.Weapon,  23, "法术作用半径+15%(半径类技能) [A1]");
            AddUnique(5006, "铜甲胸铠",     ItemQuality.Epic, EquipSlot.Chest,   25, "受到物理伤害−8% [A5]");
            AddUnique(5007, "陶纹圣印",     ItemQuality.Epic, EquipSlot.Offhand, 27, "召唤物/持续类技能持续时间+15% [A1]");
            AddUnique(5008, "霜牙战靴",     ItemQuality.Epic, EquipSlot.Feet,    29, "使用位移技能后3s内移速+20% [A1]");
            AddUnique(5009, "双生石肩",     ItemQuality.Epic, EquipSlot.Trinket, 31, "受到致命伤害时免疫一次(保留1HP,CD180s) [特殊]");
            AddUnique(5010, "爬行者之握",   ItemQuality.Epic, EquipSlot.Hands,   33, "造成伤害的3%转化为自身治疗 [A1]");
            AddUnique(5011, "熔渣护心",     ItemQuality.Epic, EquipSlot.Chest,   35, "受到火焰类伤害−20% [A5]");
            AddUnique(5012, "蜃楼面纱",     ItemQuality.Epic, EquipSlot.Head,    37, "施法被打断概率−30% [A2]");
            AddUnique(5013, "银辉短刃",     ItemQuality.Epic, EquipSlot.Weapon,  39, "远程技能暴击率+8% [A4]");
            AddUnique(5014, "亡魂胫甲",     ItemQuality.Epic, EquipSlot.Legs,    41, "脱战后3s内移速+30%(逃离向) [A1]");
            AddUnique(5015, "玄龟背甲",     ItemQuality.Epic, EquipSlot.Trinket, 43, "生命上限+8% [A5]");
            AddUnique(5016, "卢恩石盾",     ItemQuality.Epic, EquipSlot.Offhand, 45, "受到伤害−7%(通用减伤) [A5]");
            AddUnique(5017, "魔藤缠腕",     ItemQuality.Epic, EquipSlot.Hands,   47, "自身施加的减速效果强度+15%(不改控制时长) [A1]");
            AddUnique(5018, "双狼追猎靴",   ItemQuality.Epic, EquipSlot.Feet,    49, "击杀敌人后5s内移速+25% [A1]");
            AddUnique(5019, "守渊重锤",     ItemQuality.Epic, EquipSlot.Weapon,  51, "终极技能(槽6)K+12% [A1]");
            AddUnique(5020, "星陨坠饰",     ItemQuality.Epic, EquipSlot.Trinket, 53, "核心技能(槽5)CD−8% [A2]");
            AddUnique(5021, "万骨碑冠",     ItemQuality.Epic, EquipSlot.Head,    55, "治疗与护盾类效果+10% [A1]");
            AddUnique(5022, "归墟之牙",     ItemQuality.Epic, EquipSlot.Weapon,  57, "对生命值<30%的目标伤害+15% [A1]");

            // —— B 组：60 级 8 固定五人本（U-23 ~ U-38，品质 4 史诗，IL 305/315/325/340）——
            AddUnique(5023, "初潮涌靴",     ItemQuality.Epic, EquipSlot.Feet,    305, "脱战后资源回复速率+30% [A3]");
            AddUnique(5024, "颚兽颚甲",     ItemQuality.Epic, EquipSlot.Chest,   305, "近战承受伤害−8% [A5]");
            AddUnique(5025, "云海剑穗",     ItemQuality.Epic, EquipSlot.Trinket, 305, "剑修御剑姿态持续时间+2s [A7剑修]");
            AddUnique(5026, "断剑护腕",     ItemQuality.Epic, EquipSlot.Hands,   305, "连击类技能最后一段K+15% [A1]");
            AddUnique(5027, "桂冠冠冕",     ItemQuality.Epic, EquipSlot.Head,    315, "圣殿骑士守护结界半径+1m [A7圣殿骑士]");
            AddUnique(5028, "冠军胫甲",     ItemQuality.Epic, EquipSlot.Legs,    315, "变身期间移速+15% [A6]");
            AddUnique(5029, "冥河渡灯",     ItemQuality.Epic, EquipSlot.Offhand, 315, "复活时间−15% [特殊]");
            AddUnique(5030, "溺亡者兜帽",   ItemQuality.Epic, EquipSlot.Head,    315, "沉默持续时间−30% [A5]");
            AddUnique(5031, "锻炉锤",       ItemQuality.Epic, EquipSlot.Weapon,  315, "狂战士嗜血系数B_max+0.06 [A7狂战]");
            AddUnique(5032, "炉火战靴",     ItemQuality.Epic, EquipSlot.Feet,    315, "站立不动满2s后伤害+8%(移动即失效) [A1]");
            AddUnique(5033, "腐根缠甲",     ItemQuality.Epic, EquipSlot.Chest,   325, "秘法师过载安全阈值T_safe+0.4s [A7秘法]");
            AddUnique(5034, "幼嗣鳞盾",     ItemQuality.Epic, EquipSlot.Offhand, 325, "受到持续伤害(DoT)−20% [A5]");
            AddUnique(5035, "封印铭板",     ItemQuality.Epic, EquipSlot.Trinket, 325, "金刚罗汉金身反弹比例+6% [A7金刚]");
            AddUnique(5036, "回响法冠",     ItemQuality.Epic, EquipSlot.Head,    325, "治疗与护盾类效果+8% [A1]");
            AddUnique(5037, "守望者之眼",   ItemQuality.Epic, EquipSlot.Offhand, 340, "暴击伤害+20% [A4]");
            AddUnique(5038, "预兆者的决意", ItemQuality.Epic, EquipSlot.Trinket, 340, "变身期间造成伤害+10% [A6]");

            // —— C 组：10 人团本（U-39 ~ U-46，品质 5 传说，IL 358 / 376；A8 团队光环仅次终极层）——
            AddUnique(5039, "三域残骸重铠", ItemQuality.Legendary, EquipSlot.Chest,   358, "受到物理伤害−10% [A5]");
            AddUnique(5040, "回流之履",     ItemQuality.Legendary, EquipSlot.Feet,    358, "脱战后资源回复速率+40% [A3]");
            AddUnique(5041, "三域回响徽记", ItemQuality.Legendary, EquipSlot.Trinket, 358, "团队光环:半径15m内队友输出+1% [A8]");
            AddUnique(5042, "内环之瞳",     ItemQuality.Legendary, EquipSlot.Offhand, 358, "核心技能(槽5)CD−10% [A2]");
            AddUnique(5043, "枢纽壁垒",     ItemQuality.Legendary, EquipSlot.Offhand, 376, "受到伤害−9%(通用减伤) [A5]");
            AddUnique(5044, "封印共鸣核心", ItemQuality.Legendary, EquipSlot.Trinket, 376, "团队光环:半径15m内队友减伤+1% [A8]");
            AddUnique(5045, "地宫回响之冠", ItemQuality.Legendary, EquipSlot.Head,    376, "治疗与护盾类效果+12% [A1]");
            AddUnique(5046, "未名之决意",   ItemQuality.Legendary, EquipSlot.Trinket, 376, "变身期间造成伤害+12% [A6]");

            // —— D 组：20 人团本（U-47 ~ U-52，品质 5 传说，IL 394；A8 团队光环 +2%）——
            AddUnique(5047, "先驱重刃",     ItemQuality.Legendary, EquipSlot.Weapon,  394, "近战类技能K+12% [A1]");
            AddUnique(5048, "潮汐披鳞",     ItemQuality.Legendary, EquipSlot.Chest,   394, "受到魔法伤害−12% [A5]");
            AddUnique(5049, "三域合体纹章", ItemQuality.Legendary, EquipSlot.Trinket, 394, "团队光环:半径15m内队友输出+2% [A8]");
            AddUnique(5050, "三使徒之冠",   ItemQuality.Legendary, EquipSlot.Head,    394, "暴击伤害+25% [A4]");
            AddUnique(5051, "守门者誓约",   ItemQuality.Legendary, EquipSlot.Legs,    394, "变身期间受到伤害−15% [A6]");
            AddUnique(5052, "终焉之瞳",     ItemQuality.Legendary, EquipSlot.Offhand, 394, "终极技能(槽6)K+15% [A1]");
        }

        private static ItemTable _default;
        public static ItemTable Default => _default ?? (_default = new ItemTable());

        public ItemDef Get(int id)
        {
            ItemDef d;
            return _defs.TryGetValue(id, out d) ? d : null;
        }

        public IReadOnlyCollection<ItemDef> All()
        {
            return _defs.Values;
        }
    }

    public static class ItemText
    {
        public static string QualityName(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Common: return "凡品";
                case ItemQuality.Fine: return "精良";
                case ItemQuality.Rare: return "稀有";
                case ItemQuality.Epic: return "史诗";
                case ItemQuality.Legendary: return "传说";
                case ItemQuality.SubMythic: return "次神话";
                case ItemQuality.Mythic: return "最高级神话";
                case ItemQuality.Transcendent: return "超神话";
                default: return "未知";
            }
        }

        public static string SlotName(EquipSlot s)
        {
            switch (s)
            {
                case EquipSlot.Weapon: return "武器";
                case EquipSlot.Head: return "头部";
                case EquipSlot.Chest: return "胸甲";
                case EquipSlot.Hands: return "手部";
                case EquipSlot.Legs: return "腿部";
                case EquipSlot.Feet: return "足部";
                case EquipSlot.Trinket: return "背饰";
                case EquipSlot.Offhand: return "副手";
                default: return "材料";
            }
        }
    }
}
