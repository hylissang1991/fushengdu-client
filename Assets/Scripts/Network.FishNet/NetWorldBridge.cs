// 《浮生渡》网络适配层 · 世界事件广播与服务端交互处理
// @trace ADR-001 N-2（服务端权威结算，客户端只收事件）
// @trace D-19（拾取三选一由服务端掷骰与分配）
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Fushengdu.Network.FishNet
{
    using Abstraction;
    using Fushengdu.Core;
    using Fushengdu.Gameplay;

    /// <summary>
    /// 挂在场景中一个带 NetworkObject 的对象上：订阅 CombatWorld 的服务端事件并向观察者广播。
    /// </summary>
    public sealed class NetWorldBridge : NetworkBehaviour
    {
        public static NetWorldBridge Instance { get; private set; }

        public override void OnStartNetwork()
        {
            Instance = this;
        }

        public override void OnStartServer()
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            world.OnCombatEvent -= OnWorldEvent;
            world.OnCombatEvent += OnWorldEvent;
            world.OnLootSessionStarted -= OnLootStarted;
            world.OnLootSessionStarted += OnLootStarted;
            world.OnLootResolved -= OnLootResolved;
            world.OnLootResolved += OnLootResolved;
            world.OnXpGained -= OnXp;
            world.OnXpGained += OnXp;
            world.OnLevelUp -= OnLevelUp;
            world.OnLevelUp += OnLevelUp;
            world.OnItemGranted -= OnItemGranted;
            world.OnItemGranted += OnItemGranted;
            world.OnPlayerRespawned -= OnPlayerRespawned;
            world.OnPlayerRespawned += OnPlayerRespawned;
            world.OnPlayerRespawnCountdown -= OnPlayerCountdown;
            world.OnPlayerRespawnCountdown += OnPlayerCountdown;
            world.OnSilverGained += OnSilver;
        }

        public override void OnStopServer()
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            world.OnCombatEvent -= OnWorldEvent;
            world.OnLootSessionStarted -= OnLootStarted;
            world.OnLootResolved -= OnLootResolved;
            world.OnXpGained -= OnXp;
            world.OnLevelUp -= OnLevelUp;
            world.OnItemGranted -= OnItemGranted;
            world.OnPlayerRespawned -= OnPlayerRespawned;
            world.OnPlayerRespawnCountdown -= OnPlayerCountdown;
            world.OnSilverGained -= OnSilver;
        }

        private void OnPlayerRespawned(int playerId, UnityEngine.Vector3 point)
        {
            // 服务端权威触发复活 → 广播给所有观察者（缺陷 N-1）
            RpcRespawn(playerId, point.x, point.y, point.z);
        }

        private void OnPlayerCountdown(int playerId, float remaining)
        {
            RpcRespawnCountdown(playerId, remaining);
        }

        private void OnWorldEvent(CombatEvent e)
        {
            RpcCombatEvent((int)e.Kind, e.SourceId, e.TargetId, e.Amount, (int)e.Skill);
        }

        private void OnLootStarted(LootSessionRuntime session)
        {
            RpcLootStarted(session.SessionId, session.Session.ItemDefId, session.Position.x, session.Position.y, session.Position.z);
            UiLootHooks.RaiseLootStarted(session.SessionId, session.Session.ItemDefId, session.Position);
        }

        private void OnLootResolved(int winnerId, int itemDefId, LootChoice tier)
        {
            RpcLootResolved(winnerId, itemDefId, (int)tier);
        }

        private void OnXp(int playerId, int xp)
        {
            RpcXp(playerId, xp);
        }

        private void OnLevelUp(int playerId, int level)
        {
            RpcLevelUp(playerId, level);
        }

        private void OnItemGranted(int playerId, int itemDefId)
        {
            RpcItemGranted(playerId, itemDefId);
        }

        private void OnSilver(int playerId, int silver, Vector3 pos)
        {
            // 服务端权威触发渡银到账 → 广播给所有观察者（含纯客户端）
            RpcSilver(playerId, silver, pos.x, pos.y, pos.z);
        }

        // ------------------------------------------------------------ RPC
        [ObserversRpc]
        private void RpcCombatEvent(int kind, int sourceId, int targetId, float amount, int skillId)
        {
            var e = new CombatEvent
            {
                Kind = (CombatEventKind)kind, SourceId = sourceId, TargetId = targetId,
                Amount = amount, Skill = (SkillId)skillId
            };
            CombatWorld world = CombatWorld.Instance;
            if (world != null) world.PushEvent(e);
        }

        [ObserversRpc]
        private void RpcLootStarted(int sessionId, int itemDefId, float x, float y, float z)
        {
            UiLootHooks.RaiseLootStarted(sessionId, itemDefId, new UnityEngine.Vector3(x, y, z));
        }

        [ObserversRpc]
        private void RpcLootResolved(int winnerId, int itemDefId, int tier)
        {
            UiLootHooks.RaiseLootResolved(winnerId, itemDefId, tier);
        }

        [ObserversRpc]
        private void RpcXp(int playerId, int xp)
        {
            UiLootHooks.RaiseXp(playerId, xp);
        }

        [ObserversRpc]
        private void RpcLevelUp(int playerId, int level)
        {
            UiLootHooks.RaiseLevelUp(playerId, level);
        }

        [ObserversRpc]
        private void RpcItemGranted(int playerId, int itemDefId)
        {
            UiLootHooks.RaiseItemGranted(playerId, itemDefId);
        }

        [ObserversRpc]
        private void RpcSilver(int playerId, int silver, float x, float y, float z)
        {
            UiLootHooks.RaiseSilver(playerId, silver, new Vector3(x, y, z));
        }

        /// <summary>复活广播：客户端据此把角色传送到复活点（缺陷 N-1）。</summary>
        [ObserversRpc]
        private void RpcRespawn(int playerId, float x, float y, float z)
        {
            if (base.IsServerInitialized) return; // 服务端已在 CombatWorld 内传送过
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            ActorEntity actor = world.Get(playerId);
            var player = actor as PlayerActor;
            if (player == null) return;
            player.Teleport(new UnityEngine.Vector3(x, y, z));
            player.SetTarget(null);
        }

        /// <summary>复活倒计时广播：让客户端 UI 显示一致的剩余秒数。</summary>
        [ObserversRpc]
        private void RpcRespawnCountdown(int playerId, float remaining)
        {
            UiLootHooks.RaiseRespawnCountdown(playerId, remaining);
        }

        /// <summary>服务端处理 NPC 交互（接取 / 交付任务）。走离线同一套逻辑，保证结果一致。</summary>
        public void HandleInteractOnServer(int playerId, int npcId)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            ActorEntity actor = world.Get(playerId);
            var player = actor as PlayerActor;
            if (player == null) return;

            NpcEntity npc = NpcEntity.Find(npcId);
            if (npc == null) return;

            player.Quests.OnTalk(npcId);

            var toTurnIn = new System.Collections.Generic.List<int>();
            foreach (QuestProgress p in player.Quests.Active)
                if (player.Quests.CanTurnIn(p.QuestId)) toTurnIn.Add(p.QuestId);

            int questSilver = 0;
            for (int i = 0; i < toTurnIn.Count; i++)
            {
                QuestDef def = QuestTable.Default.Get(toTurnIn[i]);
                int next = player.Quests.TurnIn(toTurnIn[i]);
                if (def == null) continue;
                int before = player.Progression.Level;
                player.Progression.AddXp(def.XpReward);
                player.Silver += def.SilverReward;
                questSilver += def.SilverReward;
                if (def.ItemRewardDefId != 0) player.Inventory.Add(def.ItemRewardDefId, false);
                if (player.Progression.Level != before)
                    Progression.ApplyLevel(player.State, player.Progression.Level);
                if (next != 0) player.Quests.Accept(next);
            }
            // 任务交付渡银飘字：聚合本次交付的总渡银，仅在本地玩家视角触发（playerId 过滤在 UI 侧）
            if (questSilver > 0)
                world.RaiseSilverGained(playerId, questSilver, player.transform.position);

            foreach (QuestDef def in QuestTable.Default.All())
            {
                if (player.Quests.IsAccepted(def.Id) || player.Quests.IsDone(def.Id)) continue;
                if (def.PredecessorId != 0 && !player.Quests.IsDone(def.PredecessorId)) continue;
                player.Quests.Accept(def.Id);
            }

            player.RefreshFromEquipment();
        }
    }
}
