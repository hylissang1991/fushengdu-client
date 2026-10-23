// 《浮生渡》表现层 · 副本阶段机 / 归墟词缀 无头验证（Slice 2a 验收用）
// 与 PlaytestHarness 同构：不依赖任何人工点击，用命令行参数 -fdDungeonVerify 触发，
// 按脚本驱动真实业务链路（进本 → 压血量过阈值 → 观察阶段切换；进工厂本 → 校验词缀抽 2 + 互斥）。
// 通过/失败均以 [自检] ... OK/FAIL 结尾写入日志；结束后自动退出进程。
// 用法（Windows 独立播放器）：
//   Fushengdu.exe -fdSolo -fdDungeonVerify -fdQuit
using System.Collections;
using System.Collections.Generic;
using Fushengdu.Core;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>命令行无头验证：BOSS 阶段机（M3）+ 归墟词缀（M5）。</summary>
    public sealed class DungeonVerifyHarness : MonoBehaviour
    {
        private const string Tag = "[自检]";

        private readonly List<string> _logBuffer = new List<string>();
        private int _pass;
        private int _fail;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!HasFlag("-fdDungeonVerify")) return;
            var go = new GameObject("DungeonVerifyHarness");
            go.AddComponent<DungeonVerifyHarness>();
        }

        private static bool HasFlag(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void OnEnable() { Application.logMessageReceived += OnLog; }
        private void OnDisable() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string cond, string trace, LogType type) { _logBuffer.Add(cond); }

        private void Start() { StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            Log("=== 副本阶段机 / 归墟词缀 无头验证开始 ===");

            // 等运行环境就绪（单机模式创建 CombatWorld + 本地玩家需要几帧）
            float t = 0f;
            while ((CombatWorld.Instance == null || LocalPlayerLocator.Current == null) && t < 15f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (CombatWorld.Instance == null || LocalPlayerLocator.Current == null)
            {
                Record(false, "运行环境未就绪（缺少 CombatWorld / 本地玩家）");
                Finish();
                yield break;
            }

            if (DungeonInstanceManager.Instance == null)
                new GameObject("DungeonInstanceManager").AddComponent<DungeonInstanceManager>();
            DungeonInstanceManager mgr = DungeonInstanceManager.Instance;

            // M6 进度存档单例（无头验证需显式创建；游戏内由 DebugDungeonPanel 保障）
            if (DungeonProgressStore.Instance == null)
                new GameObject("DungeonProgressStore").AddComponent<DungeonProgressStore>();
            // 无头模式不弹结算面板（避免无关 UI 噪声），仅验证数据链路
            DungeonInstanceManager.OnSettlement = null;

            yield return VerifyRaid(mgr, 3001, "605", 3);   // R10-1 终 BOSS：3 阶段
            yield return VerifyRaid(mgr, 4001, "616", 4);   // R20-1 终 BOSS：4 阶段
            yield return VerifyFactory(mgr, 1001);          // 工厂本：词缀抽 2 + 互斥重掷
            yield return VerifyLoot(mgr, 2001);             // 固定本：击杀 BOSS 掉落回归
            yield return VerifyProgress(mgr);               // M6：周帽 / 首通持久化 / 周一重置
            yield return VerifyAffixMechanics(mgr);         // ②：707–714 进阶机制不放异常 + 关键行为生效

            Finish();
        }

        // ----------------------------------------------------- M3：BOSS 阶段机
        private IEnumerator VerifyRaid(DungeonInstanceManager mgr, int dungeonId, string expectBossId, int expectPhases)
        {
            mgr.EnterDungeon(dungeonId, null);
            yield return new WaitForSeconds(1.0f);

            int last = mgr.BossCount - 1;
            MonsterActor boss = mgr.GetSpawnedBoss(last);
            BossPhaseController ctrl = boss != null ? boss.GetComponent<BossPhaseController>() : null;

            if (boss == null || ctrl == null)
            {
                Record(false, dungeonId + " 终BOSS(" + expectBossId + ") 未生成或未挂阶段机（BossCount=" + mgr.BossCount + "）");
                mgr.ExitDungeon();
                yield return new WaitForSeconds(0.3f);
                yield break;
            }

            // 记录阈值点：605 = [1.0,0.66,0.33]，616 = [1.0,0.75,0.50,0.25]
            float[] thresholds = expectPhases == 3
                ? new[] { 1.0f, 0.66f, 0.33f }
                : new[] { 1.0f, 0.75f, 0.50f, 0.25f };

            var labels = new List<string> { ctrl.CurrentPhaseLabel };
            for (int i = 1; i < thresholds.Length; i++)
            {
                mgr.DebugSetBossHpPct(last, thresholds[i]);
                yield return new WaitForSeconds(0.4f);
                labels.Add(ctrl.CurrentPhaseLabel);
            }

            // 每个阶段标签须包含 P1..Pn 且依次递增
            bool ok = labels.Count == expectPhases;
            for (int i = 0; i < labels.Count && ok; i++)
                ok = labels[i] != null && labels[i].Contains("P" + (i + 1));

            Record(ok, dungeonId + " 终BOSS(" + expectBossId + ") 阶段切换(" + expectPhases + ")："
                       + string.Join(" → ", labels.ToArray()));

            mgr.ExitDungeon();
            yield return new WaitForSeconds(0.3f);
        }

        // ----------------------------------------------------- M5：归墟词缀
        private IEnumerator VerifyFactory(DungeonInstanceManager mgr, int dungeonId)
        {
            mgr.EnterDungeon(dungeonId, null);
            yield return new WaitForSeconds(0.5f);

            List<AffixDef> affixes = mgr.GetActiveAffixes();
            bool countOk = affixes != null && affixes.Count == 2;

            // 互斥重掷必须保证：抽出的 2 条不落在同一互斥组
            bool mutexOk = true;
            if (affixes != null && affixes.Count == 2 && affixes[0].MutexGroup != 0)
                mutexOk = affixes[0].MutexGroup != affixes[1].MutexGroup;

            // 词缀抽取日志须出现
            bool logOk = false;
            foreach (string l in _logBuffer)
                if (l.Contains("[Affix] 本次归墟侵蚀层词缀")) { logOk = true; break; }

            bool ok = countOk && mutexOk && logOk;
            string detail = affixes != null
                ? string.Join(" + ", affixes.ConvertAll(a => a.Name + "(组" + a.MutexGroup + ")"))
                : "null";
            Record(ok, dungeonId + " 词缀抽2=" + countOk + " 互斥重掷生效=" + mutexOk + " 日志=" + logOk + " → " + detail);

            mgr.ExitDungeon();
            yield return new WaitForSeconds(0.3f);
        }

        // ----------------------------------------------------- 回归：2001 固定本掉落
        private bool _lootGranted;

        private IEnumerator VerifyLoot(DungeonInstanceManager mgr, int dungeonId)
        {
            mgr.EnterDungeon(dungeonId, null);
            yield return new WaitForSeconds(1.0f);

            _lootGranted = false;
            CombatWorld.Instance.OnItemGranted += OnLootGranted;
            // 击杀最后一个（终）BOSS，确保走副本掉落规则 D-06
            mgr.DebugKillBoss(mgr.BossCount - 1);
            yield return new WaitForSeconds(0.6f);
            CombatWorld.Instance.OnItemGranted -= OnLootGranted;

            Record(_lootGranted, dungeonId + " 击杀终BOSS掉落发放：" + (_lootGranted ? "已触发" : "未触发"));
            mgr.ExitDungeon();
            yield return new WaitForSeconds(0.3f);
        }

        private void OnLootGranted(int playerId, int itemDefId)
        {
            _lootGranted = true;
            Log("掉落发放：player=" + playerId + " item=" + itemDefId);
        }

        // ----------------------------------------------------- M6：进度存档（周帽 / 首通 / 周一重置）
        private IEnumerator VerifyProgress(DungeonInstanceManager mgr)
        {
            if (DungeonProgressStore.Instance == null)
                new GameObject("DungeonProgressStore").AddComponent<DungeonProgressStore>();

            // 20 人团本（D-26-myth）：终 BOSS 团末保底必出 1 件最高级神话 → 周帽确定性 +1
            mgr.EnterDungeon(4001, null);
            yield return new WaitForSeconds(1.0f);

            int usedBefore = mgr.GetWeeklyCapUsed(4001);
            bool firstBefore = DungeonProgressStore.Instance.IsFirstCleared(4001);

            mgr.DebugKillBoss(mgr.BossCount - 1);   // 击杀终 BOSS（末位）
            yield return new WaitForSeconds(0.6f);

            int usedAfter = mgr.GetWeeklyCapUsed(4001);
            bool firstAfter = DungeonProgressStore.Instance.IsFirstCleared(4001);
            bool ok1 = usedAfter >= 1 && firstAfter && usedAfter > usedBefore;
            Record(ok1, "M6 4001 终BOSS：周帽 " + usedBefore + "→" + usedAfter + "，首通=" + firstAfter);

            mgr.ExitDungeon();                       // 落盘
            yield return new WaitForSeconds(0.3f);

            // 退出重进：首通与周帽应从 JSON 文件回读持久化
            mgr.EnterDungeon(4001, null);
            yield return new WaitForSeconds(0.6f);
            bool persisted = DungeonProgressStore.Instance.IsFirstCleared(4001)
                             && mgr.GetWeeklyCapUsed(4001) >= 1;
            Record(persisted, "M6 退出重进：JSON 持久化（首通=" + DungeonProgressStore.Instance.IsFirstCleared(4001)
                               + "，周帽=" + mgr.GetWeeklyCapUsed(4001) + "）=" + persisted);
            mgr.ExitDungeon();
            yield return new WaitForSeconds(0.3f);

            // 调试触发「周一重置」：周帽清零、首通保留
            DungeonProgressStore.Instance.DebugSimulateNewWeek();
            bool cleared = mgr.GetWeeklyCapUsed(4001) == 0
                       && DungeonProgressStore.Instance.IsFirstCleared(4001);
            Record(cleared, "M6 周一重置：周帽清零=" + (mgr.GetWeeklyCapUsed(4001) == 0)
                               + "，首通保留=" + DungeonProgressStore.Instance.IsFirstCleared(4001));
        }

        // ----------------------------------------------------- ②：707–714 进阶机制冒烟
        // 逐条强制抽中 707..714（搭配 701 占位第二条），进 R20-1（含共享血量 BOSS 与终 BOSS），
        // 确认 Init / 每帧 Tick / 事件订阅 / 退出清理 全程不抛异常；并对 712（守卫生成）、707（镜像分裂）做行为校验。
        private IEnumerator VerifyAffixMechanics(DungeonInstanceManager mgr)
        {
            bool allOk = true;
            var sb = new System.Text.StringBuilder();
            for (int id = 707; id <= 714; id++)
            {
                int before = _logBuffer.Count;
                var pre = new List<AffixDef>
                {
                    Core.AffixTable.Default.Get(id),
                    Core.AffixTable.Default.Get(701)
                };
                // 工厂本 1001 带 AffixPool，才能走 preDrawn 公示路径真正实例化 AffixRuntime
                mgr.EnterDungeon(1001, null, pre);
                yield return new WaitForSeconds(1.0f);

                // 707 镜像：把终 BOSS 压到 45% 触发分裂（全部 BOSS 都压低，确保 FindFinalBoss 命中的那个 ≤50%）
                if (id == 707)
                {
                    for (int b = 0; b < mgr.BossCount; b++) mgr.DebugSetBossHpPct(b, 0.45f);
                    yield return new WaitForSeconds(0.5f);
                }

                // 退出前扫描本次是否出现新的异常
                bool threw = false;
                for (int i = before; i < _logBuffer.Count; i++)
                {
                    string l = _logBuffer[i];
                    if (l.Contains("Exception") || l.Contains("NullReference"))
                    {
                        threw = true;
                        sb.Append(" [" + id + " 异常:" + l + "]");
                        break;
                    }
                }

                // 行为校验
                bool behavior = true;
                string behaviorNote = "";
                if (id == 712)
                {
                    // 献祭：应生成 3 个祭坛守卫（世界内怪物数应大于进本时的 BOSS 数）
                    int guardians = 0;
                    var actors = CombatWorld.Instance != null ? CombatWorld.Instance.Actors : null;
                    if (actors != null)
                        foreach (ActorEntity a in actors)
                        {
                            var m = a as MonsterActor;
                            if (m != null && m.State != null && m.State.Name != null && m.State.Name.Contains("祭坛守卫"))
                                guardians++;
                        }
                    behavior = guardians >= 3;
                    behaviorNote = "守卫=" + guardians;
                }
                else if (id == 707)
                {
                    // 镜像：应分裂出名称含「镜像」的单位
                    bool mirror = false;
                    var actors = CombatWorld.Instance != null ? CombatWorld.Instance.Actors : null;
                    if (actors != null)
                        foreach (ActorEntity a in actors)
                        {
                            var m = a as MonsterActor;
                            if (m != null && m.State != null && m.State.Name != null && m.State.Name.Contains("镜像"))
                                mirror = true;
                        }
                    behavior = mirror;
                    behaviorNote = "镜像=" + (mirror ? "已分裂" : "未分裂");
                }

                bool ok = !threw && behavior;
                if (!ok) allOk = false;
                Record(ok, "②词缀 " + id + " 冒烟：" + (threw ? "异常" : "无异常") + " " + behaviorNote);
                mgr.ExitDungeon();
                yield return new WaitForSeconds(0.4f);
            }
            if (!allOk) Record(false, "② 707–714 进阶机制存在失败项" + sb);
        }

        // ----------------------------------------------------- 工具
        private void Record(bool ok, string msg)
        {
            if (ok) _pass++; else _fail++;
            Log(msg + " —— " + (ok ? "OK" : "FAIL"));
        }

        private void Finish()
        {
            Log("=== 副本阶段机 / 归墟词缀 无头验证结束：通过 " + _pass + " / 失败 " + _fail + " ===");
            Application.Quit(0);
        }

        private static void Log(string msg) { Debug.Log(Tag + " " + msg); }
    }
}
