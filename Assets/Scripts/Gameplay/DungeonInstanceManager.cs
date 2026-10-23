// 《浮生渡》表现层 · 副本实例编排（Slice 1 核心）
// 单例 MonoBehaviour：进入/退出副本、校验、设置掉落上下文、刷 BOSS、发武器碎片。
// 不引用 FishNet：复用 CombatWorld / LocalPlayerLocator / Core 数据表，联机部分本期降级为离线单人。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 副本实例管理器。负责把玩家从主城「拉进」副本实例，并在退出时还原。
    /// 本期为离线单人可玩闭环（验证路径）；联机主机的场景实例与权威端掷骰由后续 Slice 补全。
    /// </summary>
    public sealed class DungeonInstanceManager : MonoBehaviour
    {
        public static DungeonInstanceManager Instance { get; private set; }

        /// <summary>
        /// 结算回调钩子（M2）。退出副本且有掉落时触发，由 UI 层注册（Gameplay 不引用 UI 程序集，
        /// 故用委托解耦：UI 启动时挂 `DungeonInstanceManager.OnSettlement = DungeonSettlementPanel.Show`）。
        /// </summary>
        public static System.Action<List<int>> OnSettlement;

        private int _activeDungeonId;
        private bool _active;
        private DungeonSpawner _spawner;
        private AffixRuntime _affixRuntime;
        private readonly List<MonsterActor> _spawned = new List<MonsterActor>();
        private readonly List<int> _runDrops = new List<int>();   // M2/M6：本场掉落 itemDefId 收集，供结算面板

        // 本期降级方案：在当前场景内生成临时竞技场区域（不加载独立 DungeonArena 场景，详见汇报）。
        private readonly Vector3 _arenaAnchor = new Vector3(40f, 0f, 0f);

        private void Awake()
        {
            Instance = this;
        }

        public bool IsActive => _active;

        /// <summary>当前已生成的 BOSS 数量（调试面板 / 验证用）。</summary>
        public int BossCount => _spawned.Count;

        /// <summary>取第 index 个已生成 BOSS 实例（验证用；越界返回 null）。</summary>
        public MonsterActor GetSpawnedBoss(int index) => (index >= 0 && index < _spawned.Count) ? _spawned[index] : null;

        /// <summary>当前生效的归墟词缀列表（验证用；未进词缀本返回空表）。</summary>
        public System.Collections.Generic.List<Core.AffixDef> GetActiveAffixes()
            => _affixRuntime != null ? _affixRuntime.GetActiveAffixes() : new System.Collections.Generic.List<Core.AffixDef>();

        /// <summary>
        /// 进入副本。partyMemberIds 传 null/空时自动把本地玩家作为唯一参战者（离线单人）。
        /// </summary>
        public void EnterDungeon(int dungeonId, int[] partyMemberIds)
            => EnterDungeon(dungeonId, partyMemberIds, null);

        /// <summary>
        /// 进入副本（可携带入口面板「公示」抽中的 2 个词缀，保证预览 == 实际；null 则由进本时自行抽取）。
        /// </summary>
        public void EnterDungeon(int dungeonId, int[] partyMemberIds, List<Core.AffixDef> preDrawnAffixes)
        {
            if (_active) ExitDungeon();

            // 进本先清零归墟词缀跨切状态，避免上一场残留（时滞/光暗/星序）污染本场
            AffixEffects.Reset();

            Core.DungeonDef def = Core.DungeonTable.Default.Get(dungeonId);
            if (def == null) { Debug.LogError("[Dungeon] 未知副本 id=" + dungeonId); return; }

            // M6：进入时加载存档并做周重置判定
            var store = DungeonProgressStore.Instance;
            if (store != null) { store.Load(); store.TryResetIfNewWeek(); }

            ValidateEntry(def, partyMemberIds);

            int[] bossIds = ResolveBossIds(def);
            SetupLootContext(def, bossIds, partyMemberIds);

            if (CombatWorld.Instance != null)
            {
                _spawner = new DungeonSpawner(CombatWorld.Instance);
                _spawned.AddRange(_spawner.SpawnBosses(bossIds, _arenaAnchor));
            }

            // M5 词缀：优先使用入口面板「公示」抽中的 2 个（保证预览==实际）；否则进本时自行抽取
            if (def.AffixPool != null && def.AffixPool.Length > 0 && CombatWorld.Instance != null)
            {
                _affixRuntime = gameObject.AddComponent<AffixRuntime>();
                if (preDrawnAffixes != null && preDrawnAffixes.Count > 0)
                    _affixRuntime.Init(preDrawnAffixes, CombatWorld.Instance, _spawner);
                else
                    _affixRuntime.Init(def.AffixPool, 99, CombatWorld.Instance, _spawner);
            }

            // M2/M6：收集本场掉落供结算面板展示
            _runDrops.Clear();
            if (CombatWorld.Instance != null) CombatWorld.Instance.OnItemGranted += OnRunDrop;

            TeleportLocalPlayerToArena();

            _activeDungeonId = dungeonId;
            _active = true;
            Debug.Log("[Dungeon] 进入副本 id=" + dungeonId + " (" + def.Name + ")，BOSS 数=" + bossIds.Length + "，规则=" + def.LootRuleKey);
        }

        /// <summary>退出副本：清理 BOSS、清空掉落上下文、传送回主城复活点、存档、展示结算。</summary>
        public void ExitDungeon()
        {
            if (CombatWorld.Instance != null) CombatWorld.Instance.OnItemGranted -= OnRunDrop;
            if (_spawner != null) _spawner.DespawnAll(_spawned);
            _spawned.Clear();
            if (_affixRuntime != null) { Destroy(_affixRuntime); _affixRuntime = null; }
            if (CombatWorld.Instance != null) CombatWorld.Instance.ActiveDungeonLoot = null;

            // M6：退出时存档
            var store = DungeonProgressStore.Instance;
            if (store != null) store.Save();

            // M2：展示结算面板（若有掉落）。通过委托钩子解耦 UI 程序集（Gameplay 不引用 UI）。
            if (_runDrops.Count > 0 && OnSettlement != null)
                OnSettlement(new List<int>(_runDrops));

            ReturnLocalPlayerToHub();
            _active = false;
            _activeDungeonId = 0;
            Debug.Log("[Dungeon] 已退出副本，传送回主城；本场掉落 " + _runDrops.Count + " 件");
        }

        private void OnRunDrop(int playerId, int itemDefId) { _runDrops.Add(itemDefId); }

        /// <summary>本场掉落的 itemDefId 列表（结算面板 / 验证用）。</summary>
        public List<int> GetRunDrops() => new List<int>(_runDrops);

        /// <summary>某副本本周已用顶级神话数（验证用）。</summary>
        public int GetWeeklyCapUsed(int dungeonId)
            => DungeonProgressStore.Instance != null ? DungeonProgressStore.Instance.WeeklyCapUsed(dungeonId) : 0;

        /// <summary>解析本次要刷的 BOSS：固定/团本用 BossIds；工厂本用 BossPoolRef 取前 3 只作为代表。</summary>
        private static int[] ResolveBossIds(Core.DungeonDef def)
        {
            if (def.BossIds != null && def.BossIds.Length > 0) return def.BossIds;
            if (def.BossPoolRef != null && def.BossPoolRef.Length > 0)
            {
                int n = Mathf.Min(3, def.BossPoolRef.Length);
                int[] ids = new int[n];
                for (int i = 0; i < n; i++) ids[i] = def.BossPoolRef[i];
                return ids;
            }
            return new int[0];
        }

        private void SetupLootContext(Core.DungeonDef def, int[] bossIds, int[] party)
        {
            var ctx = new ActiveDungeonLootContext
            {
                DungeonId = def.Id,
                RuleKey = def.LootRuleKey,
                IsRaid = def.Kind == Core.DungeonKind.Raid10 || def.Kind == Core.DungeonKind.Raid20,
                BossIds = bossIds ?? new int[0],
                Participants = new List<int>()
            };
            if (party != null) ctx.Participants.AddRange(party);
            if (ctx.Participants.Count == 0 && LocalPlayerLocator.Current != null)
                ctx.Participants.Add(LocalPlayerLocator.Current.ActorId);

            if (CombatWorld.Instance != null) CombatWorld.Instance.ActiveDungeonLoot = ctx;
        }

        /// <summary>校验 RequiredLevel / MinPlayers / MaxPlayers；离线单人放宽 MinPlayers，仅警告不阻断。</summary>
        private static void ValidateEntry(Core.DungeonDef def, int[] party)
        {
            int members = party != null ? party.Length : (LocalPlayerLocator.Current != null ? 1 : 0);
            if (members < def.MinPlayers)
                Debug.LogWarning("[Dungeon] 参战人数 " + members + " < MinPlayers=" + def.MinPlayers + "（离线单人放宽，仅警告）");
            if (members > def.MaxPlayers)
                Debug.LogWarning("[Dungeon] 参战人数 " + members + " > MaxPlayers=" + def.MaxPlayers);
        }

        private void TeleportLocalPlayerToArena()
        {
            PlayerActor p = LocalPlayerLocator.Current;
            if (p != null) p.Teleport(_arenaAnchor + new Vector3(0f, 0f, 8f));
        }

        private void ReturnLocalPlayerToHub()
        {
            PlayerActor p = LocalPlayerLocator.Current;
            Vector3 hub = CombatWorld.Instance != null ? CombatWorld.Instance.RespawnPoint : new Vector3(0f, 1f, 0f);
            if (p != null) p.Teleport(hub);
        }

        /// <summary>调试用：直接击杀第 index 个已生成 BOSS（离线验证掉落）。</summary>
        public void DebugKillBoss(int index)
        {
            if (index < 0 || index >= _spawned.Count) return;
            MonsterActor m = _spawned[index];
            if (m == null) return;
            CombatWorld.Instance?.DebugKill(m);
        }

        /// <summary>调试用：把第 index 个已生成 BOSS 的血量直接压到指定百分比（离线验证阶段机阈值切换）。</summary>
        public void DebugSetBossHpPct(int index, float pct)
        {
            if (index < 0 || index >= _spawned.Count) return;
            MonsterActor m = _spawned[index];
            if (m == null || m.State == null) return;
            pct = Mathf.Clamp(pct, 0f, 1f);
            m.State.Hp = m.State.HpMax * pct;
            Debug.Log("[Dungeon] DebugSetBossHpPct boss#" + index + "(" + m.State.Name + ") -> " + (pct * 100f).ToString("F0") + "%");
        }

        /// <summary>调试用：退回主城并清场。</summary>
        public void DebugExit()
        {
            if (_active) ExitDungeon();
        }
    }
}
