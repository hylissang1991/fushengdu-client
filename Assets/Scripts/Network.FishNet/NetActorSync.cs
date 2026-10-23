// 《浮生渡》网络适配层 · 单位状态同步
// @trace ADR-001 N-5（每个同步字段必须声明：可靠性 / 发送间隔 / 接收方）
// 同步字段清单（本文件）：
//   _hp / _hpMax / _resource / _resourceMax / _level / _classId / _dead / _targetActorId / _displayName
//   —— 均为「服务端写、全体观察者读」，按 10 Hz 节流下发；HP/资源为高频，其余为低频。
// @trace ADR-001 N-2：客户端不写任何真相字段，只通过 ServerRpc 提交意图。
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Fushengdu.Network.FishNet
{
    using Abstraction;
    using Fushengdu.Core;
    using Fushengdu.Gameplay;

    /// <summary>
    /// 挂在玩家 / 怪物预制体上的同步组件。是 FishNet 类型与业务层之间的唯一桥。
    /// </summary>
    public sealed class NetActorSync : NetworkBehaviour
    {
        public ActorEntity Entity;

        private readonly SyncVar<float> _hp = new SyncVar<float>();
        private readonly SyncVar<float> _hpMax = new SyncVar<float>();
        private readonly SyncVar<float> _resource = new SyncVar<float>();
        private readonly SyncVar<float> _resourceMax = new SyncVar<float>();
        private readonly SyncVar<int> _level = new SyncVar<int>();
        private readonly SyncVar<int> _classId = new SyncVar<int>();
        private readonly SyncVar<bool> _dead = new SyncVar<bool>();
        private readonly SyncVar<int> _targetActorId = new SyncVar<int>();
        private readonly SyncVar<string> _displayName = new SyncVar<string>();
        // 怪物定义 Id：服务端在 OnStartServer 写入，客户端据此拼装外观（场景外生成的预制体实例
        // 不会携带运行时设置的 MonsterDefId，必须靠同步变量下发，否则客户端看不到模型）。
        private readonly SyncVar<int> _monsterDefId = new SyncVar<int>();

        /// <summary>NetworkObject.ObjectId → 同步组件（服务端解析目标用）。</summary>
        public static readonly Dictionary<int, NetActorSync> Registry = new Dictionary<int, NetActorSync>();

        /// <summary>本地玩家自己的同步组件（输入层提交意图用）。</summary>
        public static NetActorSync LocalPlayer { get; private set; }

        private float _syncTimer;
        private const float SyncInterval = 0.1f; // 10 Hz

        public override void OnStartNetwork()
        {
            Registry[base.ObjectId] = this;
        }

        public override void OnStopNetwork()
        {
            Registry.Remove(base.ObjectId);
            if (LocalPlayer == this) LocalPlayer = null;
            CombatWorld world = CombatWorld.Instance;
            if (world != null && Entity != null) world.Unregister(Entity);
        }

        public override void OnStartServer()
        {
            if (Entity == null) Entity = GetComponent<ActorEntity>();
            if (Entity == null) return;

            // 服务端为权威 Id
            Entity.State.Id = base.ObjectId;

            CombatWorld world = CombatWorld.Instance;
            if (world != null) world.Register(Entity);

            var monster = Entity as MonsterActor;
            if (monster != null && monster.Definition == null)
            {
                MonsterDef def = MonsterTable.Default.Get(monster.MonsterDefId);
                if (def != null) monster.Initialize(def, base.ObjectId);
            }
            // 同步怪物定义 Id 给客户端（用于拼装外观）
            if (monster != null) _monsterDefId.Value = monster.MonsterDefId;

            PushAll();
        }

        public override void OnStartClient()
        {
            if (Entity == null) Entity = GetComponent<ActorEntity>();
            if (Entity == null) return;

            // 客户端监听怪物定义 Id 同步，用于拼装外观（场景外生成的预制体实例不携带运行时 MonsterDefId）
            if (Entity is MonsterActor)
                _monsterDefId.OnChange += OnMonsterDefIdChanged;

            var player = Entity as PlayerActor;
            if (player != null)
            {
                player.Authority = new FishNetAuthority(this);
                if (base.IsOwner)
                {
                    LocalPlayer = this;
                    // 让 UI 能找到本机玩家（UI 程序集不引用 FishNet）
                    LocalPlayerLocator.Set(player);
                    // 提交角色配置（服务端权威应用，客户端不自行决定数值）
                    ServerSetup((int)NetLaunchSettings.ClassId, NetLaunchSettings.Female ? 1 : 0,
                        NetLaunchSettings.PlayerName, NetLaunchSettings.StartLevel);
                }
            }

            // 任一同步字段变化都回写本地镜像状态（死亡/等级/资源的 UI 表现依赖它）
            _hp.OnChange += (prev, next, asServer) => { if (!asServer) ApplyToState(); };
            _hpMax.OnChange += (prev, next, asServer) => { if (!asServer) ApplyToState(); };
            _resource.OnChange += (prev, next, asServer) => { if (!asServer) ApplyToState(); };
            _level.OnChange += (prev, next, asServer) => { if (!asServer) ApplyToState(); };
            _dead.OnChange += OnDeadChanged;
            ApplyToState();
        }

        private void OnDeadChanged(bool prev, bool next, bool asServer)
        {
            if (asServer) return;
            ApplyToState();
            // 客户端同步死亡表现：清除目标选择，避免死亡后仍锁着目标
            var player = Entity as PlayerActor;
            if (player != null && next) player.SetTarget(null);
        }

        /// <summary>客户端收到怪物定义 Id：据此拼装可见模型（服务端已 Initialize，客户端只补外观）。</summary>
        private void OnMonsterDefIdChanged(int prev, int next, bool asServer)
        {
            if (asServer) return;
            var monster = Entity as MonsterActor;
            if (monster != null && next > 0 && monster.MonsterDefId == 0)
            {
                monster.MonsterDefId = next;
                Core.MonsterDef def = Core.MonsterTable.Default.Get(next);
                if (def != null) monster.EnsureVisual(def);
            }
        }

        private void Update()
        {
            if (!base.IsServerInitialized) return;
            _syncTimer += Time.deltaTime;
            if (_syncTimer < SyncInterval) return;
            _syncTimer = 0f;
            PushAll();
        }

        private void PushAll()
        {
            if (Entity == null || Entity.State == null) return;
            ActorState s = Entity.State;
            _hp.Value = s.Hp;
            _hpMax.Value = s.HpMax;
            _resource.Value = s.Resource;
            _resourceMax.Value = s.ResourceMax;
            _level.Value = s.Level;
            _classId.Value = (int)s.Class;
            _dead.Value = s.Dead;
            _displayName.Value = s.Name;

            var player = Entity as PlayerActor;
            _targetActorId.Value = player != null && player.Target != null ? player.Target.ActorId : 0;
        }

        private void ApplyToState()
        {
            if (Entity == null || Entity.State == null) return;
            ActorState s = Entity.State;
            if (_hpMax.Value > 0f) s.HpMax = _hpMax.Value;
            s.Hp = _hp.Value;
            s.ResourceMax = _resourceMax.Value;
            s.Resource = _resource.Value;
            s.Level = _level.Value;
            s.Class = (ClassId)_classId.Value;
            s.Dead = _dead.Value;
            if (!string.IsNullOrEmpty(_displayName.Value)) s.Name = _displayName.Value;
        }

        // ------------------------------------------------------ 客户端 → 服务端

        /// <summary>提交角色配置（仅 owner 调用）。</summary>
        [ServerRpc]
        private void ServerSetup(int classId, int female, string name, int startLevel)
        {
            var player = Entity as PlayerActor;
            if (player == null) return;
            player.Setup((ClassId)classId, female != 0, name, startLevel);
            CombatWorld world = CombatWorld.Instance;
            if (world != null) world.Register(player);
            PushAll();
            // 回推完整状态，保证客户端拿到的是服务端应用后的值
            ObserversApplySetup(classId, female, name, startLevel);
        }

        [ObserversRpc]
        private void ObserversApplySetup(int classId, int female, string name, int startLevel)
        {
            if (base.IsServerInitialized) return;
            var player = Entity as PlayerActor;
            if (player == null) return;
            if (player.State.Class != (ClassId)classId)
                player.Setup((ClassId)classId, female != 0, name, startLevel);
            ApplyToState();
        }

        /// <summary>技能请求：客户端只发「意图」（ADR-001 N-2/N-3）。</summary>
        [ServerRpc]
        private void ServerCast(int skillId, int targetObjectId)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null || Entity == null) return;
            int targetActorId = 0;
            if (targetObjectId != 0)
            {
                NetActorSync targetSync;
                if (Registry.TryGetValue(targetObjectId, out targetSync) && targetSync.Entity != null)
                    targetActorId = targetSync.Entity.ActorId;
            }
            world.Cast(Entity.ActorId, (SkillId)skillId, targetActorId);
        }

        [ServerRpc]
        private void ServerInteract(int npcId)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null || Entity == null) return;
            NetWorldBridge bridge = NetWorldBridge.Instance;
            if (bridge != null) bridge.HandleInteractOnServer(Entity.ActorId, npcId);
        }

        [ServerRpc]
        private void ServerLootChoice(int sessionId, int choice)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null || Entity == null) return;
            world.SubmitLootChoice(Entity.ActorId, sessionId, (LootChoice)choice);
        }

        /// <summary>穿戴请求（缺陷 N-2 · 服务端权威）。</summary>
        [ServerRpc]
        private void ServerEquip(int itemUid)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null || Entity == null) return;
            world.RequestEquip(Entity.ActorId, itemUid);
        }

        /// <summary>立即复活请求（缺陷 N-1 · 服务端权威）。</summary>
        [ServerRpc]
        private void ServerRespawn()
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null || Entity == null) return;
            world.RequestRespawn(Entity.ActorId);
        }

        // ------------------------------------------------------ 对外（供 Authority）
        public void SendCast(SkillId skill, ActorEntity target)
        {
            int targetObjectId = 0;
            if (target != null)
            {
                var sync = target.GetComponent<NetActorSync>();
                if (sync != null) targetObjectId = sync.ObjectId;
            }
            ServerCast((int)skill, targetObjectId);
        }

        public void SendInteract(int npcId)
        {
            ServerInteract(npcId);
        }

        public void SendLootChoice(int sessionId, LootChoice choice)
        {
            ServerLootChoice(sessionId, (int)choice);
        }

        public void SendEquip(int itemUid)
        {
            ServerEquip(itemUid);
        }

        public void SendRespawn()
        {
            ServerRespawn();
        }
    }

    /// <summary>
    /// FishNet 权威端实现：把业务层的「意图」转成 ServerRpc。
    /// </summary>
    public sealed class FishNetAuthority : IWorldAuthority
    {
        private readonly NetActorSync _sync;

        public FishNetAuthority(NetActorSync sync)
        {
            _sync = sync;
        }

        public void RequestCast(int casterId, SkillId skill, int targetId)
        {
            CombatWorld world = CombatWorld.Instance;
            ActorEntity target = targetId != 0 && world != null ? world.Get(targetId) : null;
            _sync.SendCast(skill, target);
        }

        public void RequestInteract(int playerId, int npcId)
        {
            _sync.SendInteract(npcId);
        }

        public void RequestPickup(int playerId, int itemUid)
        {
            // 本期掉落由服务端直接发放（D-19 掷骰流程覆盖组队场景）
        }

        public void RequestLootChoice(int playerId, int sessionId, LootChoice choice)
        {
            _sync.SendLootChoice(sessionId, choice);
        }

        public void RequestEquip(int playerId, int itemUid)
        {
            _sync.SendEquip(itemUid);
        }

        public void RequestRespawn(int playerId)
        {
            _sync.SendRespawn();
        }
    }
}
