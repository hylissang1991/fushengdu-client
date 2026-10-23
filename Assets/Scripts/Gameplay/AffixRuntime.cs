// 《浮生渡》表现层 · 归墟侵蚀层词缀运行时
// 由 DungeonInstanceManager.EnterDungeon 在进本时挂在竞技场对象上。
// Init 从已解锁词缀池不重复抽 2，命中互斥组则重掷（≤3 次，兜底取首个非互斥项）。
// Update 按各词缀周期触发机制：6 个基础机制（701–706）+ 8 个进阶机制（707–714，本期全部实做）。
// 伤害/治疗均通过权威端 ActorState 施加，不绕过 CombatWorld。
// 进阶机制需要「召唤/分裂/守卫」的单位生成能力，故 Init 接收 DungeonSpawner 引用来生成额外单位；
// 这些额外单位统一登记在 _extraActors，OnDestroy 时清理，避免退出副本后泄漏 GameObject。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>归墟侵蚀层词缀运行时。</summary>
    public sealed class AffixRuntime : MonoBehaviour
    {
        private CombatWorld _world;
        private DungeonSpawner _spawner;
        private readonly System.Random _rng = new System.Random();
        private readonly List<ActiveAffix> _active = new List<ActiveAffix>();
        private readonly List<TempBuff> _tempBuffs = new List<TempBuff>();
        private readonly List<ErosionZone> _zones = new List<ErosionZone>();

        // —— 进阶机制（707–714）状态 ——
        private bool _mirrorEnabled, _gravityEnabled, _soulEnabled, _prolifEnabled;
        private bool _sacrificeEnabled, _lightDarkEnabled, _starChaosEnabled;

        private bool _mirrorSpawned;                       // 707：镜像只分裂一次
        private float _gravityTimer;                       // 708：拉扯窗口剩余
        private Vector3 _gravityCenter;                    // 708：拉扯中心

        private readonly List<MonsterActor> _guardians = new List<MonsterActor>(); // 712：祭坛守卫
        private bool _sacrificeCleared;                    // 712：守卫是否已全灭

        private float _lightDarkTimer;                     // 713：明暗切换累计
        private bool _darkPhase;                           // 713：当前是否暗期

        private readonly List<MonsterActor> _extraActors = new List<MonsterActor>(); // 所有词缀生成的额外单位（清理用）
        private readonly List<TimedActor> _mirrorCopies = new List<TimedActor>();     // 707：镜像（带自毁计时）
        private readonly HashSet<int> _copyIds = new HashSet<int>();                  // 710：已生成的分裂体 id（不再分裂）
        private readonly HashSet<int> _splitDoneIds = new HashSet<int>();             // 710：已分裂过的原 id

        /// <summary>当前生效的 2 个词缀定义（供 Slice 2b UI 展示）。</summary>
        public List<Core.AffixDef> GetActiveAffixes()
        {
            return _active.ConvertAll(a => a.Def);
        }

        /// <summary>初始化并抽取 2 个词缀（传入词缀池 + 解锁档位）。</summary>
        public void Init(int[] pool, int unlockTier, CombatWorld world, DungeonSpawner spawner = null)
        {
            _world = world;
            _spawner = spawner;
            AffixEffects.Reset();

            var candidates = new List<Core.AffixDef>();
            if (pool != null)
            {
                foreach (int id in pool)
                {
                    Core.AffixDef d = Core.AffixTable.Default.Get(id);
                    if (d != null && d.UnlockTier <= unlockTier) candidates.Add(d);
                }
            }

            List<Core.AffixDef> drawn = DrawTwo(candidates);
            RegisterActive(drawn);
            PostInitFlags();
        }

        /// <summary>用入口面板「公示」预先抽中的词缀直接初始化（保证预览 == 实际；M2 入口面板调用）。</summary>
        public void Init(List<Core.AffixDef> preDrawn, CombatWorld world, DungeonSpawner spawner = null)
        {
            _world = world;
            _spawner = spawner;
            AffixEffects.Reset();

            var drawn = new List<Core.AffixDef>();
            if (preDrawn != null) drawn.AddRange(preDrawn);
            RegisterActive(drawn);
            PostInitFlags();
        }

        private void RegisterActive(List<Core.AffixDef> drawn)
        {
            foreach (Core.AffixDef d in drawn)
            {
                if (d == null) continue;
                _active.Add(new ActiveAffix { Def = d, Interval = GetInterval(d.Id), Timer = GetInterval(d.Id) * 0.5f });
            }

            if (_active.Count == 0)
            {
                Debug.Log("[Affix] 本次无可用词缀（池为空或未解锁）。");
                return;
            }
            Debug.Log("[Affix] 本次归墟侵蚀层词缀：" +
                string.Join(" + ", _active.ConvertAll(a => a.Def.Name + "（" + a.Def.Description + "）")));
        }

        /// <summary>根据本次抽中的词缀设置跨切状态 / 生成守卫 / 订阅事件。</summary>
        private void PostInitFlags()
        {
            foreach (var a in _active)
            {
                switch (a.Def.Id)
                {
                    case 707: _mirrorEnabled = true; break;
                    case 708: _gravityEnabled = true; break;
                    case 709: _soulEnabled = true; break;
                    case 710: _prolifEnabled = true; break;
                    case 711:
                        AffixEffects.TimeLagIntervalMul = 0.6f;
                        Debug.Log("[Affix] 时滞：BOSS 周期 AOE 间隔/预警 ×0.6（反应窗口缩短 40%）");
                        break;
                    case 712:
                        _sacrificeEnabled = true;
                        SpawnGuardians();
                        break;
                    case 713: _lightDarkEnabled = true; break;
                    case 714:
                        _starChaosEnabled = true;
                        AffixEffects.StarChaosActive = true;
                        break;
                }
            }

            // 709/710 依赖死亡事件；714 依赖施放事件
            if (_soulEnabled || _prolifEnabled)
            {
                if (_world != null) _world.OnActorDied += OnActorDied;
            }
            if (_starChaosEnabled)
            {
                if (_world != null) _world.OnCastResolved += OnCastResolved;
            }
        }

        // -------------------------------------------------------- 抽取与重掷
        public static List<Core.AffixDef> Draw(int[] pool, int unlockTier)
        {
            return DrawFromCandidates(BuildCandidates(pool, unlockTier), new System.Random());
        }

        private static List<Core.AffixDef> BuildCandidates(int[] pool, int unlockTier)
        {
            var candidates = new List<Core.AffixDef>();
            if (pool != null)
            {
                foreach (int id in pool)
                {
                    Core.AffixDef d = Core.AffixTable.Default.Get(id);
                    if (d != null && d.UnlockTier <= unlockTier) candidates.Add(d);
                }
            }
            return candidates;
        }

        private List<Core.AffixDef> DrawTwo(List<Core.AffixDef> candidates)
        {
            return DrawFromCandidates(candidates, _rng);
        }

        private static List<Core.AffixDef> DrawFromCandidates(List<Core.AffixDef> candidates, System.Random rng)
        {
            var res = new List<Core.AffixDef>();
            if (candidates == null || candidates.Count == 0) return res;
            if (candidates.Count == 1) { res.Add(candidates[0]); res.Add(candidates[0]); return res; }

            int a = rng.Next(candidates.Count);
            int b = rng.Next(candidates.Count);
            int tries = 0;
            while ((b == a || (candidates[a].MutexGroup != 0 && candidates[a].MutexGroup == candidates[b].MutexGroup)) && tries < 3)
            {
                b = rng.Next(candidates.Count);
                tries++;
            }
            if (b == a || (candidates[a].MutexGroup != 0 && candidates[a].MutexGroup == candidates[b].MutexGroup))
                b = FindFirstNonMutex(candidates, a, b);

            res.Add(candidates[a]);
            res.Add(candidates[b]);
            return res;
        }

        private static int FindFirstNonMutex(List<Core.AffixDef> c, int a, int fallback)
        {
            for (int i = 0; i < c.Count; i++)
            {
                if (i == a) continue;
                if (c[a].MutexGroup != 0 && c[a].MutexGroup == c[i].MutexGroup) continue;
                return i;
            }
            return fallback;
        }

        private static float GetInterval(int id)
        {
            switch (id)
            {
                case 701: return 20f; // 卢恩狂暴
                case 702: return 6f;  // 东荒雷罚
                case 703: return 5f;  // 归墟侵蚀
                case 704: return 8f;  // 石肤
                case 705: return 15f; // 血月
                case 706: return 25f; // 静默
                case 707: return 20f; // 镜像（HP 触发，间隔仅作兜底）
                case 708: return 15f; // 引力异常
                case 709: return 10f; // 蚀魂（死亡触发，间隔兜底）
                case 710: return 20f; // 增殖（死亡触发，间隔兜底）
                case 711: return 12f; // 时滞（跨切状态，间隔兜底）
                case 712: return 20f; // 献祭（守卫触发，间隔兜底）
                case 713: return 20f; // 光暗交替
                case 714: return 12f; // 星序紊乱（施放触发，间隔兜底）
                default:  return 15f;
            }
        }

        // -------------------------------------------------------- 每帧推进
        private void Update()
        {
            if (_world == null) return;
            float dt = Time.deltaTime;

            // 周期型词缀（701–706 + 708 起手引力窗口）
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveAffix a = _active[i];
                a.Timer -= dt;
                if (a.Timer <= 0f)
                {
                    TriggerAffix(a.Def);
                    a.Timer += a.Interval;
                }
            }

            // 进阶机制逐帧逻辑
            TickMirror(dt);
            TickGravity(dt);
            TickGuardians();
            TickLightDark(dt);
            ProcessTempBuffs(dt);
            ProcessZones(dt);
        }

        private void TriggerAffix(Core.AffixDef def)
        {
            switch (def.Id)
            {
                // —— 6 个基础机制（纯数值 / 简单召唤类）——
                case 701: // 卢恩狂暴：周期给所有 BOSS WeaponDps ×1.4 短窗口
                    ApplyTempAtkToBosses(1.4f, 8f);
                    Debug.Log("[Affix] 卢恩狂暴：全体 BOSS 伤害 +40%（8s）");
                    break;
                case 702: // 东荒雷罚：周期对随机玩家造成 AOE 伤害
                    LightningStrike();
                    break;
                case 703: // 归墟侵蚀：在随机点生成持续 DoT 区域，区域内玩家每 tick 掉血
                    SpawnErosionZone();
                    break;
                case 704: // 石肤：周期给 BOSS 加吸收护盾
                    GrantBossShield();
                    break;
                case 705: // 血月：周期全体 BOSS 短暂增伤
                    ApplyTempAtkToBosses(1.25f, 6f);
                    Debug.Log("[Affix] 血月：附近怪物狂暴 +25%（6s）");
                    break;
                case 706: // 静默：周期令全体玩家短时无法施法
                    SilenceAllPlayers(2f);
                    break;
                // —— 进阶机制（707/709/710/711/712/713/714 由专属 Tick / 事件 / Init 处理）——
                case 708: // 引力异常：起手 2s 拉扯窗口
                    _gravityTimer = 2f;
                    _gravityCenter = CentroidOfPlayers();
                    Debug.Log("[Affix] 引力异常：全场向中心拉扯 2s（位移技能可抵抗）");
                    break;
            }
        }

        // -------------------------------------------------------- 707 镜像
        private void TickMirror(float dt)
        {
            // 自毁计时（30s 后镜像消失）
            for (int i = _mirrorCopies.Count - 1; i >= 0; i--)
            {
                TimedActor t = _mirrorCopies[i];
                t.Remaining -= dt;
                if (t.Remaining <= 0f)
                {
                    DestroyExtra(t.Actor);
                    _mirrorCopies.RemoveAt(i);
                }
            }

            if (!_mirrorEnabled || _mirrorSpawned) return;
            MonsterActor boss = FindFinalBoss();
            if (boss == null || boss.State == null || boss.State.Dead) return;
            if (boss.State.Hp / boss.State.HpMax <= 0.5f)
            {
                SpawnMirror(boss);
                _mirrorSpawned = true;
            }
        }

        private void SpawnMirror(MonsterActor boss)
        {
            if (_spawner == null || boss == null) return;
            Vector3 pos = boss.transform.position + new Vector3(4f, 0f, 4f);
            MonsterActor copy = _spawner.SpawnCopy(boss, 0.5f, pos, "镜像·" + boss.State.Name);
            if (copy != null)
            {
                _extraActors.Add(copy);
                _mirrorCopies.Add(new TimedActor { Actor = copy, Remaining = 30f });
                Debug.Log("[Affix] 镜像：终 BOSS 50% 血量分裂镜像（50% 属性），30s 后自毁");
            }
        }

        // -------------------------------------------------------- 708 引力异常
        private void TickGravity(float dt)
        {
            if (!_gravityEnabled || _gravityTimer <= 0f) return;
            _gravityTimer -= dt;
            if (_world != null)
            {
                foreach (ActorEntity a in _world.Actors)
                {
                    if (a == null || !a.IsPlayer || a.IsDead) continue;
                    Vector3 np = Vector3.MoveTowards(a.transform.position, _gravityCenter, 3f * dt);
                    a.transform.position = np;
                }
            }
            if (_gravityTimer <= 0f) Debug.Log("[Affix] 引力异常：拉扯结束");
        }

        // -------------------------------------------------------- 712 献祭守卫
        private void SpawnGuardians()
        {
            if (_spawner == null) return;
            Core.MonsterDef def = Core.MonsterTable.Default.Get(3); // 林间山贼 作为祭坛守卫基底
            MonsterActor boss = FindFinalBoss();
            Vector3 center = boss != null ? boss.transform.position : new Vector3(40f, 0f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float ang = i * 2.094f; // 120° 间隔
                Vector3 pos = center + new Vector3(Mathf.Cos(ang) * 8f, 0f, Mathf.Sin(ang) * 8f);
                MonsterActor g = _spawner.SpawnCopy(def, pos, 1f, "祭坛守卫");
                if (g != null) { _guardians.Add(g); _extraActors.Add(g); }
            }
            Debug.Log("[Affix] 献祭：已生成 3 个祭坛守卫；未肃清前终 BOSS 减伤 60%（DamageTakenMul=0.4）");
        }

        private void TickGuardians()
        {
            if (!_sacrificeEnabled) return;
            MonsterActor boss = FindFinalBoss();
            if (boss == null || boss.State == null) return;
            int alive = 0;
            foreach (var g in _guardians) if (g != null && g.State != null && !g.State.Dead) alive++;
            boss.State.DamageTakenMul = alive > 0 ? 0.4f : 1f;
            if (alive == 0 && !_sacrificeCleared)
            {
                _sacrificeCleared = true;
                Debug.Log("[Affix] 献祭：3 祭坛守卫已肃清，终 BOSS 减伤解除（DamageTakenMul=1）");
            }
        }

        // -------------------------------------------------------- 713 光暗交替
        private void TickLightDark(float dt)
        {
            if (!_lightDarkEnabled) return;
            _lightDarkTimer += dt;
            if (_lightDarkTimer >= 20f)
            {
                _lightDarkTimer = 0f;
                _darkPhase = !_darkPhase;
                AffixEffects.DarkPhaseActive = _darkPhase;
                Debug.Log("[Affix] 光暗交替：" + (_darkPhase ? "暗期（玩家伤害 +25%）" : "明期"));
            }
            if (_world != null)
            {
                foreach (ActorEntity a in _world.Actors)
                {
                    if (a == null || !a.IsPlayer || a.IsDead) continue;
                    var p = a as PlayerActor;
                    if (p == null || p.State == null) continue;
                    if (_darkPhase) p.State.Buffs.Apply(Core.BuffId.DarkPhaseDamage, 1.5f, 0.25f);
                    else p.State.Buffs.Remove(Core.BuffId.DarkPhaseDamage);
                }
            }
        }

        // -------------------------------------------------------- 死亡事件：709 蚀魂 / 710 增殖
        private void OnActorDied(int actorId)
        {
            if (_world == null) return;
            ActorEntity a = _world.Get(actorId);
            if (a == null || a.State == null) return;

            if (_soulEnabled && a.IsPlayer)
            {
                _zones.Add(new ErosionZone { Center = a.transform.position, Radius = 5f, Remaining = 10f, DotTimer = 0f });
                Debug.Log("[Affix] 蚀魂：玩家 " + a.State.Name + " 死亡，原地残留腐蚀区 10s");
            }

            if (_prolifEnabled)
            {
                var m = a as MonsterActor;
                if (m != null && !m.State.IsBoss && !m.State.IsElite
                    && !_copyIds.Contains(actorId) && !_splitDoneIds.Contains(actorId))
                {
                    _splitDoneIds.Add(actorId);
                    SpawnSplit(m);
                }
            }
        }

        private void SpawnSplit(MonsterActor source)
        {
            if (_spawner == null || source == null) return;
            for (int i = 0; i < 2; i++)
            {
                MonsterActor c = _spawner.SpawnCopy(source, 0.5f);
                if (c != null)
                {
                    _copyIds.Add(c.ActorId);
                    _extraActors.Add(c);
                }
            }
            Debug.Log("[Affix] 增殖：小怪死亡分裂为 2 个 50% 属性小怪（最多分裂 1 次）");
        }

        // -------------------------------------------------------- 施放事件：714 星序紊乱
        private void OnCastResolved(int casterId, Core.SkillId skill, Core.CastFailReason fail)
        {
            if (!AffixEffects.StarChaosActive) return;
            if (fail != Core.CastFailReason.None) return;
            if (_world == null) return;
            ActorEntity c = _world.Get(casterId);
            if (c == null || !c.IsPlayer) return;
            Core.SkillDef def = Core.SkillTable.Get(skill);
            if (def == null) return;
            float factor = 0.8f + (float)_rng.NextDouble() * 0.4f; // ±20%
            c.State.JitterCooldown(def, factor);
        }

        // -------------------------------------------------------- 机制实现（701–706）
        /// <summary>给全体存活 BOSS 临时乘算攻击力（窗口结束后还原）。</summary>
        private void ApplyTempAtkToBosses(float mul, float window)
        {
            if (_world == null) return;
            int n = 0;
            foreach (ActorEntity a in _world.Actors)
            {
                var m = a as MonsterActor;
                if (m == null || m.State == null || m.State.Dead || !m.State.IsBoss) continue;
                m.State.WeaponDps *= mul;
                _tempBuffs.Add(new TempBuff { Boss = m, Factor = mul, Remaining = window });
                n++;
            }
            if (n > 0) Debug.Log("[Affix] 卢恩狂暴/血月：对 " + n + " 个 BOSS 应用临时攻击乘算 ×" + mul);
        }

        private void ProcessTempBuffs(float dt)
        {
            for (int i = _tempBuffs.Count - 1; i >= 0; i--)
            {
                TempBuff t = _tempBuffs[i];
                t.Remaining -= dt;
                if (t.Remaining <= 0f)
                {
                    if (t.Boss != null && t.Boss.State != null) t.Boss.State.WeaponDps /= t.Factor;
                    _tempBuffs.RemoveAt(i);
                }
            }
        }

        private void LightningStrike()
        {
            ActorEntity target = PickRandomPlayer();
            if (target == null) return;
            float dmg = Mathf.Max(1f, target.State.HpMax * 0.12f);
            target.State.ApplyDamage(dmg);
            Debug.Log("[Affix] 东荒雷罚：落雷命中 " + target.State.Name + " 伤害=" + dmg.ToString("F0"));
        }

        private void SpawnErosionZone()
        {
            ActorEntity p = PickRandomPlayer();
            if (p == null) return;
            _zones.Add(new ErosionZone
            {
                Center = p.transform.position,
                Radius = 6f,
                Remaining = 8f,
                DotTimer = 0f
            });
            Debug.Log("[Affix] 归墟侵蚀：在 " + p.State.Name + " 附近生成腐蚀区（6m / 8s）");
        }

        private void ProcessZones(float dt)
        {
            for (int i = _zones.Count - 1; i >= 0; i--)
            {
                ErosionZone z = _zones[i];
                z.Remaining -= dt;
                z.DotTimer -= dt;
                if (z.DotTimer <= 0f)
                {
                    z.DotTimer = 1f; // 每 1s 一跳 DoT
                    if (_world != null)
                    {
                        foreach (ActorEntity a in _world.Actors)
                        {
                            if (a == null || !a.IsPlayer || a.IsDead) continue;
                            if ((a.transform.position - z.Center).sqrMagnitude <= z.Radius * z.Radius)
                                a.State.ApplyDamage(a.State.HpMax * 0.015f);
                        }
                    }
                }
                if (z.Remaining <= 0f) _zones.RemoveAt(i);
            }
        }

        private void GrantBossShield()
        {
            if (_world == null) return;
            int n = 0;
            foreach (ActorEntity a in _world.Actors)
            {
                var m = a as MonsterActor;
                if (m == null || m.State == null || m.State.Dead || !m.State.IsBoss) continue;
                m.State.Shield += m.State.HpMax * 0.2f;
                n++;
            }
            if (n > 0) Debug.Log("[Affix] 石肤：为 " + n + " 个 BOSS 注入 20% 最大生命吸收护盾");
        }

        private void SilenceAllPlayers(float duration)
        {
            if (_world == null) return;
            int n = 0;
            foreach (ActorEntity a in _world.Actors)
            {
                if (a == null || !a.IsPlayer || a.IsDead) continue;
                a.State.Control = Core.ControlKind.Silence;
                a.State.ControlRemaining = duration;
                n++;
            }
            if (n > 0) Debug.Log("[Affix] 静默：全场 " + n + " 名玩家禁技能 " + duration + "s");
        }

        private void ApplyRandomPlayerDamage(float pctOfMax, string logMsg)
        {
            ActorEntity p = PickRandomPlayer();
            if (p == null) return;
            p.State.ApplyDamage(p.State.HpMax * pctOfMax);
            Debug.Log(logMsg + " 轻微伤害命中 " + p.State.Name);
        }

        private ActorEntity PickRandomPlayer()
        {
            if (_world == null) return null;
            var players = new List<ActorEntity>();
            foreach (ActorEntity a in _world.Actors)
                if (a != null && a.IsPlayer && !a.IsDead) players.Add(a);
            if (players.Count == 0) return null;
            return players[_rng.Next(players.Count)];
        }

        private Vector3 CentroidOfPlayers()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            if (_world != null)
                foreach (ActorEntity a in _world.Actors)
                    if (a != null && a.IsPlayer && !a.IsDead) { sum += a.transform.position; n++; }
            return n > 0 ? sum / n : Vector3.zero;
        }

        /// <summary>取当前场上 HpMax 最大的 BOSS（即「终 BOSS」），用于 707 镜像 / 712 守卫 / 711 等定位。
        /// HpMax 相同时取 MonsterDefId 最大者（如 R20-1 的 616 优先于 611–615），保证终 BOSS 语义唯一。</summary>
        private MonsterActor FindFinalBoss()
        {
            MonsterActor best = null; float bestHp = -1f; int bestId = -1;
            if (_world == null) return null;
            foreach (ActorEntity a in _world.Actors)
            {
                var m = a as MonsterActor;
                if (m == null || m.State == null || !m.State.IsBoss) continue;
                if (m.State.HpMax > bestHp || (m.State.HpMax == bestHp && m.MonsterDefId > bestId))
                {
                    bestHp = m.State.HpMax; bestId = m.MonsterDefId; best = m;
                }
            }
            return best;
        }

        private void DestroyExtra(MonsterActor m)
        {
            if (m == null) return;
            if (_world != null) _world.Unregister(m);
            if (m.gameObject != null) Destroy(m.gameObject);
            _extraActors.Remove(m);
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.OnActorDied -= OnActorDied;
                _world.OnCastResolved -= OnCastResolved;
            }
            // 清理本词缀生成的所有额外单位（镜像 / 祭坛守卫 / 增殖分裂体），避免退出副本后泄漏
            foreach (var m in _extraActors)
            {
                if (m != null)
                {
                    if (_world != null) _world.Unregister(m);
                    if (m.gameObject != null) Destroy(m.gameObject);
                }
            }
            _extraActors.Clear();
            _guardians.Clear();
            _mirrorCopies.Clear();
            _copyIds.Clear();
            _splitDoneIds.Clear();
            AffixEffects.Reset();
        }

        // -------------------------------------------------------- 数据结构
        private sealed class ActiveAffix
        {
            public Core.AffixDef Def;
            public float Timer;
            public float Interval;
        }

        private sealed class TempBuff
        {
            public MonsterActor Boss;
            public float Factor;
            public float Remaining;
        }

        private sealed class ErosionZone
        {
            public Vector3 Center;
            public float Radius;
            public float Remaining;
            public float DotTimer;
        }

        private sealed class TimedActor
        {
            public MonsterActor Actor;
            public float Remaining;
        }
    }
}
