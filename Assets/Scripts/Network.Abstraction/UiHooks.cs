// 《浮生渡》网络抽象层 · UI 事件钩子
// 用途：网络适配层（FishNet）把服务端事件转交 UI，但网络程序集不引用 UI 程序集。
// UI 订阅本静态类的事件，网络层调用 Raise* 方法（单向依赖，符合 ADR-001 N-8）。
using System;
using UnityEngine;

namespace Fushengdu.Network.Abstraction
{
    public static class UiLootHooks
    {
        /// <summary>(sessionId, itemDefId, 掉落位置)</summary>
        public static Action<int, int, Vector3> OnLootStarted;

        /// <summary>(winnerActorId, itemDefId, 获胜档位)</summary>
        public static Action<int, int, int> OnLootResolved;

        /// <summary>(playerActorId, xp)</summary>
        public static Action<int, int> OnXpGained;

        /// <summary>(playerActorId, newLevel)</summary>
        public static Action<int, int> OnLevelUp;

        /// <summary>(playerActorId, itemDefId)</summary>
        public static Action<int, int> OnItemGranted;

        /// <summary>(playerActorId, 剩余复活秒数) —— 缺陷 N-1</summary>
        public static Action<int, float> OnRespawnCountdown;

        public static void RaiseLootStarted(int sessionId, int itemDefId, Vector3 position)
        {
            Action<int, int, Vector3> h = OnLootStarted;
            if (h != null) h(sessionId, itemDefId, position);
        }

        public static void RaiseLootResolved(int winnerId, int itemDefId, int tier)
        {
            Action<int, int, int> h = OnLootResolved;
            if (h != null) h(winnerId, itemDefId, tier);
        }

        public static void RaiseXp(int playerId, int xp)
        {
            Action<int, int> h = OnXpGained;
            if (h != null) h(playerId, xp);
        }

        public static void RaiseLevelUp(int playerId, int level)
        {
            Action<int, int> h = OnLevelUp;
            if (h != null) h(playerId, level);
        }

        public static void RaiseItemGranted(int playerId, int itemDefId)
        {
            Action<int, int> h = OnItemGranted;
            if (h != null) h(playerId, itemDefId);
        }

        public static void RaiseRespawnCountdown(int playerId, float remaining)
        {
            Action<int, float> h = OnRespawnCountdown;
            if (h != null) h(playerId, remaining);
        }

        /// <summary>(playerActorId, 渡银, 世界坐标) —— 击杀 / 任务交付的渡银到账，供客户端飘字。</summary>
        public static Action<int, int, Vector3> OnSilverGained;

        public static void RaiseSilver(int playerId, int silver, Vector3 position)
        {
            Action<int, int, Vector3> h = OnSilverGained;
            if (h != null) h(playerId, silver, position);
        }
    }
}
