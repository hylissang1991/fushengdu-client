// 《浮生渡》编辑器批处理 · BOSS / 团本终boss / 任务交付 渡银无头断言（统一上线/离线交付触发校验）
// 与 PlaytestHarness.CheckBossSilver 共用 BossSilverVerifier，走真实的 CombatWorld.OnDeath 奖励链路。
// 用法（命令行，无需独立播放器）：
//   Unity.exe -batchmode -quit -projectPath <工程> \
//             -executeMethod Fushengdu.Editor.BossSilverSelfCheck.Run
// 报告：工程同级 Logs/boss-silver-selfcheck.txt，同时打 Unity 控制台。
// 失败则以退出码 1 结束，便于 CI 判定。
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fushengdu.Core;
using Fushengdu.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class BossSilverSelfCheck
    {
        [MenuItem("浮生渡/5. 无头断言 BOSS 渡银", priority = 50)]
        public static void RunMenu() { Run(); }

        public static void Run()
        {
            var report = new StringBuilder();
            int pass = 0, fail = 0;
            report.AppendLine("# 《浮生渡》BOSS / 团本终boss 渡银无头断言");
            report.AppendLine("# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();
            report.AppendLine("## 校验口径：击杀 BOSS / 任务交付后 player.Silver 增量 == 对应口径，且统一事件 OnSilverGained 被正确触发（击杀/任务交付共用 RaiseSilverGained）");
            report.AppendLine();

            // 搭建最小权威端：CombatWorld + 一名玩家（无需场景 / 独立播放器）
            // 注意：编辑器批处理 executeMethod 没有游戏循环，AddComponent 不会自动触发 Awake，
            // 故手动调用 Awake 完成字段初始化（独立播放器内由引擎自动调用，无需此步）。
            var wgo = new GameObject("CW");
            var world = wgo.AddComponent<CombatWorld>();
            InvokeAwake(world);
            var pgo = new GameObject("Player");
            var player = pgo.AddComponent<PlayerActor>();
            InvokeAwake(player);
            player.State.Faction = ActorFaction.Player;
            player.State.Class = ClassId.SwordCultivator;
            player.State.Level = 1;
            world.Register(player);

            int expected, actual;

            bool okBoss = BossSilverVerifier.Verify(world, player, 30, false, out expected, out actual);
            report.AppendLine((okBoss ? "  PASS  " : "  FAIL  ")
                + "普通 BOSS  Lv30  渡银 Δ=" + actual + "  期望 " + expected
                + "  (" + Core.Progression.SilverForBoss(30) + ")");
            if (okBoss) pass++; else fail++;

            bool okRaid = BossSilverVerifier.Verify(world, player, 55, true, out expected, out actual);
            report.AppendLine((okRaid ? "  PASS  " : "  FAIL  ")
                + "团本终boss Lv55  渡银 Δ=" + actual + "  期望 " + expected
                + "  (" + Core.Progression.SilverForRaidFinalBoss(55) + ")");
            if (okRaid) pass++; else fail++;

            // 任务交付路径：经由真实 OfflineAuthority.RequestInteract 交付对话型任务，断言统一事件触发
            int qExpected, qActual;
            bool qFired;
            bool okQuest = BossSilverVerifier.VerifyQuest(world, player, 9001, out qExpected, out qActual, out qFired);
            report.AppendLine((okQuest ? "  PASS  " : "  FAIL  ")
                + "任务交付(对话环)  渡银 Δ=" + qActual + "  期望 " + qExpected
                + "  事件触发=" + (qFired ? "是" : "否"));
            if (okQuest) pass++; else fail++;

            report.AppendLine();
            report.AppendLine("# 汇总：通过 " + pass + " / 失败 " + fail + " / 合计 " + (pass + fail));

            // 输出到工程同级 Logs/
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "boss-silver-selfcheck.txt"), report.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BossSilver] 报告写入失败：" + ex.Message);
            }

            Debug.Log("[浮生渡] BOSS 渡银无头断言：通过 " + pass + "，失败 " + fail);
            if (fail > 0) EditorApplication.Exit(1);
        }

        /// <summary>编辑器批处理下 AddComponent 不会自动触发 Awake，这里手动调用以完成字段初始化。</summary>
        private static void InvokeAwake(UnityEngine.MonoBehaviour mb)
        {
            var m = mb.GetType().GetMethod("Awake",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m != null) m.Invoke(mb, null);
        }
    }
}
#endif
