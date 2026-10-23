// 《浮生渡》领域层 · 局域网发现报文编解码（纯 C#，可被单测直接覆盖）
// @trace 缺陷 N-3：主机端必须能在 UDP 47770 上应答广播，否则「搜索局域网主机」恒返回 0 个
// @trace ADR-001 N-8：报文格式属于「业务协议」，放在领域层；Socket 收发留在网络适配层
// 设计约束：报文必须自描述（含协议版本 + 游戏端口 + 主机 IP + 房间名），
// 且解析必须对畸形报文免疫（UDP 不可靠，会收到任意来源的脏数据）。
namespace Fushengdu.Core
{
    /// <summary>一条主机广播的内容。</summary>
    public struct LanBeaconInfo
    {
        public string Version;  // 协议版本，用于拒绝不同版本的客户端/主机
        public int Port;        // 游戏端口（默认 7770）
        public string Name;     // 房间 / 主机名
        public string Address;  // 主机 IP（字面量，便于客户端跨网卡场景直接取用）

        public bool IsValid => !string.IsNullOrEmpty(Version) && Port > 0 && Port < 65536;

        /// <summary>去重键：同 IP 同端口只保留一条（UDP 会重复应答）。</summary>
        public string DedupeKey => Address + ":" + Port;
    }

    public static class LanBeacon
    {
        /// <summary>协议版本。协议字段变更时递增，避免版本错配导致连不上却无提示。</summary>
        public const string ProtocolVersion = "v1";

        /// <summary>客户端查询令牌。</summary>
        public const string QueryToken = "FSD_DISCOVER";

        /// <summary>主机应答前缀。</summary>
        public const string ReplyPrefix = "FSD_HOST";

        /// <summary>发现端口（与游戏端口分离）。客户端收发绑定的端口见 LanDiscovery。</summary>
        public const int DiscoveryPort = 47770;

        /// <summary>默认游戏端口。</summary>
        public const int DefaultGamePort = 7770;

        /// <summary>客户端搜索的超时时间（秒）。超时后返回已收集到的主机。</summary>
        public const float ScanTimeoutSeconds = 3.0f;

        public static string BuildQuery()
        {
            return QueryToken;
        }

        public static bool IsQuery(string text)
        {
            return text == QueryToken;
        }

        /// <summary>构造主机应答报文：`FSD_HOST|版本|端口|房间名|主机IP`</summary>
        public static string BuildReply(int port, string name, string address)
        {
            string safeName = string.IsNullOrEmpty(name) ? "浮生渡" : name.Replace("|", " ").Replace("\n", " ");
            string safeAddress = string.IsNullOrEmpty(address) ? "" : address;
            return ReplyPrefix + "|" + ProtocolVersion + "|" + port + "|" + safeName + "|" + safeAddress;
        }

        /// <summary>
        /// 解析主机应答。任何字段缺失 / 端口非法 / 版本不匹配 / 前缀不符，一律返回 false。
        /// 房间名允许为空（旧版本兼容），但版本与端口必须合法。
        /// </summary>
        public static bool TryParseReply(string text, out LanBeaconInfo info)
        {
            info = default(LanBeaconInfo);
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split('|');
            if (parts.Length < 4) return false;
            if (parts[0] != ReplyPrefix) return false;

            string version = parts[1];
            if (version != ProtocolVersion) return false;

            int port;
            if (!int.TryParse(parts[2], out port)) return false;
            if (port <= 0 || port > 65535) return false;

            info.Version = version;
            info.Port = port;
            info.Name = parts.Length > 3 ? parts[3] : "浮生渡";
            // 地址可选：UDP 报文里即使没写，也可由远端端点补上
            info.Address = parts.Length > 4 ? parts[4] : "";
            return true;
        }
    }
}
