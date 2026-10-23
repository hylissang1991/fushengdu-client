// 《浮生渡》表现层 · 离线（单机）权威端实现
// @trace ADR-001 N-8：业务层只依赖 IWorldAuthority，框架类型不进入本文件。
// 离线模式下本机即权威端；LAN 联机时由 Fushengdu.Network.FishNet 提供同接口的另一实现。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 离线权威端：直接驱动 CombatWorld，无任何网络往返。
    /// </summary>
    public sealed class OfflineAuthority : IWorldAuthority
    {
        public void RequestCast(int casterId, Core.SkillId skill, int targetId)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            Core.CastFailReason fail = world.Cast(casterId, skill, targetId);
            if (fail != Core.CastFailReason.None)
                Debug.Log("[战斗] 施法被拒：" + fail);
        }

        public void RequestInteract(int playerId, int npcId)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            ActorEntity actor = world.Get(playerId);
            var player = actor as PlayerActor;
            if (player == null) return;

            NpcEntity npc = NpcEntity.Find(npcId);
            if (npc == null) return;

            // 对话型任务推进
            player.Quests.OnTalk(npcId);

            // 交付已完成的任务
            var toTurnIn = new List<int>();
            foreach (Core.QuestProgress p in player.Quests.Active)
                if (player.Quests.CanTurnIn(p.QuestId)) toTurnIn.Add(p.QuestId);

            int questSilver = 0;
            for (int i = 0; i < toTurnIn.Count; i++)
            {
                int questId = toTurnIn[i];
                Core.QuestDef def = Core.QuestTable.Default.Get(questId);
                int next = player.Quests.TurnIn(questId);
                if (def == null) continue;

                int before = player.Progression.Level;
                player.Progression.AddXp(def.XpReward);
                player.Silver += def.SilverReward;
                questSilver += def.SilverReward;
                if (def.ItemRewardDefId != 0)
                {
                    player.Inventory.Add(def.ItemRewardDefId, false);
                    player.RefreshFromEquipment();
                }
                if (player.Progression.Level != before)
                {
                    Core.Progression.ApplyLevel(player.State, player.Progression.Level);
                    player.RefreshFromEquipment();
                }
                if (next != 0) player.Quests.Accept(next);
            }
            // 任务交付渡银飘字（离线单机路径）：聚合本次交付总渡银，本地玩家视角触发
            if (questSilver > 0 && world != null)
                world.RaiseSilverGained(playerId, questSilver, player.transform.position);

            // 接取可接的新任务
            foreach (Core.QuestDef def in Core.QuestTable.Default.All())
            {
                if (player.Quests.IsAccepted(def.Id) || player.Quests.IsDone(def.Id)) continue;
                if (def.PredecessorId != 0 && !player.Quests.IsDone(def.PredecessorId)) continue;
                player.Quests.Accept(def.Id);
            }

            player.RefreshFromEquipment();
        }

        public void RequestPickup(int playerId, int itemUid)
        {
            // 离线模式下掉落已直接入包；本接口保留用于「地上拾取」扩展。
        }

        public void RequestLootChoice(int playerId, int sessionId, Core.LootChoice choice)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            world.SubmitLootChoice(playerId, sessionId, choice);
        }

        public void RequestEquip(int playerId, int itemUid)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            world.RequestEquip(playerId, itemUid);
        }

        public void RequestRespawn(int playerId)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;
            world.RequestRespawn(playerId);
        }
    }

    /// <summary>
    /// 离线会话引导：创建玩家、驱动输入、处理采集与复活。
    /// 联机时由 FishNet 侧的会话引导替换（同一套 PlayerActor / CombatWorld）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class OfflineSession : MonoBehaviour
    {
        public Core.ClassId ClassId = Core.ClassId.SwordCultivator;
        public bool Female;
        public string PlayerName = "无名渡者";
        public int StartLevel = 1;

        public PlayerActor Player { get; private set; }
        public CameraFollow CameraRig { get; private set; }

        private bool _started;

        private void Start()
        {
            Begin();
        }

        public void Begin()
        {
            if (_started) return;
            _started = true;

            CombatWorld world = FindObjectOfType<CombatWorld>();
            if (world == null)
            {
                var worldGo = new GameObject("CombatWorld");
                world = worldGo.AddComponent<CombatWorld>();
            }

            GameObject playerGo = new GameObject("Player_" + PlayerName);
            Player = playerGo.AddComponent<PlayerActor>();
            Player.Authority = new OfflineAuthority();

            CharacterData saved = SaveSystem.Load();
            if (saved != null && saved.Level >= 1)
            {
                Player.ApplySaveData(saved);
            }
            else
            {
                Player.Setup(ClassId, Female, PlayerName, StartLevel);
            }

            RespawnPointMarker marker = FindObjectOfType<RespawnPointMarker>();
            Vector3 spawn = marker != null ? marker.transform.position : new Vector3(0f, 2f, 0f);
            Player.Teleport(spawn);
            Player.SpawnPoint = spawn;
            world.RespawnPoint = spawn;

            world.Register(Player);

            // 离线单机：场景内已摆放的怪物没有 FishNet 服务器来初始化，需本地权威直接驱动。
            // （联机主机模式由 NetActorSync.OnStartServer 负责；此处只覆盖纯离线路径。）
            SpawnSceneMonsters(world);

            LocalPlayerLocator.Set(Player);

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                var camGo = new GameObject("Main Camera");
                mainCam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }
            CameraRig = mainCam.GetComponent<CameraFollow>();
            if (CameraRig == null) CameraRig = mainCam.gameObject.AddComponent<CameraFollow>();
            CameraRig.Target = Player.transform;

            var input = playerGo.GetComponent<PlayerInputController>();
            if (input == null) input = playerGo.AddComponent<PlayerInputController>();
            input.Player = Player;
        }

        /// <summary>把坐标贴到地面（向下射线打地面网格），避免怪物悬在半空或埋进土里。</summary>
        private static Vector3 GroundSnap(Vector3 at)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(at.x, at.y + 40f, at.z), Vector3.down, out hit, 90f))
                return new Vector3(at.x, hit.point.y, at.z);
            Terrain t = Terrain.activeTerrain;
            if (t != null) return new Vector3(at.x, t.SampleHeight(at), at.z);
            return at;
        }

        private void OnApplicationQuit()
        {
            if (Player != null) SaveSystem.Save(Player.ToSaveData());
        }

        /// <summary>
        /// 本地权威初始化场景内已摆放的怪物（单机模式无 FishNet 服务器，
        /// 不会触发 NetActorSync.OnStartServer，故在此直接驱动）。
        /// 联机主机模式由 NetActorSync.OnStartServer 统一初始化，不走此路径。
        /// </summary>
        private void SpawnSceneMonsters(CombatWorld world)
        {
            if (world == null) return;
            int initialized = 0;

            // 离线单机：场景内预摆放的怪物是 FishNet NetworkObject，无服务器时会被 FishNet
            // 反初始化/销毁，无法作为单机怪物使用。在此移除它们（多数情况下 FishNet 已销毁），
            // 改为按刷怪点（MonsterSpawnPoint，非 NetworkObject，可稳定存活）动态生成，
            // 保证单机一定有可战斗的怪物。修复「能打开但玩不了」。
            MonsterActor[] placed = FindObjectsOfType<MonsterActor>();
            for (int i = 0; i < placed.Length; i++)
            {
                if (placed[i] != null) Object.Destroy(placed[i].gameObject);
            }

            MonsterSpawnPoint[] points = FindObjectsOfType<MonsterSpawnPoint>();

            for (int p = 0; p < points.Length; p++)
            {
                MonsterSpawnPoint sp = points[p];
                if (sp == null) continue;
                Core.MonsterDef def = Core.MonsterTable.Default.Get(sp.MonsterDefId);
                if (def == null) continue;
                for (int k = 0; k < sp.Count; k++)
                {
                    Vector3 offset = sp.ScatterRadius > 0f
                        ? new Vector3(UnityEngine.Random.Range(-sp.ScatterRadius, sp.ScatterRadius), 0f,
                                      UnityEngine.Random.Range(-sp.ScatterRadius, sp.ScatterRadius))
                        : Vector3.zero;
                    Vector3 at = sp.transform.position + offset;

                    // 空节点即可：可见模型由 MonsterActor.Initialize → RuntimeArt 按怪物定义拼装
                    GameObject go = new GameObject("SoloMonster_" + sp.MonsterDefId + "_" + (k + 1));
                    go.transform.position = GroundSnap(at);
                    MonsterActor m = go.AddComponent<MonsterActor>();
                    m.MonsterDefId = sp.MonsterDefId;
                    m.Initialize(def, 0);   // State.Id=0 → CombatWorld.Register 分配本地 Id
                    world.Register(m);
                    initialized++;
                }
            }

            Debug.Log("[浮生渡] 离线模式：本地初始化场景怪物 " + initialized + " 只（来自刷怪点）");
        }
    }
}
