// 《浮生渡》网络适配层 · FishNet 会话实现
// @trace ADR-001 §3.1（选型 FishNet）、§4.1（L0 客户端 / L2 世界服务器分层）
// 本程序集是**唯一**允许出现 FishNet 类型的地方（ADR-001 N-8）。
// 本期为 LAN 主机 / 加入形态：主机即权威端（服务器权威，客户端零权威）。
using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace Fushengdu.Network.FishNet
{
    using Abstraction;
    using Fushengdu.Core;
    using Fushengdu.Gameplay;

    /// <summary>
    /// FishNet 会话：主机 / 加入 / 断开 / LAN 发现。
    /// </summary>
    public sealed class FishNetSession : MonoBehaviour, INetSession
    {
        [Header("FishNet 装配")]
        public NetworkManager NetworkManager;

        [Header("默认端口")]
        public int DefaultPort = 7770;

        /// <summary>世界事件桥接预制体（由编辑器构建脚本注入，已注册进可生成集合）。服务端就绪后生成。</summary>
        public GameObject WorldBridgePrefab;

        private NetRole _role = NetRole.Offline;
        private NetStatus _status = NetStatus.Disconnected;
        private string _lastError = "";
        private LanDiscovery _discovery;

        public NetRole Role => _role;
        public NetStatus Status => _status;
        public int Port { get; private set; } = 7770;
        public string LastError => _lastError;

        public event Action<NetStatus> OnStatusChanged;

        public static FishNetSession Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            Port = DefaultPort;
            if (NetworkManager == null) NetworkManager = GetComponent<NetworkManager>();
            if (NetworkManager == null) NetworkManager = InstanceFinder.NetworkManager;
            NetSessionLocator.Set(this);
        }

        private void OnEnable()
        {
            if (NetworkManager == null) return;
            NetworkManager.ServerManager.OnServerConnectionState += OnServerState;
            NetworkManager.ClientManager.OnClientConnectionState += OnClientState;
        }

        private void OnDisable()
        {
            if (NetworkManager == null) return;
            NetworkManager.ServerManager.OnServerConnectionState -= OnServerState;
            NetworkManager.ClientManager.OnClientConnectionState -= OnClientState;
        }

        private void SetStatus(NetStatus status)
        {
            if (_status == status) return;
            _status = status;
            Action<NetStatus> handler = OnStatusChanged;
            if (handler != null) handler(status);
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _lastError = "";
                if (_role != NetRole.Host) _role = NetRole.Server;
                SetStatus(NetStatus.Connected);
                // 缺陷 N-3：服务器真正起来后才开始应答 LAN 广播，否则「搜索局域网主机」恒为 0
                StartLanAdvertise();
                // 服务端就绪后生成世界事件桥接（替代场景内 NetworkBridge，避免 65535 报错）
                SpawnWorldBridge();
                // 服务端就绪后按刷怪点标记生成怪物（场景内不再直接摆放怪物 NetworkObject）
                SpawnWorldMonsters();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                StopLanAdvertise();
                // 服务器与客户端都停止才算断开
                if (!NetworkManager.IsClientStarted)
                {
                    _role = NetRole.Offline;
                    SetStatus(NetStatus.Disconnected);
                }
            }
        }

        /// <summary>服务器就绪后生成世界事件桥接对象（替代原先直接摆进场景的 NetworkBridge，避免 65535 报错）。</summary>
        private void SpawnWorldBridge()
        {
            if (WorldBridgePrefab == null || NetworkManager == null) return;
            GameObject go = Instantiate(WorldBridgePrefab);
            NetworkManager.ServerManager.Spawn(go);
        }

        /// <summary>服务器就绪后按场景内 MonsterSpawnPoint 标记生成怪物（联机/主机模式）。</summary>
        private void SpawnWorldMonsters()
        {
            if (NetworkManager == null) return;
            MonsterSpawnPoint[] points = FindObjectsOfType<MonsterSpawnPoint>();
            int total = 0;
            for (int i = 0; i < points.Length; i++)
            {
                MonsterSpawnPoint sp = points[i];
                if (sp == null || sp.MonsterPrefab == null) continue;
                MonsterDef def = MonsterTable.Default.Get(sp.MonsterDefId);
                if (def == null) continue;

                for (int k = 0; k < sp.Count; k++)
                {
                    Vector3 at = sp.transform.position;
                    if (sp.ScatterRadius > 0f)
                        at += new Vector3(UnityEngine.Random.Range(-sp.ScatterRadius, sp.ScatterRadius), 0f,
                                          UnityEngine.Random.Range(-sp.ScatterRadius, sp.ScatterRadius));
                    GameObject instance = Instantiate(sp.MonsterPrefab, at, Quaternion.identity);
                    MonsterActor actor = instance.GetComponent<MonsterActor>();
                    // 服务端设置 MonsterDefId，NetActorSync.OnStartServer 会据此初始化并同步到客户端
                    if (actor != null) actor.MonsterDefId = sp.MonsterDefId;
                    NetworkObject nob = instance.GetComponent<NetworkObject>();
                    if (nob != null) nob.SetIsSpawnable(true);
                    NetworkManager.ServerManager.Spawn(instance);
                    total++;
                }
            }
            if (total > 0) Debug.Log("[浮生渡] 主机模式：服务端生成怪物 " + total + " 只（来自刷怪点）");
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _lastError = "";
                if (_role != NetRole.Host) _role = NetRole.Client;
                SetStatus(NetStatus.Connected);
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (!NetworkManager.IsServerStarted)
                {
                    _role = NetRole.Offline;
                    SetStatus(NetStatus.Disconnected);
                }
            }
        }

        public void StartHost(int port)
        {
            if (NetworkManager == null)
            {
                _lastError = "场景中缺少 FishNet NetworkManager";
                SetStatus(NetStatus.Failed);
                return;
            }
            Port = port;
            _role = NetRole.Host;
            SetStatus(NetStatus.Starting);
            bool serverOk = NetworkManager.ServerManager.StartConnection((ushort)port);
            bool clientOk = NetworkManager.ClientManager.StartConnection("localhost", (ushort)port);
            if (!serverOk || !clientOk)
            {
                _lastError = "启动主机失败（端口可能被占用）";
                SetStatus(NetStatus.Failed);
                StopLanAdvertise();
                return;
            }
            // 缺陷 N-3：显式开启 LAN 广播应答（OnServerState 也会开一次，BeginAdvertise 幂等）
            StartLanAdvertise();
        }

        public void StartClient(string address, int port)
        {
            if (NetworkManager == null)
            {
                _lastError = "场景中缺少 FishNet NetworkManager";
                SetStatus(NetStatus.Failed);
                return;
            }
            Port = port;
            _role = NetRole.Client;
            SetStatus(NetStatus.Starting);
            if (!NetworkManager.ClientManager.StartConnection(address, (ushort)port))
            {
                _lastError = "连接失败：" + address + ":" + port;
                SetStatus(NetStatus.Failed);
            }
        }

        public void Disconnect()
        {
            StopLanAdvertise(); // 缺陷 N-3：断开时释放 UDP 47770
            if (NetworkManager == null) return;
            NetworkManager.ClientManager.StopConnection();
            NetworkManager.ServerManager.StopConnection(true);
            _role = NetRole.Offline;
            SetStatus(NetStatus.Disconnected);
        }

        public void BeginLanDiscovery(Action<LanHostEntry[]> onComplete)
        {
            if (_discovery == null) _discovery = gameObject.AddComponent<LanDiscovery>();
            _discovery.BeginScan(onComplete);
        }

        /// <summary>
        /// 主机端开启 LAN 广播应答（StartHost / 服务器 Started 后调用）。
        /// 房间名 = 设备名 + 职业无关的房间标识，便于朋友在列表里认出是哪台机器。
        /// </summary>
        public void StartLanAdvertise()
        {
            if (_discovery == null) _discovery = gameObject.AddComponent<LanDiscovery>();
            // 房间名：命令行 -fdRoom 指定优先，否则回退「浮生渡-设备名」
            string room = NetLaunchSettings.RoomName;
            if (string.IsNullOrEmpty(room)) room = "浮生渡-" + SystemInfo.deviceName;
            _discovery.BeginAdvertise(Port, room);
        }

        public void StopLanAdvertise()
        {
            if (_discovery != null) _discovery.StopAll();
        }

        /// <summary>当前是否运行在权威端（服务器或主机）。</summary>
        public static bool IsAuthority()
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            return nm == null || nm.IsServerStarted; // 无 NetworkManager 时视为离线权威端
        }
    }
}
