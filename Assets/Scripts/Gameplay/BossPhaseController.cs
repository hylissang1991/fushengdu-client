// 《浮生渡》表现层 · BOSS 阶段机（数据驱动）
// 挂到 BOSS 的 MonsterActor 上，由 DungeonSpawner.SpawnBosses 在生成后挂载并 Init。
// 每帧计算 pct = Hp/HpMax，跨越阈值即进入新阶段并一次性应用效果（增益 / 护盾 / 召唤 / 分场 AOE）。
// 双体 / 合体：通过 LinkedBossIds 做联动（任一阵亡 → 其余进入 Enrage ×1.3，简化近似，详见汇报）。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// BOSS 阶段机。解释执行 Core.BossPhaseProfile 中定义的阶段序列。
    /// </summary>
    public sealed class BossPhaseController : MonoBehaviour
    {
        private MonsterActor _boss;
        private Core.BossPhaseProfile _profile;
        private DungeonSpawner _spawner;
        private CombatWorld _world;
        private SharedHpGroup _sharedGroup;   // 双体/合体真实共享血量分组（null = 单体）

        private int _currentIndex = -1;
        private float _aoeTimer;
        private readonly List<MonsterActor> _adds = new List<MonsterActor>();

        // 进本时记录的基准属性：阶段机按「绝对乘算」应用，避免跨阶段复利（P2/P3 分别 = base×1.15 / base×1.30，而非 base×1.15×1.30）。
        private float _baseAtk;
        private float _baseArmor;

        /// <summary>阶段切换事件（bossId, label），供 UI / 日志订阅（Slice 2b 接 UI）。</summary>
        public event System.Action<int, string> OnBossPhaseChanged;

        /// <summary>当前阶段标签（UI 读取）。</summary>
        public string CurrentPhaseLabel
        {
            get
            {
                if (_profile == null || _currentIndex < 0 || _currentIndex >= _profile.Phases.Count) return "";
                return _profile.Phases[_currentIndex].Label;
            }
        }

        public int CurrentPhaseIndex => _currentIndex;

        public void Init(Core.BossPhaseProfile profile, DungeonSpawner spawner, CombatWorld world, SharedHpGroup sharedGroup = null)
        {
            _profile = profile;
            _spawner = spawner;
            _world = world;
            _sharedGroup = sharedGroup;
            _boss = GetComponent<MonsterActor>();
            if (_boss != null && _boss.State != null)
            {
                _baseAtk = _boss.State.WeaponDps;
                _baseArmor = _boss.State.Armor;
            }
        }

        /// <summary>双体 / 合体：绑定真实共享血量分组（由 DungeonSpawner.BindSharedHpGroups 调用）。</summary>
        public void BindSharedHpGroup(SharedHpGroup group)
        {
            _sharedGroup = group;
        }

        private void Update()
        {
            if (_boss == null || _profile == null || _world == null) return;
            Core.ActorState st = _boss.State;
            if (st == null || st.Dead) return;

            float dt = Time.deltaTime;
            float pct = st.HpMax > 0f ? st.Hp / st.HpMax : 1f;

            int idx = ResolvePhaseIndex(pct);
            if (idx > _currentIndex)
            {
                // 通常一次跨一级；逐层应用保证不漏阶段
                for (int i = _currentIndex + 1; i <= idx; i++) EnterPhase(i);
                _currentIndex = idx;
            }

            if (_sharedGroup != null) UpdateSharedHp();
            UpdatePhaseMechanics(dt);
        }

        /// <summary>取最大的 i 使得 pct ≤ Phases[i].HpThresholdPct（阈值降序排列）。</summary>
        private int ResolvePhaseIndex(float pct)
        {
            int idx = 0;
            for (int i = 0; i < _profile.Phases.Count; i++)
            {
                if (pct <= _profile.Phases[i].HpThresholdPct) idx = i;
            }
            return idx;
        }

        private void EnterPhase(int i)
        {
            Core.BossPhase ph = _profile.Phases[i];
            Core.ActorState st = _boss.State;

            if (_baseAtk <= 0f) _baseAtk = st.WeaponDps;
            if (_baseArmor <= 0f) _baseArmor = st.Armor;
            if (ph.AtkMul != 1f) st.WeaponDps = _baseAtk * ph.AtkMul;
            if (ph.ArmorMul != 1f) st.Armor = _baseArmor * ph.ArmorMul;
            if (ph.GrantShield) st.Shield += st.HpMax * 0.2f;

            if (ph.SpawnAddsDefIds != null && ph.SpawnAddsDefIds.Length > 0 && _spawner != null)
            {
                List<MonsterActor> spawned = _spawner.SpawnAddsNear(_boss, ph.SpawnAddsDefIds, ph.SpawnAddsCounts);
                _adds.AddRange(spawned);
            }

            Debug.Log("[BossPhase] " + st.Name + "(id=" + _boss.MonsterDefId + ") 进入 " + ph.Label +
                      " | 阈值=" + (ph.HpThresholdPct * 100f).ToString("F0") + "%" +
                      (ph.AtkMul != 1f ? " Atk×" + ph.AtkMul : "") +
                      (ph.ArmorMul != 1f ? " Armor×" + ph.ArmorMul : "") +
                      (ph.GrantShield ? " [护盾+20%MaxHp]" : "") +
                      (ph.ArenaSplit ? " [分场AOE]" : "") +
                      (ph.SpawnAddsDefIds != null && ph.SpawnAddsDefIds.Length > 0 ? " [召唤小怪]" : ""));

            if (OnBossPhaseChanged != null) OnBossPhaseChanged(_boss.MonsterDefId, ph.Label);
        }

        private void UpdatePhaseMechanics(float dt)
        {
            Core.BossPhase ph = _profile.Phases[_currentIndex];
            if (ph.AoeDamage <= 0f) return;
            _aoeTimer += dt;
            // 711 时滞：BOSS 技能预警/周期 AOE 间隔缩短（反应窗口缩短）
            float interval = ph.AoeInterval * AffixEffects.TimeLagIntervalMul;
            if (_aoeTimer >= interval)
            {
                _aoeTimer = 0f;
                ApplyPeriodicAoe(ph);
            }
        }

        /// <summary>周期 AOE / 分场 DoT（简化：对全体存活玩家造成伤害 + 日志）。</summary>
        private void ApplyPeriodicAoe(Core.BossPhase ph)
        {
            if (_world == null) return;
            float dmg = ph.AoeDamage;
            int hit = 0;
            foreach (ActorEntity a in _world.Actors)
            {
                if (a == null || !a.IsPlayer || a.IsDead) continue;
                a.State.ApplyDamage(dmg);
                hit++;
            }
            Debug.Log("[BossPhase] " + _boss.State.Name + " 「" + ph.Label + "」触发" +
                      (ph.ArenaSplit ? "分场AOE" : "周期AOE") + " 伤害=" + dmg + " 命中玩家=" + hit);
        }

        /// <summary>
        /// 双体 / 合体真实共享血量：每帧把各成员显示血量同步到共享池比例（伤害已在 SharedHpGroup.Sink 中扣减统一池）。
        /// 单一 BOSS（_sharedGroup 为 null）跳过此步。
        /// </summary>
        private void UpdateSharedHp()
        {
            if (_sharedGroup != null && _sharedGroup.Active) _sharedGroup.Sync();
        }

        private MonsterActor FindBossById(int id)
        {
            if (_world == null) return null;
            foreach (ActorEntity a in _world.Actors)
            {
                MonsterActor m = a as MonsterActor;
                if (m != null && m.MonsterDefId == id) return m;
            }
            return null;
        }

        private void OnDestroy()
        {
            // 清理本控制器召唤的小怪，避免退出副本后泄漏 GameObject
            for (int i = 0; i < _adds.Count; i++)
            {
                MonsterActor a = _adds[i];
                if (a != null)
                {
                    if (_world != null) _world.Unregister(a);
                    if (a.gameObject != null) Object.Destroy(a.gameObject);
                }
            }
            _adds.Clear();
        }
    }
}
