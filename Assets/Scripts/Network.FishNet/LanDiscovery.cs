// 《浮生渡》网络适配层 · 局域网发现（LAN Discovery）
// @trace 缺陷 N-3：主机端必须在 UDP 47770 上应答广播，否则「搜索局域网主机」恒返回 0 个
// 说明：FishNet 免费核心不含 LAN 发现组件（已核对其 Runtime 目录），本期自实现最小 UDP 广播。
// 本组件只负责「找到主机的 IP 与端口」，不参与任何战斗/掉落逻辑，也不构成自研网络栈（ADR-001 §2.4 否决项）。
// 报文格式与解析在 Fushengdu.Core.LanBeacon（纯 C#，可单测）；本文件只做 Socket 收发。
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Fushengdu.Network.FishNet
{
    using Abstraction;
    using Fushengdu.Core;

    /// <summary>
    /// LAN 发现：主机端应答广播，客户端发送广播并收集应答。
    /// 端口：UDP 47770（发现广播 / 主机监听）；客户端收发临时绑定 47771。
    /// </summary>
    public sealed class LanDiscovery : MonoBehaviour
    {
        private const int ServerListenPort = LanBeacon.DiscoveryPort;      // 47770
        private const int ClientListenPort = LanBeacon.DiscoveryPort + 1;  // 47771
        private const int ResendCount = 3;                                 // 广播重发次数（UDP 会丢包）
        private const float ResendInterval = 0.4f;

        private UdpClient _advertise;
        private UdpClient _scan;
        private bool _advertising;
        private bool _scanning;
        private int _advertisePort;
        private string _advertiseName = "浮生渡";
        private string _localAddress = "";

        private readonly List<LanHostEntry> _found = new List<LanHostEntry>();
        private readonly HashSet<string> _seenKeys = new HashSet<string>();
        private Action<LanHostEntry[]> _onComplete;
        private float _scanDeadline;
        private float _nextResend;
        private int _resendLeft;

        private void Update()
        {
            if (_advertising) PumpAdvertise();
            if (_scanning)
            {
                PumpScan();
                PumpResend();
                if (Time.realtimeSinceStartup >= _scanDeadline) FinishScan();
            }
        }

        private void OnDestroy()
        {
            StopAll();
        }

        public void StopAll()
        {
            _advertising = false;
            _scanning = false;
            Close(ref _advertise);
            Close(ref _scan);
        }

        private static void Close(ref UdpClient client)
        {
            if (client == null) return;
            try { client.Close(); } catch { /* 关闭失败不影响主流程 */ }
            client = null;
        }

        // ------------------------------------------------------------ 主机端
        /// <summary>
        /// 开启广播应答。幂等：已在广播时只更新端口与房间名，不重复绑定端口。
        /// </summary>
        public void BeginAdvertise(int gamePort, string hostName)
        {
            _advertisePort = gamePort;
            _advertiseName = string.IsNullOrEmpty(hostName) ? "浮生渡" : hostName;
            _localAddress = ResolveLocalAddress();

            if (_advertising && _advertise != null)
            {
                Debug.Log("[LAN] 已在广播应答（端口 " + gamePort + "），仅更新房间名");
                return;
            }

            try
            {
                _advertise = new UdpClient();
                _advertise.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _advertise.Client.Bind(new IPEndPoint(IPAddress.Any, ServerListenPort));
                _advertise.Client.Blocking = false;
                _advertising = true;
                Debug.Log("[LAN] 开始广播应答：UDP " + ServerListenPort + " → 游戏端口 " + gamePort
                          + " · 房间名 " + _advertiseName + " · 主机 IP " + _localAddress);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LAN] 广播应答启动失败（端口 " + ServerListenPort + " 可能被占用）：" + ex.Message);
                Close(ref _advertise);
                _advertising = false;
            }
        }

        private void PumpAdvertise()
        {
            if (_advertise == null) return;
            try
            {
                while (_advertise.Available > 0)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _advertise.Receive(ref remote);
                    string text = Encoding.UTF8.GetString(data);
                    if (!LanBeacon.IsQuery(text)) continue;

                    // 报文自带主机 IP；若解析失败则退回远端端点地址
                    string reply = LanBeacon.BuildReply(_advertisePort, _advertiseName, _localAddress);
                    byte[] payload = Encoding.UTF8.GetBytes(reply);
                    _advertise.Send(payload, payload.Length, remote);
                }
            }
            catch (SocketException)
            {
                // 非阻塞接收在无数据时抛 WSAEWOULDBLOCK，忽略
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LAN] 应答异常：" + ex.Message);
            }
        }

        /// <summary>取本机局域网 IPv4 地址（排除回环）。取不到时返回空串，由远端端点兜底。</summary>
        private static string ResolveLocalAddress()
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ip)) continue;
                    return ip.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LAN] 本机 IP 解析失败：" + ex.Message);
            }
            return "";
        }

        // ------------------------------------------------------------ 客户端
        public void BeginScan(Action<LanHostEntry[]> onComplete)
        {
            StopAll();
            _found.Clear();
            _seenKeys.Clear();
            _onComplete = onComplete;

            try
            {
                _scan = new UdpClient();
                _scan.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _scan.Client.Bind(new IPEndPoint(IPAddress.Any, ClientListenPort));
                _scan.Client.Blocking = false;
                _scan.EnableBroadcast = true;

                _scanDeadline = Time.realtimeSinceStartup + LanBeacon.ScanTimeoutSeconds;
                _nextResend = 0f;
                _resendLeft = ResendCount;
                _scanning = true;
                Debug.Log("[LAN] 开始搜索主机（超时 " + LanBeacon.ScanTimeoutSeconds + "s，UDP "
                          + ServerListenPort + " 广播 / 本机监听 " + ClientListenPort + "）");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LAN] 搜索启动失败：" + ex.Message);
                Close(ref _scan);
                if (onComplete != null) onComplete(new LanHostEntry[0]);
            }
        }

        /// <summary>UDP 广播会丢包，因此按固定间隔重发若干次查询。</summary>
        private void PumpResend()
        {
            if (_resendLeft <= 0 || _scan == null) return;
            if (Time.realtimeSinceStartup < _nextResend) return;

            byte[] payload = Encoding.UTF8.GetBytes(LanBeacon.BuildQuery());
            IPEndPoint broadcast = new IPEndPoint(IPAddress.Broadcast, ServerListenPort);
            _scan.Send(payload, payload.Length, broadcast);
            _nextResend = Time.realtimeSinceStartup + ResendInterval;
            _resendLeft--;
        }

        private void PumpScan()
        {
            if (_scan == null) return;
            try
            {
                while (_scan.Available > 0)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _scan.Receive(ref remote);
                    string text = Encoding.UTF8.GetString(data);

                    LanBeaconInfo info;
                    if (!LanBeacon.TryParseReply(text, out info)) continue;

                    // 报文未带 IP 时用远端端点补上
                    string address = string.IsNullOrEmpty(info.Address) ? remote.Address.ToString() : info.Address;

                    // 去重：同一 IP+端口 只保留一条（UDP 会重复应答）
                    string key = address + ":" + info.Port;
                    if (_seenKeys.Contains(key)) continue;
                    _seenKeys.Add(key);

                    _found.Add(new LanHostEntry { Address = address, Port = info.Port, Name = info.Name });
                }
            }
            catch (SocketException)
            {
                // 忽略非阻塞空读
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LAN] 搜索异常：" + ex.Message);
            }
        }

        private void FinishScan()
        {
            _scanning = false;
            Close(ref _scan);
            Debug.Log("[LAN] 搜索结束，找到 " + _found.Count + " 个主机");
            Action<LanHostEntry[]> cb = _onComplete;
            _onComplete = null;
            if (cb != null) cb(_found.ToArray());
        }
    }
}
