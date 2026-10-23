// 《浮生渡》表现层 · 本地玩家定位器
// 用途：UI 与网络适配层之间解耦。网络层在本机玩家生成后写入，UI 轮询读取，
// 从而 UI 程序集无需引用网络框架程序集（ADR-001 N-8）。
using System;

namespace Fushengdu.Gameplay
{
    public static class LocalPlayerLocator
    {
        private static PlayerActor _current;

        public static PlayerActor Current => _current;

        public static event Action<PlayerActor> OnChanged;

        public static void Set(PlayerActor player)
        {
            if (_current == player) return;
            _current = player;
            Action<PlayerActor> handler = OnChanged;
            if (handler != null) handler(player);
        }

        public static void Clear()
        {
            _current = null;
        }
    }
}
