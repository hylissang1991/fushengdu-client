// 《浮生渡》表现层 · 战斗世界（权威端模拟）
// @trace ADR-001 N-2 / N-3：伤害、掉落、经验一律服务端结算；客户端只发意图。
// @trace D-14：掉落率与战斗数值不得与任何付费项挂钩（本文件无任何付费入口）。
// 本类只在「权威端」运行：离线单机 或 LAN 主机（FishNet Server）。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 权威端接口。离线与联网各一套实现，业务层只依赖本接口（ADR-001 N-8）。
    /// </summary>
    public interface IWorldAuthority
    {
        void RequestCast(int casterId, Core.SkillId skill, int targetId);
        void RequestInteract(int playerId, int npcId);
        void RequestPickup(int playerId, int itemUid);
        void RequestLootChoice(int playerId, int sessionId, Core.LootChoice choice);
        /// <summary>请求穿戴背包中的一件装备（服务端权威 —— 缺陷 N-2）。</summary>
        void RequestEquip(int playerId, int itemUid);
        /// <summary>请求立即复活（跳过剩余等待 —— 缺陷 N-1）。</summary>
        void RequestRespawn(int playerId);
    }

    /// <summary>待分配掉落会话（D-19，服务端持有）。</summary>
    public sealed class LootSessionRuntime
    {
        public int SessionId;
        public Core.LootRollSession Session;
        public Vector3 Position;
        public float Remaining;
        public Dictionary<int, int> NeedWinStreak = new Dictionary<int, int>();
    }

    /// <summary>
    /// 副本/团本运行期掉落上下文（由 DungeonInstanceManager 在进入副本时设置，退出时清空）。
    /// BOSS 死亡时若本上下文激活，则走副本掉落规则（DungeonLoot）而非野外掉落表。
    /// </summary>
    public sealed class ActiveDungeonLootContext
    {
        public int DungeonId;
        public string RuleKey = "";        // D-06 / D-26-sub / D-26-myth
        public bool IsRaid;
        public int[] BossIds = new int[0];
        public List<int> Participants = new List<int>();           // 参战玩家 actorId（离线单人即本地玩家）
        public Core.DungeonLootRunState Run = new Core.DungeonLootRunState(); // 跨 BOSS 的保底/上限累计
    }

    /// <summary>
    /// 世界模拟：单位注册、每帧推进、怪物 AI、技能结算、经验与掉落发放。
    /// </summary>
    public sealed class CombatWorld : MonoBehaviour
    {
        public static CombatWorld Instance { get; private set; }

        /// <summary>
        /// 当前激活的副本/团本掉落上下文（null = 非副本环境，走野外掉落表）。
        /// 进入副本时由 DungeonInstanceManager 赋值，退出时置 null。
        /// </summary>
        public ActiveDungeonLootContext ActiveDungeonLoot { get; set; }

        private readonly Dictionary<int, ActorEntity> _actors = new Dictionary<int, ActorEntity>();
        private readonly List<ActorEntity> _actorList = new List<ActorEntity>();
        private readonly List<Core.CombatEvent> _events = new List<Core.CombatEvent>();
        private readonly Dictionary<int, LootSessionRuntime> _lootSessions = new Dictionary<int, LootSessionRuntime>();
        private readonly Dictionary<int, Core.ClassId> _playerClasses = new Dictionary<int, Core.ClassId>();

        private Core.Rng _rng;
        private Core.CombatResolver _resolver;
        private Core.LootService _loot;
        private int _nextActorId = 1;
        private int _nextSessionId = 1;

        [Header("规则参数")]
        public float PartyLootRadius = 30f;   // 米：范围内多于 1 名玩家则进入掷骰流程
        public float RespawnDelaySeconds = Core.RespawnRules.DefaultDelaySeconds; // 玩家死亡到自动复活的等待
        public Vector3 RespawnPoint = new Vector3(0f, 1f, 0f);   // 兜底复活点（无标记时使用）

        // ------------------------------------------------------------ 事件
        public event System.Action<Core.CombatEvent> OnCombatEvent;
        public event System.Action<int, int> OnXpGained;          // (playerActorId, xp)
        public event System.Action<int, int> OnLevelUp;           // (playerActorId, newLevel)
        public event System.Action<int, int> OnItemGranted;       // (playerActorId, itemDefId)
        public event System.Action<int, int, Vector3> OnSilverGained; // (playerActorId, 渡银, 击杀位置)

        // 渡银飘字实跑计数（无头 playtest 校验用）：由 UI 层 SpawnSilverFloat 写入，Gameplay 层 PlaytestHarness 读取。
        // 放在 Gameplay 程序集（而非 UI），避免 Gameplay 反向依赖 UI（架构要求单向：UI → Gameplay）。
        public static int SilverFloatSpawned;
        public static string LastSilverFloatText;

        /// <summary>统一触发渡银到账事件（击杀 / 任务交付共用），供外部（如 NetWorldBridge）调用。</summary>
        public void RaiseSilverGained(int playerActorId, int silver, Vector3 worldPos)
        {
            if (OnSilverGained != null) OnSilverGained(playerActorId, silver, worldPos);
        }
        public event System.Action<LootSessionRuntime> OnLootSessionStarted;
        public event System.Action<int, int, Core.LootChoice> OnLootResolved; // (winnerId, itemDefId, tier)
        public event System.Action<int> OnActorDied;              // (actorId)
        /// <summary>一次技能结算结束（无论成败）；参数含施法者、技能、失败原因。归墟词缀 714 星序紊乱据此重掷 CD。</summary>
        public event System.Action<int, Core.SkillId, Core.CastFailReason> OnCastResolved;
        public event System.Action<int, float> OnPlayerRespawnCountdown; // (playerActorId, 剩余秒)
        public event System.Action<int, Vector3> OnPlayerRespawned;      // (playerActorId, 复活点)

        private void Awake()
        {
            Instance = this;
            // 种子取自时间；服务端权威，客户端不参与随机（副本 GDD E-9）
            _rng = new Core.Rng((uint)System.DateTime.UtcNow.Ticks);
            _resolver = new Core.CombatResolver(_rng);
            _loot = new Core.LootService(_rng);
            BuildDefaultLootTables();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // -------------------------------------------------------- 单位注册
        public void Register(ActorEntity actor)
        {
            if (actor == null || actor.State == null) return;
            if (actor.State.Id <= 0) actor.State.Id = _nextActorId++;
            _actors[actor.State.Id] = actor;
            if (!_actorList.Contains(actor)) _actorList.Add(actor);
            if (actor.IsPlayer)
            {
                var player = actor as PlayerActor;
                if (player != null) _playerClasses[actor.State.Id] = player.Class;
            }
        }

        public void Unregister(ActorEntity actor)
        {
            if (actor == null || actor.State == null) return;
            _actors.Remove(actor.State.Id);
            _actorList.Remove(actor);
            _playerClasses.Remove(actor.State.Id);
        }

        public ActorEntity Get(int actorId)
        {
            ActorEntity a;
            return _actors.TryGetValue(actorId, out a) ? a : null;
        }

        public IReadOnlyList<ActorEntity> Actors => _actorList;

        public IReadOnlyDictionary<int, Core.ClassId> PlayerClasses => _playerClasses;

        // -------------------------------------------------------- 每帧推进
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            _events.Clear();

            for (int i = 0; i < _actorList.Count; i++)
            {
                ActorEntity actor = _actorList[i];
                if (actor == null) continue;
                actor.TickAuthority(dt);
            }

            RunMonsterAi(dt);
            TickRespawns(dt);
            TickLootSessions(dt);
            ProcessDeaths();

            for (int i = 0; i < _events.Count; i++) Dispatch(_events[i]);
        }

        // -------------------------------------------------------- 复活（缺陷 N-1）
        /// <summary>
        /// 权威端单点推进玩家复活倒计时；归零即执行复活。
        /// 复活点优先取场景内最近的 RespawnPointMarker，无标记时退回 RespawnPoint。
        /// </summary>
        private void TickRespawns(float dt)
        {
            for (int i = 0; i < _actorList.Count; i++)
            {
                var player = _actorList[i] as PlayerActor;
                if (player == null || player.State == null) continue;
                if (!Core.RespawnRules.IsAwaitingRespawn(player.State)) continue;

                // 倒计时每帧上报，供 UI 显示「N 秒后复活」
                if (OnPlayerRespawnCountdown != null) OnPlayerRespawnCountdown(player.ActorId, player.State.RespawnTimer);

                if (!Core.RespawnRules.Advance(player.State, dt)) continue;
                DoRespawn(player);

                if (OnPlayerRespawnCountdown != null) OnPlayerRespawnCountdown(player.ActorId, 0f);
            }
        }

        /// <summary>执行复活并广播（联机时由网络层转成 RPC）。</summary>
        private void DoRespawn(PlayerActor player)
        {
            if (player == null || player.State == null) return;
            Vector3 point = FindNearestRespawnPoint(player.transform.position);

            Core.RespawnRules.ApplyRespawn(player.State);
            player.Teleport(point);
            player.SetTarget(null);
            player.RefreshFromEquipment();

            if (OnPlayerRespawned != null) OnPlayerRespawned(player.ActorId, point);
        }

        /// <summary>取最近的复活点标记；场景中无标记时返回兜底点。</summary>
        public Vector3 FindNearestRespawnPoint(Vector3 from)
        {
            RespawnPointMarker[] markers = FindObjectsOfType<RespawnPointMarker>();
            if (markers == null || markers.Length == 0) return RespawnPoint;

            Vector3 best = RespawnPoint;
            float bestDist = float.MaxValue;
            for (int i = 0; i < markers.Length; i++)
            {
                float d = (markers[i].transform.position - from).sqrMagnitude;
                if (d >= bestDist) continue;
                bestDist = d;
                best = markers[i].transform.position;
            }
            return best;
        }

        /// <summary>权威端入口：请求立即复活（UI 按钮 / 按键）。</summary>
        public void RequestRespawn(int playerId)
        {
            ActorEntity actor = Get(playerId);
            var player = actor as PlayerActor;
            if (player == null || player.State == null) return;
            if (!Core.RespawnRules.IsAwaitingRespawn(player.State)) return;
            DoRespawn(player);
        }

        /// <summary>权威端入口：穿戴装备并刷新派生属性（缺陷 N-2）。</summary>
        public void RequestEquip(int playerId, int itemUid)
        {
            ActorEntity actor = Get(playerId);
            var player = actor as PlayerActor;
            if (player == null) return;

            Core.ItemInstance inst = player.Inventory.Find(itemUid);
            if (inst == null) return;

            Core.ItemDef def = Core.ItemTable.Default.Get(inst.DefId);
            if (def == null || def.Slot == Core.EquipSlot.None) return;

            int displaced = player.Inventory.Equip(itemUid, def);
            player.RefreshFromEquipment();

            if (OnItemGranted != null) OnItemGranted(playerId, def.Id);
            if (displaced != 0) { /* 被替换下来的装备仍在背包内（Equip 只改槽位映射），无需额外处理 */ }
        }

        private void RunMonsterAi(float dt)
        {
            for (int i = 0; i < _actorList.Count; i++)
            {
                var monster = _actorList[i] as MonsterActor;
                if (monster == null || monster.State == null) continue;
                if (monster.State.Dead || monster.WaitingRespawn) continue;

                // 无目标 → 在仇恨半径内寻找最近玩家
                if (monster.Brain.TargetId == 0)
                {
                    ActorEntity nearest = FindNearestPlayer(monster.transform.position, monster.Definition != null ? monster.Definition.AggroRadius : 10f);
                    if (nearest != null)
                    {
                        monster.Brain.Acquire(nearest.ActorId);
                        monster.Threat.Add(nearest.ActorId, 1f);
                    }
                }

                ActorEntity target = monster.Brain.TargetId != 0 ? Get(monster.Brain.TargetId) : null;
                if (target != null && target.IsDead)
                {
                    monster.Brain.Release();
                    monster.Threat.Clear();
                    continue;
                }

                bool attackReady;
                monster.RunBrain(dt, target, out attackReady);
                if (attackReady && target != null) MonsterAutoAttack(monster, target);
            }
        }

        private ActorEntity FindNearestPlayer(Vector3 from, float radius)
        {
            ActorEntity best = null;
            float bestDist = radius * radius;
            for (int i = 0; i < _actorList.Count; i++)
            {
                ActorEntity a = _actorList[i];
                if (a == null || !a.IsPlayer || a.IsDead) continue;
                float d = (a.transform.position - from).sqrMagnitude;
                if (d > bestDist) continue;
                bestDist = d;
                best = a;
            }
            return best;
        }

        /// <summary>怪物普攻（不走 56 技能表，直接用怪物定义的 W/S/E_AP 与普攻系数）。</summary>
        private void MonsterAutoAttack(MonsterActor monster, ActorEntity target)
        {
            if (target == null || target.State == null || target.State.Dead) return;

            float targetDt = target.State.TotalIncomingMitigation();
            float m = Core.Damage.BuildMultiplier(targetDt, 0f);
            float amount = Core.Damage.Compute(monster.State.WeaponDps, monster.State.WeaponSpeed,
                monster.State.E_AP, monster.Definition.AttackK, m, _rng.CombatRoll());

            bool died = target.State.ApplyDamage(amount);
            target.State.MarkInCombat();

            _events.Add(new Core.CombatEvent
            {
                Kind = Core.CombatEventKind.Damage, SourceId = monster.ActorId, TargetId = target.ActorId, Amount = amount
            });

            // 受击资源（GDD §2.5）
            float victimGain = target.State.ResourceId == Core.ResourceType.Qi ? 5f
                : (target.State.ResourceId == Core.ResourceType.Rage ? 8f : 0f);
            if (victimGain > 0f) target.State.ChangeResource(victimGain);

            if (died)
            {
                _events.Add(new Core.CombatEvent { Kind = Core.CombatEventKind.Death, SourceId = monster.ActorId, TargetId = target.ActorId });
                OnDeath(target, monster);
            }
        }

        // -------------------------------------------------------- 技能结算
        /// <summary>权威端执行技能请求（含完整合法性校验 —— ADR-001 N-3）。</summary>
        public Core.CastFailReason Cast(int casterId, Core.SkillId skill, int targetId)
        {
            ActorEntity caster = Get(casterId);
            if (caster == null || caster.IsPlayer == false) return Core.CastFailReason.NoTarget;
            ActorEntity target = targetId != 0 ? Get(targetId) : null;

            // 射程校验（服务端按世界坐标复核，不信任客户端 —— GDD §5.6 P-3）
            Core.SkillDef def = Core.SkillTable.Get(skill);
            if (def == null) return Core.CastFailReason.UnknownSkill;
            if (def.Shape == Core.SkillShape.SingleTarget)
            {
                if (target == null) return Core.CastFailReason.NoTarget;
                float range = def.Range;
                // 御剑姿态：射程 +10 m（GDD §4.3）
                if (def.RequiredStance == Core.StanceId.YuJian || caster.State.Buffs.InStance(Core.StanceId.YuJian)) range += 10f;
                if (caster.transform.position.DistanceXZ(target.transform.position) > range + 0.5f)
                    return Core.CastFailReason.OutOfRange;
            }

            List<ActorEntity> area = null;
            if (def.Shape == Core.SkillShape.SelfAoe || def.Shape == Core.SkillShape.Displacement)
            {
                area = CollectEnemies(caster.transform.position, def.Radius > 0f ? def.Radius : 5f, caster.State.Faction);
            }
            else if (def.MaxTargets > 1)
            {
                area = CollectEnemies(caster.transform.position, 3f, caster.State.Faction);
            }

            var areaStates = new List<Core.ActorState>();
            if (area != null)
            {
                for (int i = 0; i < area.Count; i++)
                {
                    if (area[i] == target) continue;
                    areaStates.Add(area[i].State);
                }
            }

            int before = _events.Count;
            Core.CastFailReason fail = _resolver.Execute(caster.State, target != null ? target.State : null, areaStates, skill, _events);

            // 位移：服务端执行并记录（后续接位移终点校验 —— GDD §5.6 P-3）
            for (int i = before; i < _events.Count; i++)
            {
                Core.CombatEvent e = _events[i];
                if (e.Kind != Core.CombatEventKind.Displacement) continue;
                Vector3 forward = caster.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                var player = caster as PlayerActor;
                if (player != null) player.Teleport(caster.transform.position + forward.normalized * e.FloatValue);
            }

            // 仇恨：玩家造成的伤害都计入怪物仇恨表
            for (int i = before; i < _events.Count; i++)
            {
                Core.CombatEvent e = _events[i];
                if (e.Kind != Core.CombatEventKind.Damage) continue;
                ActorEntity victim = Get(e.TargetId);
                var m = victim as MonsterActor;
                if (m != null) m.Threat.Add(casterId, e.Amount);
                if (e.Kind == Core.CombatEventKind.Damage) ApplyThreatlessSideEffects(e);
            }

            // 死亡处理
            for (int i = before; i < _events.Count; i++)
            {
                Core.CombatEvent e = _events[i];
                if (e.Kind != Core.CombatEventKind.Death) continue;
                ActorEntity victim = Get(e.TargetId);
                if (victim != null) OnDeath(victim, Get(e.SourceId));
            }

            if (OnCastResolved != null) OnCastResolved(casterId, skill, fail);
            return fail;
        }

        private void ApplyThreatlessSideEffects(Core.CombatEvent e)
        {
            // 预留：反伤 / 吸血等跨单位效果。本期无坦克职业，保持空实现但保留扩展点。
        }

        private List<ActorEntity> CollectEnemies(Vector3 center, float radius, Core.ActorFaction faction)
        {
            var result = new List<ActorEntity>();
            float r2 = radius * radius;
            for (int i = 0; i < _actorList.Count; i++)
            {
                ActorEntity a = _actorList[i];
                if (a == null || a.IsDead || a.State == null) continue;
                if (a.State.Faction == faction) continue;
                if ((a.transform.position - center).sqrMagnitude > r2) continue;
                result.Add(a);
            }
            return result;
        }

        // -------------------------------------------------------- 死亡与奖励
        private void ProcessDeaths()
        {
            // 玩家复活计时在 PlayerActor/UI 层处理；此处保证死亡事件已派发。
        }

        private void OnDeath(ActorEntity victim, ActorEntity killer)
        {
            if (victim == null) return;
            if (OnActorDied != null) OnActorDied(victim.ActorId);

            // 玩家死亡：由权威端启动复活倒计时（缺陷 N-1）
            var deadPlayer = victim as PlayerActor;
            if (deadPlayer != null)
            {
                Core.RespawnRules.BeginRespawn(deadPlayer.State, RespawnDelaySeconds);
                if (OnPlayerRespawnCountdown != null)
                    OnPlayerRespawnCountdown(deadPlayer.ActorId, RespawnDelaySeconds);
                return;
            }

            var monster = victim as MonsterActor;
            if (monster == null) return;

            // 经验：发给所有参与战斗（仇恨表内）的玩家
            int xp = Core.Progression.XpForMonster(monster.State.Level);

            // 渡银结算口径：
            //  · 普通怪 → 纯线性（4 + 等级×3）
            //  · BOSS   → 差异化保底（保底基数 + 等级×系数），高于同等级普通怪
            //  · 团本终boss（mythic D-26-myth）→ 最高保底
            // 保底确保即便低等级 BOSS 也发放有意义的渡银，区别于普通怪纯线性。
            int silver;
            if (monster.State.IsBoss)
            {
                bool isRaidFinal = ActiveDungeonLoot != null
                                   && ActiveDungeonLoot.IsRaid
                                   && ActiveDungeonLoot.RuleKey == "D-26-myth"
                                   && IsFinalBoss(monster.MonsterDefId);
                silver = isRaidFinal
                    ? Core.Progression.SilverForRaidFinalBoss(monster.State.Level)
                    : Core.Progression.SilverForBoss(monster.State.Level);
            }
            else
            {
                silver = Core.Progression.SilverForMonster(monster.State.Level);
            }

            foreach (KeyValuePair<int, ActorEntity> kv in _actors)
            {
                ActorEntity a = kv.Value;
                if (!a.IsPlayer) continue;
                if (monster.Threat.ThreatOf(a.ActorId) <= 0f) continue;
                var player = a as PlayerActor;
                if (player == null) continue;
                int before = player.Progression.Level;
                player.Progression.AddXp(xp);
                player.Progression.TotalKills++;
                // 渡银：按上述口径结算（普通怪线性 / BOSS 保底 / 团本终boss 最高保底），参战者均得
                player.Silver += silver;
                player.Quests.OnKill(monster.Definition != null ? monster.Definition.Id : 0);
                if (OnXpGained != null) OnXpGained(a.ActorId, xp);
                // 渡银飘字反馈：仅给参战者本人（playerId 过滤在 UI 侧做），位置取击杀点
                RaiseSilverGained(a.ActorId, silver, monster.transform.position);
                if (player.Progression.Level != before)
                {
                    Core.Progression.ApplyLevel(player.State, player.Progression.Level);
                    player.RefreshFromEquipment();
                    if (OnLevelUp != null) OnLevelUp(a.ActorId, player.Progression.Level);
                }
            }

            // 掉落：副本环境内 BOSS 走副本/团本掉落规则；其余走野外掉落表（D-19 组队掷骰；单人直接进包）
            if (ActiveDungeonLoot != null && monster.State.IsBoss)
            {
                int bossId = monster.MonsterDefId;
                bool isFinal = IsFinalBoss(bossId);
                ResolveDungeonLoot(monster, bossId, isFinal, ActiveDungeonLoot.RuleKey, ActiveDungeonLoot.DungeonId);

                // 武器碎片（D-24）：20 人本终 BOSS 击杀时，每名参战玩家 2 块统一碎片
                if (ActiveDungeonLoot.IsRaid && ActiveDungeonLoot.RuleKey == "D-26-myth" && isFinal)
                    GrantWeaponShards();
            }
            else
            {
                int defId = _loot.RollDrop(monster.MonsterDefId);
                if (defId != 0) GrantDrop(defId, monster.transform.position);
            }
        }

        /// <summary>
        /// 副本/团本掉落结算并应用进度规则（M6）：周帽上限、团末保底、首通标记。
        /// 周帽规则映射集中在 Core.DungeonProgressRules，不改动冻结的 Core/Dungeon.cs。
        /// 区分两层：DungeonLootRunState.MythicDroppedThisRun（单次 run 内）与 WeeklyMythicClaimed（本周级、跨周重置）。
        /// </summary>
        private void ResolveDungeonLoot(MonsterActor monster, int bossId, bool isFinal, string ruleKey, int dungeonId)
        {
            var sessions = _loot.RollDropByRule(ruleKey, bossId, isFinal, ActiveDungeonLoot.Run);

            var store = DungeonProgressStore.Instance;
            int cap = store != null ? store.WeeklyCapLimit(ruleKey) : 0;
            int used = store != null ? store.WeeklyCapUsed(dungeonId) : 0;
            int slots = cap <= 0 ? int.MaxValue : Mathf.Max(0, cap - used);
            Core.ItemQuality premium = Core.DungeonProgressRules.PremiumTier(ruleKey);

            int grantedPremium = 0;
            foreach (Core.LootRollSession s in sessions)
            {
                bool premiumItem = premium != Core.ItemQuality.None && s.QualityCap == premium;
                if (premiumItem && slots <= 0)
                {
                    Debug.Log("[DungeonProgress] 周帽已满（" + used + "/" + cap + "），跳过顶级掉落 id=" + s.ItemDefId);
                    continue;
                }
                if (premiumItem) slots--;
                GrantDrop(s, monster.transform.position);
                if (premiumItem) grantedPremium++;
            }

            // 周保底：D-26-myth 团末（终 BOSS）若本周未发保底神话，强制发 1 件 Mythic(7)
            if (store != null && ruleKey == "D-26-myth" && isFinal && !store.IsMythicClaimed(dungeonId))
            {
                var mythic = _loot.CreateSession(Core.DungeonLoot.GenMythic, Core.LootSource.Party);
                mythic.QualityCap = Core.ItemQuality.Mythic;
                GrantDrop(mythic, monster.transform.position);
                store.SetMythicClaimed(dungeonId, true);
                grantedPremium++;
                Debug.Log("[DungeonProgress] 团末保底：强制发放 1 件最高级神话（id=" + Core.DungeonLoot.GenMythic + "）");
            }

            if (store != null && grantedPremium > 0)
                store.AddWeeklyCap(dungeonId, grantedPremium);

            // 首通：终 BOSS 击杀即标记
            if (store != null && isFinal && !store.IsFirstCleared(dungeonId))
            {
                store.MarkFirstClear(dungeonId);
                Debug.Log("[DungeonProgress] 首通达成：副本 id=" + dungeonId);
            }

            if (store != null) store.Save();
        }

        /// <summary>判断某 bossId 是否为本副本的终 BOSS（BossIds 末位）。</summary>
        private bool IsFinalBoss(int bossId)
        {
            if (ActiveDungeonLoot == null || ActiveDungeonLoot.BossIds == null || ActiveDungeonLoot.BossIds.Length == 0) return false;
            return bossId == ActiveDungeonLoot.BossIds[ActiveDungeonLoot.BossIds.Length - 1];
        }

        /// <summary>向参战玩家发放武器碎片（D-24：每人 2 块，统一碎片）。</summary>
        private void GrantWeaponShards()
        {
            var targets = ActiveDungeonLoot != null && ActiveDungeonLoot.Participants.Count > 0
                ? ActiveDungeonLoot.Participants
                : new List<int>();
            for (int i = 0; i < _actorList.Count; i++)
            {
                var p = _actorList[i] as PlayerActor;
                if (p == null || p.IsDead) continue;
                if (targets.Count > 0 && !targets.Contains(p.ActorId)) continue;
                for (int k = 0; k < Core.DungeonLoot.WeaponShardPerPlayer; k++)
                    p.Inventory.Add(Core.DungeonLoot.WeaponShardItemDefId, false);
                p.RefreshFromEquipment();
                if (OnItemGranted != null) OnItemGranted(p.ActorId, Core.DungeonLoot.WeaponShardItemDefId);
            }
        }

        /// <summary>发放掉落：范围内玩家 ≥2 → 掷骰流程；否则直接给最近的参与者。</summary>
        private void GrantDrop(int itemDefId, Vector3 position)
        {
            GrantDrop(_loot.CreateSession(itemDefId, Core.LootSource.Party), position);
        }

        /// <summary>发放一件预置的待分配掉落（副本掉落使用此重载，保留 IsUnique / 绑定等元数据）。</summary>
        private void GrantDrop(Core.LootRollSession session, Vector3 position)
        {
            var nearby = new List<PlayerActor>();
            for (int i = 0; i < _actorList.Count; i++)
            {
                var p = _actorList[i] as PlayerActor;
                if (p == null || p.IsDead) continue;
                if ((p.transform.position - position).sqrMagnitude > PartyLootRadius * PartyLootRadius) continue;
                nearby.Add(p);
            }

            if (nearby.Count <= 1)
            {
                PlayerActor only = nearby.Count == 1 ? nearby[0] : FindAnyPlayer();
                if (only == null) return;
                only.Inventory.Add(session.ItemDefId, session.Bind == Core.BindType.OnPickup);
                only.RefreshFromEquipment();
                if (OnItemGranted != null) OnItemGranted(only.ActorId, session.ItemDefId);
                return;
            }

            // 组队：进入需要 / 掷骰子 / 放弃 流程（D-19；产出来源标记为组队 → 拾取绑定）
            var runtime = new LootSessionRuntime
            {
                SessionId = _nextSessionId++,
                Session = session,
                Position = position,
                Remaining = 60f
            };
            _lootSessions[runtime.SessionId] = runtime;
            if (OnLootSessionStarted != null) OnLootSessionStarted(runtime);
        }

        private PlayerActor FindAnyPlayer()
        {
            for (int i = 0; i < _actorList.Count; i++)
            {
                var p = _actorList[i] as PlayerActor;
                if (p != null && !p.IsDead) return p;
            }
            return null;
        }

        private void TickLootSessions(float dt)
        {
            if (_lootSessions.Count == 0) return;
            var toResolve = new List<int>();
            foreach (KeyValuePair<int, LootSessionRuntime> kv in _lootSessions)
            {
                kv.Value.Remaining -= dt;
                if (kv.Value.Remaining > 0f) continue;
                toResolve.Add(kv.Key);
            }
            for (int i = 0; i < toResolve.Count; i++) ResolveLootSession(toResolve[i]);
        }

        /// <summary>结算一次拾取分配（D-19：需要优先于掷骰子，同级比骰子）。</summary>
        public void ResolveLootSession(int sessionId)
        {
            LootSessionRuntime runtime;
            if (!_lootSessions.TryGetValue(sessionId, out runtime)) return;

            Core.LootChoice tier;
            int winner = runtime.Session.Resolve(_rng, _playerClasses, runtime.NeedWinStreak, out tier);
            _lootSessions.Remove(sessionId);

            if (winner == 0)
            {
                // 全员放弃 / 超时 → 无人认领，直接销毁（副本 GDD §2.5.3）
                if (OnLootResolved != null) OnLootResolved(0, runtime.Session.ItemDefId, Core.LootChoice.Pass);
                return;
            }

            ActorEntity actor = Get(winner);
            var player = actor as PlayerActor;
            if (player != null)
            {
                player.Inventory.Add(runtime.Session.ItemDefId, runtime.Session.Bind == Core.BindType.OnPickup);
                player.RefreshFromEquipment();
                if (OnItemGranted != null) OnItemGranted(winner, runtime.Session.ItemDefId);
            }

            if (tier == Core.LootChoice.Need)
            {
                int streak;
                runtime.NeedWinStreak.TryGetValue(winner, out streak);
                runtime.NeedWinStreak[winner] = streak + 1;
            }

            if (OnLootResolved != null) OnLootResolved(winner, runtime.Session.ItemDefId, tier);
        }

        public void SubmitLootChoice(int playerId, int sessionId, Core.LootChoice choice)
        {
            LootSessionRuntime runtime;
            if (!_lootSessions.TryGetValue(sessionId, out runtime)) return;
            runtime.Session.SetChoice(playerId, choice);
        }

        /// <summary>调试/校验用：立即结算一次死亡（BOSS 死亡走副本掉落规则）。</summary>
        public void DebugKill(ActorEntity victim)
        {
            if (victim == null) return;
            OnDeath(victim, null);
        }

        public LootSessionRuntime GetLootSession(int sessionId)
        {
            LootSessionRuntime r;
            return _lootSessions.TryGetValue(sessionId, out r) ? r : null;
        }

        public IEnumerable<LootSessionRuntime> ActiveLootSessions()
        {
            return new List<LootSessionRuntime>(_lootSessions.Values);
        }

        // -------------------------------------------------------- 掉落表
        private void BuildDefaultLootTables()
        {
            // 野外掉落：凡品为主，精良小概率；绝不出现传说 / 神话（D-06）
            var wolf = new Core.LootTable();
            wolf.Add(2001, 0.10f);
            wolf.Add(2002, 0.08f);
            wolf.Add(3001, 0.35f);
            _loot.Register(1, wolf);

            var boar = new Core.LootTable();
            boar.Add(2011, 0.25f);
            boar.Add(2012, 0.22f);
            boar.Add(2013, 0.20f);
            boar.Add(1011, 0.10f);
            boar.Add(1012, 0.10f);
            _loot.Register(2, boar);

            var bandit = new Core.LootTable();
            bandit.Add(2001, 0.12f);
            bandit.Add(2012, 0.10f);
            bandit.Add(2023, 0.06f);
            bandit.Add(3001, 0.25f);
            _loot.Register(3, bandit);
        }

        // -------------------------------------------------------- 派发
        private void Dispatch(Core.CombatEvent e)
        {
            if (OnCombatEvent != null) OnCombatEvent(e);
        }

        /// <summary>广播一条外部事件（供网络层与 UI 复用）。</summary>
        public void PushEvent(Core.CombatEvent e)
        {
            Dispatch(e);
        }
    }

    internal static class Vector3Extensions
    {
        /// <summary>忽略高度的水平距离（射程校验用）。</summary>
        public static float DistanceXZ(this Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
