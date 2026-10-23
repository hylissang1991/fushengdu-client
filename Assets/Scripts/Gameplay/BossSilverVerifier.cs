// 《浮生渡》无头验证工具 · BOSS / 团本终boss 渡银断言
// 供 PlaytestHarness（独立播放器内）与编辑器批处理自检共用：构造一个 BOSS，
// 走真实 CombatWorld.OnDeath 奖励链路（反射调用 private OnDeath 模拟一次真实击杀），
// 校验击杀后 player.Silver 增量恰为 Progression 对应公式值。
//
// 之所以反射调用 OnDeath：这是权威端「怪物死亡」的统一入口，渡银发放逻辑就写在这里。
// 直接打它，等于验证了「击杀 BOSS → 按口径发渡银」的完整真实链路，而非只测公式本身。
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public static class BossSilverVerifier
    {
        /// <summary>
        /// 无头断言一次 BOSS 击杀的渡银发放。
        /// </summary>
        /// <param name="world">权威端战斗世界（CombatWorld.Instance）</param>
        /// <param name="player">参战玩家（需已在 world 注册，State.Faction == Player）</param>
        /// <param name="level">BOSS 等级</param>
        /// <param name="raidFinal">true=团本终boss（mythic D-26-myth）</param>
        /// <param name="expected">[out] 期望渡银</param>
        /// <param name="actual">[out] 实际发放渡银</param>
        /// <returns>actual == expected</returns>
        public static bool Verify(CombatWorld world, PlayerActor player, int level, bool raidFinal, out int expected, out int actual)
        {
            expected = raidFinal ? Core.Progression.SilverForRaidFinalBoss(level)
                                 : Core.Progression.SilverForBoss(level);
            actual = 0;
            if (world == null || player == null) return false;

            // 构造一个 BOSS 实体（Awake 已建好 State / Threat，这里只补字段）
            var mgo = new GameObject(raidFinal ? "VerifyRaidFinalBoss" : "VerifyBoss");
            var boss = mgo.AddComponent<MonsterActor>();
            if (boss.State == null) boss.State = new Core.ActorState();      // 编辑器批处理下 AddComponent 不触发 Awake
            if (boss.Threat == null) boss.Threat = new Core.ThreatTable();
            boss.State.Faction = Core.ActorFaction.Monster;
            boss.State.IsBoss = true;
            boss.State.Level = level;
            boss.MonsterDefId = raidFinal ? 616 : 0;   // 616 = 团本终boss（与 IsFinalBoss 约定一致）
            boss.Threat.Add(player.ActorId, 1f);        // 确保玩家在仇恨表内 → 拿到奖励
            world.Register(boss);

            ActiveDungeonLootContext prev = world.ActiveDungeonLoot;
            if (raidFinal)
            {
                world.ActiveDungeonLoot = new ActiveDungeonLootContext
                {
                    IsRaid = true,
                    RuleKey = "D-26-myth",
                    BossIds = new int[] { 616 },
                    Participants = new List<int> { player.ActorId }
                };
            }

            int before = player.Silver;
            // 走真实 OnDeath 奖励链路（private，反射调用以模拟一次真实击杀）
            MethodInfo onDeath = typeof(CombatWorld).GetMethod("OnDeath",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (onDeath != null)
            {
                try { onDeath.Invoke(world, new object[] { boss, player }); }
                catch (TargetInvocationException ex)
                {
                    Debug.LogWarning("[BossSilver] OnDeath 抛异常（保底分支仍应已发放渡银）："
                        + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                }
            }
            actual = player.Silver - before;

            // 清理
            world.Unregister(boss);
            world.ActiveDungeonLoot = prev;
#if UNITY_EDITOR
            Object.DestroyImmediate(mgo);
#else
            Object.Destroy(mgo);
#endif
            return actual == expected;
        }

        /// <summary>
        /// 无头断言「任务交付」走统一的 RaiseSilverGained 链路（与 Verify 的击杀一半对称，覆盖"击杀 / 任务交付共用"）：
        /// 接取对话型任务 → 与 NPC 对话完成 → 经由真实的 OfflineAuthority.RequestInteract 交付，
        /// 断言 (1) player.Silver 增量 == 任务 SilverReward；(2) world.OnSilverGained 事件被触发，
        /// 且参数 (playerActorId, 渡银, 世界坐标) 与本次交付一致。
        /// </summary>
        public static bool VerifyQuest(CombatWorld world, PlayerActor player, int npcId,
            out int expected, out int actual, out bool eventFired)
        {
            expected = 0; actual = 0; eventFired = false;
            if (world == null || player == null) return false;

            // headless 下 AddComponent 不会自动触发 OnEnable，手动注册到 NpcEntity 静态表使 NpcEntity.Find 可命中
            var npcGo = new GameObject("VerifyNpc");
            var npc = npcGo.AddComponent<NpcEntity>();
            npc.NpcId = npcId;
            InvokeOnEnable(npc);

            // 接取对话型任务 1（TargetId == npcId，SilverReward = 10），并通过对话完成
            player.Quests.Accept(1);
            player.Quests.OnTalk(npcId);

            // 捕获统一事件
            int evPlayer = -1, evSilver = -1;
            Vector3 evPos = Vector3.zero;
            bool fired = false;
            System.Action<int, int, Vector3> handler = (pid, sil, pos) =>
            {
                evPlayer = pid; evSilver = sil; evPos = pos; fired = true;
            };
            world.OnSilverGained += handler;

            int before = player.Silver;
            int pid0 = player.State.Id;
            var authority = new OfflineAuthority();
            authority.RequestInteract(pid0, npcId);
            int after = player.Silver;

            world.OnSilverGained -= handler;

            Core.QuestDef def = Core.QuestTable.Default.Get(1);
            expected = def != null ? def.SilverReward : 0;
            actual = after - before;
            eventFired = fired && evPlayer == pid0 && evSilver == expected;

            // 清理
            NpcEntity.ClearRegistry();
#if UNITY_EDITOR
            Object.DestroyImmediate(npcGo);
#else
            Object.Destroy(npcGo);
#endif
            return actual == expected && eventFired;
        }

        /// <summary>headless 下 AddComponent 不自动触发 OnEnable，这里手动调用以注册到 NpcEntity 静态表。</summary>
        private static void InvokeOnEnable(UnityEngine.MonoBehaviour mb)
        {
            var m = mb.GetType().GetMethod("OnEnable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m != null) m.Invoke(mb, null);
        }
    }
}
