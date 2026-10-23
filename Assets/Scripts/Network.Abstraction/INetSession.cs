// 《浮生渡》网络抽象层 · 会话接口
// @trace ADR-001 N-8：业务层与 UI 通过本接口访问网络，网络框架类型不得外泄。
// 这样即使未来从 FishNet 降级到 Mirror（ADR-001 §3.4 逃生通道），只需替换实现程序集。
using System;

namespace Fushengdu.Network.Abstraction
{
    /// <summary>会话角色。</summary>
    public enum NetRole
    {
        Offline = 0,
        Host = 1,     // 主机（本机同时是服务器与客户端）— 本期 LAN 联机形态
        Client = 2,   // 加入者
        Server = 3    // 专用服务器（本期不启用，留接口）
    }

    /// <summary>连接状态。</summary>
    public enum NetStatus
    {
        Disconnected = 0,
        Starting = 1,
        Connected = 2,
        Failed = 3
    }

    /// <summary>LAN 广播发现的主机条目。</summary>
    public struct LanHostEntry
    {
        public string Address;
        public int Port;
        public string Name;
    }

    /// <summary>
    /// 网络会话抽象。UI 只依赖本接口。
    /// </summary>
    public interface INetSession
    {
        NetRole Role { get; }
        NetStatus Status { get; }
        int Port { get; }
        string LastError { get; }

        event Action<NetStatus> OnStatusChanged;

        void StartHost(int port);
        void StartClient(string address, int port);
        void Disconnect();

        /// <summary>开始 LAN 广播搜索；结果通过回调返回（异步）。</summary>
        void BeginLanDiscovery(Action<LanHostEntry[]> onComplete);
    }

    /// <summary>会话定位器（UI 与业务层通过它拿到当前实现）。</summary>
    public static class NetSessionLocator
    {
        private static INetSession _current;

        public static INetSession Current => _current;

        public static void Set(INetSession session)
        {
            _current = session;
        }
    }
}
