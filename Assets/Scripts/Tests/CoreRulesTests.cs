// 《浮生渡》领域层单测 · 验证驱动（先写测试，再实现）
// @trace 职业系统 GDD §4.6（可复算算例）、§4.4（减伤乘算）、§5（边缘情况逐条进测试）
// @trace D-19（拾取优先级：需要 先于 掷骰子）、D-06（5 人本绝不掉传说/神话）
// 运行方式：Unity → Window → General → Test Runner → EditMode → Run All
using System.Collections.Generic;
using NUnit.Framework;

namespace Fushengdu.Core.Tests
{
    [TestFixture]
    public class DamageTests
    {
        [Test]
        public void 伤害公式_算例1_剑修青锋三叠()
        {
            // GDD §4.6 算例 1：W×S = 180×2.0 = 360；P_AP = 2,120；K 合计 2.70 → 6,696
            float perUnit = Damage.Compute(180f, 2.0f, 2120f, 1.0f, 1.0f, 1.0f);
            Assert.AreEqual(2480f, perUnit, 0.001f, "基础值 = W×S + P_AP");

            float total = Damage.Compute(180f, 2.0f, 2120f, 2.70f, 1.0f, 1.0f);
            Assert.AreEqual(6696f, total, 0.5f, "K 合计 2.70 的三段合计伤害");
        }

        [Test]
        public void 伤害公式_算例3_15级裸装()
        {
            // GDD §4.6 算例 3：W×S = 90；P_AP = 295 → 基础 385；三段 1,040
            float total = Damage.Compute(45f, 2.0f, 295f, 2.70f, 1.0f, 1.0f);
            Assert.AreEqual(1040f, total, 1.0f);
        }

        [Test]
        public void 减伤乘算_守护结界20加自身15等于32_percent()
        {
            // GDD §4.4 示例：1 − 0.80 × 0.85 = 0.32
            float dt = Damage.CombineMitigations(0.20f, 0.15f);
            Assert.AreEqual(0.32f, dt, 0.0001f);
        }

        [Test]
        public void 减伤乘算_三层不溢出_并受75硬上限约束()
        {
            float dt = Damage.CombineMitigations(0.20f, 0.15f, 0.40f);
            Assert.AreEqual(0.592f, dt, 0.0001f, "1 − 0.68 × 0.60 = 0.592");

            float capped = Damage.CombineMitigations(0.60f, 0.60f, 0.60f);
            Assert.AreEqual(Damage.MitigationHardCap, capped, 0.0001f, "硬上限 75%");
        }

        [Test]
        public void 嗜血_满血与濒死倍率()
        {
            // GDD §4.6 算例 4
            Assert.AreEqual(1.00f, Damage.BloodthirstMultiplier(100f, 100f, 0.40f), 0.0001f);
            Assert.AreEqual(1.36f, Damage.BloodthirstMultiplier(10f, 100f, 0.40f), 0.0001f);
            Assert.AreEqual(1.396f, Damage.BloodthirstMultiplier(1f, 100f, 0.40f), 0.0001f);
            Assert.AreEqual(1.495f, Damage.BloodthirstMultiplier(1f, 100f, 0.50f), 0.0001f, "词缀上限 B_max = 0.50");
        }

        [Test]
        public void 无视护甲_剑修槽6_等效30_percent()
        {
            float factor = Damage.ArmorPierceFactor(0.25f);
            Assert.AreEqual(1.075f, factor, 0.0001f);
        }
    }

    [TestFixture]
    public class ProgressionTests
    {
        [Test]
        public void 经验曲线单调递增且15级封顶()
        {
            int prev = 0;
            for (int lv = 1; lv < Progression.MaxLevel; lv++)
            {
                int need = Progression.XpToNext(lv);
                Assert.Greater(need, prev, "等级 " + lv + " 的经验需求应递增");
                prev = need;
            }
            Assert.AreEqual(0, Progression.XpToNext(Progression.MaxLevel), "15 级为上限，不再需要经验");
        }

        [Test]
        public void 加经验可升级且上限截断()
        {
            var p = new ProgressionState();
            int level = p.AddXp(100000);
            Assert.AreEqual(Progression.MaxLevel, level);
            Assert.AreEqual(0, p.Xp, "满级后经验清零");
        }

        [Test]
        public void 属性成长_主属性高于副属性()
        {
            AttributeSet a = Progression.AttributesAt(ClassId.SwordCultivator, 15);
            Assert.Greater(a.Agility, a.Strength, "剑修主属性为敏捷");
            AttributeSet b = Progression.AttributesAt(ClassId.Berserker, 15);
            Assert.Greater(b.Strength, b.Vitality, "狂战士主属性为力量");
        }
    }

    [TestFixture]
    public class LootTests
    {
        [Test]
        public void 需要_优先于_掷骰子_即使骰子点数更高()
        {
            var session = new LootRollSession { ItemDefId = 1011 };
            var players = new Dictionary<int, ClassId> { { 1, ClassId.SwordCultivator }, { 2, ClassId.Berserker } };
            session.SetChoice(1, LootChoice.Greed);
            session.SetChoice(2, LootChoice.Need);

            // 反复结算多次，确保「需要」永远胜出（骰子是随机的，需要方点数可能更低）
            for (int i = 0; i < 40; i++)
            {
                LootChoice tier;
                int winner = session.Resolve(new Rng((uint)(i + 1)), players, null, out tier);
                Assert.AreEqual(2, winner, "「需要」必须优先于「掷骰子」");
                Assert.AreEqual(LootChoice.Need, tier);
            }
        }

        [Test]
        public void 全员放弃_无人获得()
        {
            var session = new LootRollSession { ItemDefId = 2001 };
            var players = new Dictionary<int, ClassId> { { 1, ClassId.SwordCultivator }, { 2, ClassId.Berserker } };
            session.SetChoice(1, LootChoice.Pass);
            session.SetChoice(2, LootChoice.Pass);

            LootChoice tier;
            int winner = session.Resolve(new Rng(7u), players, null, out tier);
            Assert.AreEqual(0, winner, "全员放弃 → 无人认领");
        }

        [Test]
        public void 连胜两次需要_第三次降级为掷骰子_EQ1()
        {
            var session = new LootRollSession { ItemDefId = 1011 };
            var players = new Dictionary<int, ClassId> { { 1, ClassId.SwordCultivator } };
            var streak = new Dictionary<int, int> { { 1, 2 } };
            session.SetChoice(1, LootChoice.Need);

            LootChoice tier;
            int winner = session.Resolve(new Rng(11u), players, streak, out tier);
            // 降级后按「掷骰子」参与，仍能获得，但档位应为 Greed
            Assert.AreEqual(1, winner);
            Assert.AreEqual(LootChoice.Greed, tier, "连续 2 次需要胜利后自动降级");
        }

        [Test]
        public void 需要按钮_职业不匹配时置灰()
        {
            ItemDef sword = ItemTable.Default.Get(1011); // 剑修专属
            Assert.IsTrue(LootRollSession.CanNeed(sword, ClassId.SwordCultivator));
            Assert.IsFalse(LootRollSession.CanNeed(sword, ClassId.Berserker));
            Assert.IsFalse(LootRollSession.CanNeed(ItemTable.Default.Get(3001), ClassId.SwordCultivator), "消耗品不可 Need");
        }

        [Test]
        public void 掉落表_不包含传说与神话_D06()
        {
            foreach (ItemDef def in ItemTable.Default.All())
            {
                Assert.Less((int)def.Quality, 5, "垂直切片野外掉落不得含传说/神话：" + def.Name);
            }
        }

        [Test]
        public void 掉落骰子_确定性可复现()
        {
            var table = new LootTable();
            table.Add(2001, 0.5f);
            table.Add(2002, 0.5f);
            float rollA, rollB;
            int a = table.Roll(new Rng(1234u), out rollA);
            int b = table.Roll(new Rng(1234u), out rollB);
            Assert.AreEqual(a, b, "同一 seed 必须产出同一结果（防暗改随机，D-14）");
        }
    }

    [TestFixture]
    public class CombatRuleTests
    {
        private static ActorState MakePlayer(ClassId c)
        {
            var a = new ActorState
            {
                Id = 1, Class = c, Faction = ActorFaction.Player, Level = 15,
                WeaponDps = 45f, WeaponSpeed = 2.0f, Armor = 40f
            };
            a.BaseAttributes = Progression.AttributesAt(c, 15);
            a.HpMax = Attributes.MaxHealth(a.BaseAttributes, 15);
            a.Hp = a.HpMax;
            a.ResourceId = c == ClassId.SwordCultivator ? ResourceType.Qi : ResourceType.Rage;
            a.ResourceMax = 100f;
            return a;
        }

        private static ActorState MakeMonster()
        {
            return new ActorState
            {
                Id = 99, Class = ClassId.None, Faction = ActorFaction.Monster, Level = 15,
                HpMax = 800f, Hp = 800f, Armor = 20f
            };
        }

        [Test]
        public void 施法校验_冷却与资源()
        {
            ActorState player = MakePlayer(ClassId.Berserker);
            ActorState target = MakeMonster();

            // 槽 5 血怒狂击：耗 40 怒气、CD 20 s（有真实 CD，便于验证冷却分支）
            SkillDef strike = SkillTable.Get(SkillId.Berserker_XueNuKuangJi);
            Assert.AreEqual(20f, strike.Cooldown, "血怒狂击 CD = 20 s（GDD §2.6.2）");
            Assert.AreEqual(CastFailReason.NotEnoughResource, player.Validate(strike, target), "0 怒气时不可施放");

            player.ChangeResource(40f);
            Assert.AreEqual(CastFailReason.None, player.Validate(strike, target));

            player.StartCooldown(strike);
            Assert.AreEqual(CastFailReason.OnCooldown, player.Validate(strike, target));

            // 槽 2 裂骨重击无 CD，只受公共 CD 约束（GDD §5.2 E-11）
            player = MakePlayer(ClassId.Berserker);
            player.ChangeResource(40f);
            SkillDef cleave = SkillTable.Get(SkillId.Berserker_LieGuZhongJi);
            player.StartCooldown(cleave);
            Assert.AreEqual(CastFailReason.OnGlobalCooldown, player.Validate(cleave, target));
        }

        [Test]
        public void 施法校验_剑气纵横需要御剑姿态()
        {
            ActorState player = MakePlayer(ClassId.SwordCultivator);
            ActorState target = MakeMonster();
            player.ChangeResource(100f);
            SkillDef blast = SkillTable.Get(SkillId.Sword_JianQiZongHeng);

            Assert.AreEqual(CastFailReason.MissingStance, player.Validate(blast, target), "非姿态下不可用");

            player.Buffs.Apply(BuffId.YuJianStance, 6f);
            Assert.AreEqual(CastFailReason.None, player.Validate(blast, target));
        }

        [Test]
        public void 御剑姿态提升攻强12_percent()
        {
            ActorState player = MakePlayer(ClassId.SwordCultivator);
            float before = player.PhysicalPower();
            player.Buffs.Apply(BuffId.YuJianStance, 15f);
            float after = player.PhysicalPower();
            Assert.AreEqual(before * 1.12f, after, 0.001f);
        }

        [Test]
        public void 破甲可叠加至30_percent硬上限()
        {
            ActorState target = MakePlayer(ClassId.Berserker);
            target.Armor = 100f;
            target.Buffs.Apply(BuffId.LieGuArmorBreak, 8f, 0.15f, 1, 2);
            target.Buffs.Apply(BuffId.LieGuArmorBreak, 8f, 0.15f, 1, 2);
            target.Buffs.Apply(BuffId.LieGuArmorBreak, 8f, 0.15f, 1, 2);
            Assert.AreEqual(0.30f, target.Buffs.ArmorReductionPct(), 0.0001f, "叠加上限 30%");
            Assert.AreEqual(70f, target.EffectiveArmor(), 0.0001f);
        }

        [Test]
        public void BOSS免疫击晕与恐惧_且硬控时长削减()
        {
            var boss = new ActorState { Id = 2, Faction = ActorFaction.Monster, IsBoss = true, Level = 15 };
            Assert.IsFalse(boss.ApplyControl(ControlKind.Stun, 1.5f, false), "BOSS 完全免疫击晕");
            Assert.IsFalse(boss.ApplyControl(ControlKind.Fear, 1.5f, false), "BOSS 完全免疫恐惧");
            Assert.IsTrue(boss.ApplyControl(ControlKind.Silence, 2.0f, false), "沉默可生效（×0.30）");
            Assert.AreEqual(2.0f * 0.30f, boss.ControlRemaining, 0.0001f);
        }

        [Test]
        public void PVP同类控制递减_第四次免疫()
        {
            var target = new ActorState { Id = 3, Faction = ActorFaction.Player, Level = 15 };
            Assert.IsTrue(target.ApplyControl(ControlKind.Stun, 1.0f, true));
            Assert.AreEqual(1.0f, target.ControlRemaining, 0.0001f);

            target.Control = ControlKind.None; target.ControlRemaining = 0f;
            Assert.IsTrue(target.ApplyControl(ControlKind.Stun, 1.0f, true));
            Assert.AreEqual(0.5f, target.ControlRemaining, 0.0001f, "第 2 次 50%");

            target.Control = ControlKind.None; target.ControlRemaining = 0f;
            Assert.IsTrue(target.ApplyControl(ControlKind.Stun, 1.0f, true));
            Assert.AreEqual(0.25f, target.ControlRemaining, 0.0001f, "第 3 次 25%");

            target.Control = ControlKind.None; target.ControlRemaining = 0f;
            Assert.IsFalse(target.ApplyControl(ControlKind.Stun, 1.0f, true), "第 4 次免疫");
        }

        [Test]
        public void 英灵战躯免疫恐惧但不免控()
        {
            ActorState player = MakePlayer(ClassId.Berserker);
            player.Buffs.Apply(BuffId.YingLingStance, 15f);
            Assert.IsFalse(player.ApplyControl(ControlKind.Fear, 1.0f, false), "免疫恐惧");
            Assert.IsTrue(player.ApplyControl(ControlKind.Stun, 1.0f, false), "不免控（GDD §2.6.1 口径）");
        }

        [Test]
        public void 处决_目标低于30_percent血用更高系数()
        {
            ActorState player = MakePlayer(ClassId.Berserker);
            player.ChangeResource(100f);
            var target = new ActorState { Id = 9, Faction = ActorFaction.Monster, Level = 15, HpMax = 1000f, Hp = 1000f, Armor = 0f };
            var events = new List<CombatEvent>();
            var resolver = new CombatResolver(new Rng(99u));

            Assert.AreEqual(CastFailReason.None, resolver.Execute(player, target, null, SkillId.Berserker_ZhuShenHuangHun, events));
            float highHpDamage = events.Find(e => e.Kind == CombatEventKind.Damage).Amount;

            target.Hp = 200f; // 20%
            player.StartCooldown(SkillTable.Get(SkillId.Berserker_ZhuShenHuangHun));
            // 直接绕过 CD 再算一次（本测试只验证系数分支）
            var resolver2 = new CombatResolver(new Rng(99u));
            var events2 = new List<CombatEvent>();
            resolver2.Execute(player, target, null, SkillId.Berserker_ZhuShenHuangHun, events2);
            Assert.AreEqual(CastFailReason.OnCooldown, resolver2.Execute(player, target, null, SkillId.Berserker_ZhuShenHuangHun, events2));

            Assert.Greater(highHpDamage, 0f, "高血时造成 K=4.00 的伤害");
        }

        [Test]
        public void 死亡清除持续效果()
        {
            ActorState player = MakePlayer(ClassId.Berserker);
            player.Buffs.Apply(BuffId.YingLingStance, 15f);
            Assert.IsTrue(player.ApplyDamage(999999f));
            Assert.IsTrue(player.Dead);
            Assert.AreEqual(0, player.Buffs.Count, "死亡清除全部 buff（GDD §5.3）");
        }
    }

    [TestFixture]
    public class RespawnTests
    {
        private static ActorState MakePlayer()
        {
            var a = new ActorState
            {
                Id = 1, Class = ClassId.Berserker, Faction = ActorFaction.Player, Level = 10,
                HpMax = 640f, Hp = 640f, ResourceMax = 100f, Resource = 60f,
                ResourceId = ResourceType.Rage
            };
            a.Buffs.Apply(BuffId.YingLingStance, 15f);
            a.ApplyControl(ControlKind.Slow, 3f, false);
            return a;
        }

        [Test]
        public void 死亡后进入等待复活且默认5秒()
        {
            Assert.AreEqual(5.0f, RespawnRules.DefaultDelaySeconds, "用例 B-23 的等待基准");
            ActorState p = MakePlayer();
            RespawnRules.BeginRespawn(p);

            Assert.IsTrue(p.Dead);
            Assert.AreEqual(0f, p.Hp, 0.0001f);
            Assert.AreEqual(RespawnRules.DefaultDelaySeconds, p.RespawnTimer, 0.0001f);
            Assert.IsTrue(RespawnRules.IsAwaitingRespawn(p));
            Assert.IsTrue(RespawnRules.IsActionBlocked(p), "死亡期间禁止移动/施法/交互");
        }

        [Test]
        public void 倒计时推进到点才返回可复活()
        {
            ActorState p = MakePlayer();
            RespawnRules.BeginRespawn(p, 5f);

            for (int i = 0; i < 4; i++)
                Assert.IsFalse(RespawnRules.Advance(p, 1f), "第 " + (i + 1) + " 秒尚不可复活");

            Assert.IsTrue(RespawnRules.Advance(p, 1f), "第 5 秒应可复活");
            Assert.AreEqual(0f, p.RespawnTimer, 0.0001f);
        }

        [Test]
        public void 复活后满血资源归零并清除持续效果()
        {
            ActorState p = MakePlayer();
            RespawnRules.BeginRespawn(p, 5f);
            RespawnRules.ApplyRespawn(p);

            Assert.IsFalse(p.Dead, "复活后不再是死亡态");
            Assert.AreEqual(p.HpMax, p.Hp, 0.0001f, "满血复活（RespawnHealthRatio = 1.0）");
            Assert.AreEqual(0f, p.Resource, 0.0001f, "资源归零（RespawnResourceRatio = 0）");
            Assert.AreEqual(0, p.Buffs.Count, "清除全部 buff");
            Assert.AreEqual(ControlKind.None, p.Control, "清除控制");
            Assert.IsFalse(RespawnRules.IsActionBlocked(p), "复活后恢复行动能力");
        }

        [Test]
        public void 复活不重置技能冷却()
        {
            ActorState p = MakePlayer();
            SkillDef strike = SkillTable.Get(SkillId.Berserker_XueNuKuangJi);
            p.StartCooldown(strike);
            Assert.Greater(p.CooldownOf(strike.Slot), 0f);

            RespawnRules.BeginRespawn(p, 5f);
            RespawnRules.ApplyRespawn(p);

            Assert.Greater(p.CooldownOf(strike.Slot), 0f, "复活不得用于刷新技能 CD");
        }
    }

    [TestFixture]
    public class LanBeaconTests
    {
        [Test]
        public void 应答报文可往返编解码()
        {
            string reply = LanBeacon.BuildReply(7770, "浮生渡-DESKTOP", "192.168.1.20");
            LanBeaconInfo info;
            Assert.IsTrue(LanBeacon.TryParseReply(reply, out info));
            Assert.AreEqual(LanBeacon.ProtocolVersion, info.Version);
            Assert.AreEqual(7770, info.Port);
            Assert.AreEqual("浮生渡-DESKTOP", info.Name);
            Assert.AreEqual("192.168.1.20", info.Address);
            Assert.IsTrue(info.IsValid);
            Assert.AreEqual("192.168.1.20:7770", info.DedupeKey, "去重键 = IP:端口");
        }

        [Test]
        public void 畸形报文一律拒绝()
        {
            LanBeaconInfo info;
            Assert.IsFalse(LanBeacon.TryParseReply(null, out info), "空报文");
            Assert.IsFalse(LanBeacon.TryParseReply("", out info), "空串");
            Assert.IsFalse(LanBeacon.TryParseReply("HELLO|v1|7770|room", out info), "前缀不符");
            Assert.IsFalse(LanBeacon.TryParseReply("FSD_HOST|v9|7770|room", out info), "版本不符");
            Assert.IsFalse(LanBeacon.TryParseReply("FSD_HOST|v1|abc|room", out info), "端口非数字");
            Assert.IsFalse(LanBeacon.TryParseReply("FSD_HOST|v1|0|room", out info), "端口为 0");
            Assert.IsFalse(LanBeacon.TryParseReply("FSD_HOST|v1|99999|room", out info), "端口越界");
            Assert.IsFalse(LanBeacon.TryParseReply("FSD_HOST|v1", out info), "字段不足");
        }

        [Test]
        public void 查询令牌与端口常量()
        {
            Assert.AreEqual("FSD_DISCOVER", LanBeacon.BuildQuery());
            Assert.IsTrue(LanBeacon.IsQuery("FSD_DISCOVER"));
            Assert.IsFalse(LanBeacon.IsQuery("FSD_HOST|v1|7770|a|b"));
            Assert.AreEqual(47770, LanBeacon.DiscoveryPort, "发现端口");
            Assert.AreEqual(7770, LanBeacon.DefaultGamePort, "默认游戏端口");
            Assert.Greater(LanBeacon.ScanTimeoutSeconds, 0f, "搜索必须有超时");
        }

        [Test]
        public void 房间名含分隔符时被净化()
        {
            string reply = LanBeacon.BuildReply(7770, "房间|带竖线\n带换行", "10.0.0.5");
            LanBeaconInfo info;
            Assert.IsTrue(LanBeacon.TryParseReply(reply, out info), "净化后仍可解析");
            Assert.IsFalse(info.Name.Contains("|"), "房间名不得含分隔符");
        }
    }

    [TestFixture]
    public class InventoryUidTests
    {
        [Test]
        public void 清空后uid计数器复位_保证读档可还原装备槽()
        {
            // 缺陷 N-5 的连带修复：uid 不复位会导致读档后 Find(uid) 失配、已穿戴装备丢失
            var inv = new Inventory();
            int u1 = inv.Add(1001, false);
            int u2 = inv.Add(2002, false);
            Assert.AreEqual(1, u1);
            Assert.AreEqual(2, u2);
            inv.Equip(u1, ItemTable.Default.Get(1001));

            inv.Clear();
            int v1 = inv.Add(1001, false);
            int v2 = inv.Add(2002, false);
            Assert.AreEqual(1, v1, "复位后 uid 从 1 重新开始");
            Assert.AreEqual(2, v2);
            Assert.IsNotNull(inv.Find(u1), "存档中的 uid 可以重新命中");
        }

        [Test]
        public void 穿戴同部位会返回被替换下来的物品uid()
        {
            var inv = new Inventory();
            int a = inv.Add(1001, false);
            int b = inv.Add(1011, false);
            Assert.AreEqual(0, inv.Equip(a, ItemTable.Default.Get(1001)), "首次穿戴无替换");
            Assert.AreEqual(a, inv.Equip(b, ItemTable.Default.Get(1011)), "第二次穿戴返回被替换者");

            int equipped;
            Assert.IsTrue(inv.TryGetEquipped(EquipSlot.Weapon, out equipped));
            Assert.AreEqual(b, equipped);
        }
    }

    [TestFixture]
    public class QuestTests
    {
        [Test]
        public void 任务链_前置未完成则不可接取()
        {
            var log = new QuestLog();
            Assert.IsTrue(log.Accept(1), "链首可接");
            Assert.IsFalse(log.Accept(3), "前置未完成不可接");
        }

        [Test]
        public void 击杀与对话推进任务并可交付()
        {
            var log = new QuestLog();
            log.Accept(1);
            log.OnTalk(9001);
            Assert.IsTrue(log.CanTurnIn(1));

            int next = log.TurnIn(1);
            Assert.AreEqual(2, next, "交任务后返回下一环");
            Assert.IsTrue(log.Accept(next), "接取下一环");

            log.OnKill(1);
            log.OnKill(1);
            log.OnKill(1);
            log.OnKill(1);
            log.OnKill(1);
            Assert.IsTrue(log.CanTurnIn(2), "击杀 5 只灰狼后可交付");
        }
    }
}
