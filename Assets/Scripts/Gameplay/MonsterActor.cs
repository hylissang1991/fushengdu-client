// 《浮生渡》表现层 · 怪物实体与 AI 执行
// @trace 职业系统 GDD §2.4（仇恨与追击）、概念文档 §5
// 决策来自 Core.MonsterBrain（纯 C#），本层只负责移动执行与状态展示。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public sealed class MonsterActor : ActorEntity
    {
        [Header("刷新点")]
        public int MonsterDefId;

        private float _respawnTimer;
        private bool _waitingRespawn;
        private CharacterTint _tint;
        private bool _modelBuilt;

        public bool WaitingRespawn => _waitingRespawn;

        protected override void Awake()
        {
            base.Awake();
            Brain = new Core.MonsterBrain();
            Threat = new Core.ThreatTable();

            // 场景预摆放的怪物（MonsterDefId 已在编辑器写入）在 Awake 里就把模型拼好，
            // 这样联机的客户端实例也有外观（客户端不会走 Initialize）。
            if (MonsterDefId > 0)
            {
                Core.MonsterDef def = Core.MonsterTable.Default.Get(MonsterDefId);
                if (def != null) EnsureVisual(def);
            }
        }

        /// <summary>拼装可见模型（每种怪独立剪影）。只拼一次。</summary>
        public void EnsureVisual(Core.MonsterDef def)
        {
            if (_modelBuilt || def == null) return;
            _modelBuilt = true;
            RuntimeArt.AttachMonsterModel(gameObject, def);
            _tint = GetComponentInChildren<CharacterTint>();
        }

        /// <summary>按怪物定义初始化（服务端调用）。</summary>
        public void Initialize(Core.MonsterDef def, int actorId)
        {
            Definition = def;
            if (State == null) State = new Core.ActorState();
            State.Id = actorId;
            State.Name = def.Name;
            State.Faction = Core.ActorFaction.Monster;
            State.Level = def.Level;
            State.IsElite = def.IsElite;
            State.IsBoss = def.IsBoss;
            State.ResourceId = Core.ResourceType.None;
            State.WeaponDps = def.WeaponDps;
            State.WeaponSpeed = def.WeaponSpeed;
            State.E_AP = def.E_AP;
            State.Armor = def.Armor;
            State.HpMax = def.HpMax;
            State.Hp = def.HpMax;
            State.Dead = false;
            MonsterDefId = def.Id;
            SpawnPoint = transform.position;
            _waitingRespawn = false;
            _respawnTimer = 0f;

            // 首次初始化时按怪物定义拼装低模（见 RuntimeArt）：狼 / 山贼 / 野猪王 各有独立剪影。
            // 刷新时不受影响（只拼一次，见 EnsureVisual）。
            EnsureVisual(def);
        }

        public override void TickAuthority(float dt)
        {
            base.TickAuthority(dt);

            if (_waitingRespawn)
            {
                _respawnTimer -= dt;
                if (_respawnTimer <= 0f) Respawn();
                return;
            }

            if (State != null && State.Dead)
            {
                // 死亡计时到点 → 进入等待刷新
                if (DeathTimer >= DeathDespawnDelay) BeginRespawnWait();
            }
        }

        /// <summary>由 CombatWorld 驱动 AI 移动与攻击。</summary>
        public void RunBrain(float dt, ActorEntity target, out bool attackReady)
        {
            attackReady = false;
            if (State == null || Definition == null) return;

            float distToTarget = target != null ? Vector3.Distance(transform.position, target.transform.position) : -1f;
            float distToSpawn = Vector3.Distance(transform.position, SpawnPoint);

            Core.MonsterIntent intent = Brain.Update(dt, State, Definition, distToTarget, distToSpawn,
                target != null && !target.IsDead, out attackReady);

            switch (intent)
            {
                case Core.MonsterIntent.Chase:
                    MoveToward(target.transform.position, dt);
                    break;
                case Core.MonsterIntent.Return:
                    MoveToward(SpawnPoint, dt);
                    if (distToSpawn <= 0.5f) Brain.Release();
                    break;
                case Core.MonsterIntent.Attack:
                    // 原地攻击（贴脸即可，朝向目标）
                    if (target != null) FaceTarget(target.transform.position, dt);
                    break;
            }
        }

        private void MoveToward(Vector3 destination, float dt)
        {
            Vector3 dir = destination - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            dir.Normalize();
            float speed = CurrentMoveSpeed();
            transform.position += dir * speed * dt;
            FaceTarget(destination, dt);
        }

        private void FaceTarget(Vector3 destination, float dt)
        {
            Vector3 dir = destination - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 8f * dt);
        }

        private void BeginRespawnWait()
        {
            _waitingRespawn = true;
            _respawnTimer = Definition != null ? Definition.RespawnSeconds : 20f;
            if (_tint != null) _tint.SetVisible(false);
        }

        public void Respawn()
        {
            _waitingRespawn = false;
            _respawnTimer = 0f;
            transform.position = SpawnPoint;
            if (_tint != null)
            {
                _tint.SetVisible(true);
                _tint.ResetTint();
            }
            if (Definition != null) Initialize(Definition, State.Id);
            Brain.Release();
            Threat.Clear();
        }

        /// <summary>受击闪红（表现层反馈，不影响数值）。</summary>
        public void FlashHit()
        {
            if (_tint == null) return;
            _tint.SetTint(new Color(1f, 0.25f, 0.2f), 0.65f);
        }

        public void RestoreColor()
        {
            if (_tint != null) _tint.ResetTint();
        }
    }
}
