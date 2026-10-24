// 《浮生渡》网络适配层 · FishNet 会话实现
// @trace ADR-001 §3.1（选型 FishNet）、§4.1（L0 客户端 / L2 世界服务器分层）
// 本程序集是**唯一**允许出现 FishNet 类型的地方（ADR-001 N-8）。
// 本期为 LAN 主机 / 加入形态：主机即权威端（服务器权威，客户端零权威）。
using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Component.Spawning;
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
                AlignPlayerSpawnPoint();
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

        /// <summary>
        /// 联机自证日志：客户端连上后延迟取一次「连接 + 本地玩家 + 世界」快照。
        /// 此前日志只有「已请求创建主机 / 已请求加入」，无法证明真的连上了，
        /// 双端实跑拿不到任何可断言的证据 —— 这是本次补的核心观测点。
        /// </summary>
        private System.Collections.IEnumerator LogNetState()
        {
            // 等 PlayerSpawner 联网生成玩家、CombatWorld 完成注册
            yield return new WaitForSeconds(2.5f);

            NetworkManager nm = NetworkManager;
            int clientCount = (nm != null && nm.ServerManager != null && nm.IsServerStarted)
                ? nm.ServerManager.Clients.Count : -1;
            PlayerActor local = LocalPlayerLocator.Current;
            CombatWorld world = CombatWorld.Instance;

            Debug.Log("[自检] 联机状态：角色=" + _role
                + " 状态=" + _status
                + " 端口=" + Port
                + " 服务端=" + (nm != null && nm.IsServerStarted ? "已启动" : "未启动")
                + " 客户端=" + (nm != null && nm.IsClientStarted ? "已启动" : "未启动")
                + " 连接数=" + clientCount
                + " 本地玩家=" + (local != null ? local.name : "null")
                + " ActorId=" + (local != null ? local.ActorId.ToString() : "n/a")
                + " 世界Actor数=" + (world != null ? world.Actors.Count.ToString() : "n/a"));

            // 关键告警 ①：纯客户端若 ActorId 仍为 0，说明 Id 链路没同步过来。
            // 后果：ServerCast/ServerInteract/ServerLootChoice/ServerEquip/ServerRespawn 五个
            // ServerRpc 全以 Entity.ActorId 为主键，0 会让服务端 world.Get(0) 返回 null，
            // 客户端等于「连得上、但放不出技能 / 捡不了东西 / 复活不了」。
            if (local != null && local.ActorId == 0 && _role != NetRole.Host)
                Debug.LogError("[自检] 联机告警：客户端本地玩家 ActorId=0 —— Id 链路未同步，"
                    + "按 Id 过滤的逻辑（施法 / 交互 / 掉落 / 穿戴 / 复活）在客户端会全部失效。");

            // 关键告警 ②：客户端世界为空 —— 说明同步过来的单位没进注册表，UI 枚举不到任何东西。
            if (world != null && world.Actors.Count == 0 && _role != NetRole.Host)
                Debug.LogError("[自检] 联机告警：客户端世界 Actor 数为 0 —— 单位未注册进本地 CombatWorld，"
                    + "血条 / 目标 / 飘字等按 world 枚举的表现会全部空白。");

            if (_role == NetRole.Host && world != null && world.Actors.Count == 0)
                Debug.LogError("[自检] 联机告警：主机世界 Actor 数为 0 —— 服务端没有生成任何单位。");

            // -fdNetProbe：让客户端发一次 RPC 探针，由服务端用自己的 world 验证这个 Id 能否命中。
            // 这是 Id 链路的端到端证据（服务端 → 客户端回执也一并验证）。
            if (HasArg("-fdNetProbe") && NetActorSync.LocalPlayer != null)
            {
                yield return new WaitForSeconds(1.0f);
                Debug.Log("[自检] 联机探针：客户端发起 RPC 探针（marker=7）");
                NetActorSync.LocalPlayer.ServerProbe(7);
            }
        }

        private static bool HasArg(string flag)
        {
            string[] args;
            try { args = System.Environment.GetCommandLineArgs(); }
            catch { return false; }
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == flag) return true;
            return false;
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

            // 兜底预制体：WorldArt 在运行期生成的那批刷怪点（村庄旁 5 只狼）没有 MonsterPrefab 引用，
            // 而这个字段为空时下面会整点跳过 —— 离线路径不检查该字段，所以只有主机路径受害，
            // 实测表现为「新手村旁一只怪都没有，最近的在 40m 外」（离线 20 只 / 主机 15 只）。
            // 从场景里已配好 MonsterPrefab 的点借一个；NetworkManager 要求预制体已注册，
            // 能借到就说明它本来就可生成。
            GameObject fallbackPrefab = null;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] != null && points[i].MonsterPrefab != null)
                {
                    fallbackPrefab = points[i].MonsterPrefab;
                    break;
                }
            }

            int total = 0;
            for (int i = 0; i < points.Length; i++)
            {
                MonsterSpawnPoint sp = points[i];
                if (sp == null) continue;
                GameObject prefab = sp.MonsterPrefab != null ? sp.MonsterPrefab : fallbackPrefab;
                if (prefab == null) continue;
                MonsterDef def = MonsterTable.Default.Get(sp.MonsterDefId);
                if (def == null) continue;

                for (int k = 0; k < sp.Count; k++)
                {
                    Vector3 at = sp.transform.position;
                    if (sp.ScatterRadius > 0f)
                        at += new Vector3(UnityEngine.Random.Range(-sp.ScatterRadius, sp.ScatterRadius), 0f,
                                          UnityEngine.Random.Range(-sp.ScatterRadius, sp.ScatterRadius));
                    GameObject instance = Instantiate(prefab, at, Quaternion.identity);
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

        /// <summary>
        /// 把 FishNet 的玩家出生点对齐到 RespawnPointMarker —— 也就是离线单机用的同一个出生点。
        /// 场景里 PlayerSpawner.Spawns[0] 是 (0,1.2,-10)，而 WorldArt.ApplySpawnPoint 把
        /// RespawnPointMarker 挪到了广场北路口 (0,y+1.6,-7)。不对齐的话，
        /// 主机模式出生在 -10、离线出生在 -7：同一个游戏两个出生点，取景也不对。
        /// </summary>
        private void AlignPlayerSpawnPoint()
        {
            // 注意：这版 FishNet 的 PlayerSpawner 不是 NetworkManager 的属性，是场景里的独立组件。
            PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();
            if (spawner == null || spawner.Spawns == null || spawner.Spawns.Length == 0) return;

            RespawnPointMarker marker = FindObjectOfType<RespawnPointMarker>();
            if (marker == null) return;

            spawner.Spawns[0].position = marker.transform.position;
            Debug.Log("[浮生渡] 主机模式：玩家出生点对齐到 RespawnPointMarker "
                      + marker.transform.position.ToString("F1"));
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _lastError = "";
                if (_role != NetRole.Host) _role = NetRole.Client;
                SetStatus(NetStatus.Connected);
                // 联机自证：延迟取一次快照，证明「真的连上了」而不只是「已请求加入」
                StartCoroutine(LogNetState());
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
