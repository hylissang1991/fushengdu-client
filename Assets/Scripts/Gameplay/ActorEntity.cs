// 《浮生渡》表现层 · 战斗单位实体
// @trace ADR-001 N-1 / N-7：本层是 MonoBehaviour 外壳，真相状态在 Fushengdu.Core.ActorState。
// 本层不得持有任何网络框架类型（FishNet 类型只允许出现在网络适配层 —— ADR-001 N-8）。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 场景中的一个可战斗单位。持有领域层状态 + 表现所需的位置/朝向/模型。
    /// </summary>
    [DisallowMultipleComponent]
    public class ActorEntity : MonoBehaviour
    {
        [Header("领域状态（服务器真相）")]
        public Core.ActorState State;

        [Header("怪物专用（玩家为 null）")]
        public Core.MonsterDef Definition;
        public Core.MonsterBrain Brain;
        public Core.ThreatTable Threat;

        [Header("表现")]
        public float BaseMoveSpeed = 4.5f;
        public Vector3 SpawnPoint;
        public float DeathDespawnDelay = 6f;

        /// <summary>世界内唯一 Id（与 State.Id 一致）。</summary>
        public int ActorId => State != null ? State.Id : 0;

        public string DisplayName => State != null ? State.Name : "单位";

        public bool IsPlayer => State != null && State.Faction == Core.ActorFaction.Player;

        public bool IsDead => State != null && State.Dead;

        /// <summary>状态变化事件（UI 与网络同步层订阅）。</summary>
        public System.Action<ActorEntity> OnStateChanged;

        private float _deathTimer;

        protected virtual void Awake()
        {
            if (State == null) State = new Core.ActorState();
        }

        /// <summary>由服务器权威推进（客户端只读）。</summary>
        public virtual void TickAuthority(float dt)
        {
            if (State == null) return;
            State.Tick(dt);

            if (State.Dead)
            {
                _deathTimer += dt;
                return;
            }
            _deathTimer = 0f;
        }

        /// <summary>
        /// 复活（新手区：回村复活点）。数值口径统一走 Core.RespawnRules，
        /// 便于试玩用例直接引用期望值比对（缺陷 N-1）。
        /// </summary>
        public virtual void Respawn(Vector3 at)
        {
            if (State == null) return;
            Core.RespawnRules.ApplyRespawn(State);
            transform.position = at;
            _deathTimer = 0f;
        }

        public float DeathTimer => _deathTimer;

        /// <summary>当前移速（含 buff 与减速）。</summary>
        public float CurrentMoveSpeed()
        {
            if (State == null) return 0f;
            if (State.Control == Core.ControlKind.Stun || State.Control == Core.ControlKind.Fear) return 0f;
            float slow = State.Buffs.SlowPct();
            float speed = BaseMoveSpeed * (1f + State.Buffs.MoveSpeedBonusPct()) * (1f - slow);
            if (Definition != null && !IsPlayer) speed = Definition.MoveSpeed * (1f - slow);
            return speed < 0f ? 0f : speed;
        }

        /// <summary>血条比例（UI 用）。</summary>
        public float HealthRatio()
        {
            return State != null && State.HpMax > 0f ? State.Hp / State.HpMax : 0f;
        }

        /// <summary>资源条比例（UI 用）。</summary>
        public float ResourceRatio()
        {
            return State != null && State.ResourceMax > 0f ? State.Resource / State.ResourceMax : 0f;
        }

        protected void NotifyChanged()
        {
            System.Action<ActorEntity> handler = OnStateChanged;
            if (handler != null) handler(this);
        }
    }
}
