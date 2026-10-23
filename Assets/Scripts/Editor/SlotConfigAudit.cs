// 《浮生渡》编辑器工具 · 挂件配置校验（数量唯一区分 / 同骨架换皮）
//
// 【为什么要有它】
//   §8.1.1 实测结论 R-10：**片数 / 个数在 64px 剪影上不可读**。历史上踩过两次——
//   D21 × R9 靠「背后符文屏 3 片 vs 6 片」区分（差异率 7.0%）、§8.3 三只眼球靠「板数 12 vs 24」区分，
//   都在**建模开工前**才被发现。本工具把这类错误提前到**配置阶段**暴露：
//     · 规则 C-1（hard error）：两只实体只有 N 不同 → 数量是唯一区分通道，违规；
//     · 规则 C-1W（warning）：挂件 ID 与 N 全同、体型差 <10% → 同骨架换皮 · 撞脸风险，
//       由剪影比对工具终裁，**不设 hard error**（否则误伤合法换皮）；
//     · 规则 B-1（warning）：单条 N > 12（建议上限），提示评审面数与 64px 可读性。
//   配对按「骨架 × 原型」分组、只组内比（跨原型剪影天然不同，不必比），不做全量 2900 对铺开。
//
// 【数据源 —— 占位适配，不是配置表本体】
//   怪物全集总表尚未定稿，本期数据源是 `SilhouetteSheet.Sk05()`（SK-05 家族 25 条程序化占位配方）。
//   适配规则（**N 只取契约明列的那几处，其余一律 N=1，不臆造**）：
//     · 符石 FuShi3/6/9 → **原生挂点 `BP_Orbit`**，数量 3 / 6 / 9（规则 D：配置表不写、运行时生成）；
//     · 背后符文屏 BeiHou3Pian/6Pian → `SLOT_Back`，`AT_RuneScreen` × 3 / 6；
//     · 头顶 3 柄小剑 → `SLOT_Float`，`AT_SkySword` × 3；
//     · 半个环 / 环形冠痕 → `SLOT_Float`，N=1（契约规则 B 明列「半个环」为单点类）；
//     · 断柱 ×2 → `AT_BrokenPillar` × 2（契约规则 B 明列「断柱 ×2」为成组非环形）。
//   配置表定稿后**只改本文件的 adapter**，Fushengdu.Core.AttachmentAudit 的规则一行不动。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.SlotConfigAudit.Run
// 报告：工程根下 Logs/slot-config-audit.txt（与 core-selfcheck.txt 同目录；CI 判定：有 hard error 时退出码 1）
//
// @trace 形态图鉴 §2.4.0 规则 C / C-1 / C-1W / B-1
// @trace 形态图鉴 §8.1.1 R-10
// @trace ADR-001 N-9（所有提交必须附测试证据路径）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Fushengdu.Core;

namespace Fushengdu.Editor
{
    public static class SlotConfigAudit
    {
        private const string SkeletonSk05 = "SK-05";

        [MenuItem("浮生渡/24. 挂件配置校验（数量唯一区分 · 换皮）", priority = 240)]
        public static void RunMenu()
        {
            Run();
        }

        public static void Run()
        {
            List<MonsterAppearance> entities = Adapt(SilhouetteSheet.Sk05());

            var report = new StringBuilder();
            report.AppendLine("# 《浮生渡》挂件配置校验报告");
            report.AppendLine("# 数据源：SilhouetteSheet.Sk05()（SK-05 家族占位配方 · 配置表定稿后由 adapter 切换）");
            report.AppendLine("# 规则：C-1（只有 N 不同 → hard error）/ C-1W（换皮 warning）/ B-1（N>12 warning）");
            report.AppendLine("# 时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();
            report.AppendLine("## 输入条目（" + entities.Count + " 条）");
            report.AppendLine("编号   | 体型× | 挂件签名（不含数量）                        | 数量签名");
            for (int i = 0; i < entities.Count; i++)
            {
                MonsterAppearance m = entities[i];
                report.AppendLine(string.Format("{0,-6} | {1,5:F1} | {2,-42} | {3}",
                    m.Id, m.SizeMultiplier,
                    AttachmentAudit.AttachmentSignatureOf(m),
                    AttachmentAudit.CountSignatureOf(m)));
            }

            List<AuditFinding> findings = AttachmentAudit.Run(entities);
            int errors = AttachmentAudit.CountErrors(findings);
            int warns = AttachmentAudit.CountWarnings(findings);

            report.AppendLine();
            report.AppendLine("## 判定结果");
            if (findings.Count == 0)
            {
                report.AppendLine("无告警、无错误。");
            }
            else
            {
                report.AppendLine("分组 | 规则 | 编号 × 编号 | 说明");
                for (int i = 0; i < findings.Count; i++)
                    report.AppendLine(string.Format("{0} | {1} | {2} × {3} | {4}",
                        findings[i].GroupKey, findings[i].RuleId,
                        findings[i].LeftId, findings[i].RightId, findings[i].Message));
            }
            // 信息性输出：共用同一套挂件的组合（C-1 因体型差豁免的组合会在这里露出来）
            List<List<MonsterAppearance>> clusters = AttachmentAudit.GroupByAttachmentSignature(entities);
            report.AppendLine();
            report.AppendLine("## 同挂件组合（信息，不是判定）");
            if (clusters.Count == 0)
            {
                report.AppendLine("无：没有任何两只实体共用同一套挂件。");
            }
            else
            {
                report.AppendLine("这些组合挂件 ID 与槽分配完全相同，只靠体型 / N / 配色区分。");
                report.AppendLine("C-1 对「体型差 ≥10%」明文豁免，但**体型一旦调近就会立刻变 hard error**，改体型时请回看这里。");
                for (int i = 0; i < clusters.Count; i++)
                {
                    List<MonsterAppearance> c = clusters[i];
                    var sb = new StringBuilder();
                    for (int k = 0; k < c.Count; k++)
                    {
                        if (k > 0) sb.Append(" / ");
                        sb.Append(c[k].Id).Append("(").Append(c[k].SizeMultiplier.ToString("F1")).Append("×, ").Append(AttachmentAudit.CountSignatureOf(c[k])).Append(")");
                    }
                    float lo = c[0].SizeMultiplier, hi = c[0].SizeMultiplier;
                    for (int k = 1; k < c.Count; k++)
                    {
                        lo = Mathf.Min(lo, c[k].SizeMultiplier);
                        hi = Mathf.Max(hi, c[k].SizeMultiplier);
                    }
                    report.AppendLine(string.Format("  · {0} … {1}  体型跨度 {2:P1}（{3:F1}× → {4:F1}×）",
                        AttachmentAudit.GroupKeyOf(c[0]), sb, LodTier.SizeDeltaRatio(lo, hi), lo, hi));
                }
            }

            report.AppendLine();
            report.AppendLine(string.Format("# 汇总：hard error {0} / warning {1}", errors, warns));

            string dir = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs"));
            string path = Path.Combine(dir, "slot-config-audit.txt");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[挂件校验] 报告写入失败：" + ex.Message);
            }

            Debug.Log("[挂件校验] " + report);
            Debug.Log("[挂件校验] 报告已输出：" + path);
            foreach (AuditFinding f in findings)
            {
                if (f.Severity == AuditSeverity.Error) Debug.LogError("[挂件校验] " + f);
                else Debug.LogWarning("[挂件校验] " + f);
            }
            Debug.Log(string.Format("[挂件校验] 汇总：hard error {0} / warning {1}", errors, warns));

            // 供 CI 判定：只有 hard error 才让 batchmode 返回非 0（warning 不阻断）
            if (errors > 0)
                EditorApplication.Exit(1);
        }

        /// <summary>SilhouetteSheet 占位配方 → 校验输入视图。**配置表定稿后只改这里。**</summary>
        private static List<MonsterAppearance> Adapt(SilhouetteSheet.Recipe[] recipes)
        {
            var list = new List<MonsterAppearance>(recipes.Length);
            for (int i = 0; i < recipes.Length; i++)
            {
                SilhouetteSheet.Recipe r = recipes[i];

                var slots = new List<SlotAttachmentRow>();
                var natives = new List<NativeCountRow>();

                // 头一律进 SLOT_Head，N=1
                slots.Add(new SlotAttachmentRow("SLOT_Head", "AT_Head_" + r.Head, 1));

                // 手持：契约明列「断柱 ×2」为成组非环形（N=2），其余单点 N=1
                if (r.Hold != SilhouetteSheet.HoldKind.None)
                {
                    string id = r.Hold == SilhouetteSheet.HoldKind.DuanZhu || r.Hold == SilhouetteSheet.HoldKind.DuanZhuX2
                        ? "AT_BrokenPillar"
                        : "AT_Hold_" + r.Hold;
                    int n = r.Hold == SilhouetteSheet.HoldKind.DuanZhuX2 ? 2 : 1;
                    slots.Add(new SlotAttachmentRow("SLOT_ItemR", id, n));
                }

                // 悬浮 / 背饰：符石走**骨架原生 BP_Orbit**（规则 D，配置表不写）
                switch (r.Float)
                {
                    case SilhouetteSheet.FloatKind.FuShi3:
                        natives.Add(new NativeCountRow("BP_Orbit", 3)); break;
                    case SilhouetteSheet.FloatKind.FuShi6:
                        natives.Add(new NativeCountRow("BP_Orbit", 6)); break;
                    case SilhouetteSheet.FloatKind.FuShi9:
                        natives.Add(new NativeCountRow("BP_Orbit", 9)); break;
                    case SilhouetteSheet.FloatKind.BeiHou3Pian:
                        slots.Add(new SlotAttachmentRow("SLOT_Back", "AT_RuneScreen", 3)); break;
                    case SilhouetteSheet.FloatKind.BeiHou6Pian:
                        slots.Add(new SlotAttachmentRow("SLOT_Back", "AT_RuneScreen", 6)); break;
                    case SilhouetteSheet.FloatKind.TouDing3Jian:
                        slots.Add(new SlotAttachmentRow("SLOT_Float", "AT_SkySword", 3)); break;
                    case SilhouetteSheet.FloatKind.TouDingHuanHen:
                        slots.Add(new SlotAttachmentRow("SLOT_Float", "AT_CrownRing", 1)); break;
                    case SilhouetteSheet.FloatKind.TouDingBanHuan:
                        slots.Add(new SlotAttachmentRow("SLOT_Float", "AT_HalfRing", 1)); break;
                }

                list.Add(new MonsterAppearance
                {
                    Id = r.Id,
                    Name = r.Name,
                    SkeletonId = SkeletonSk05,
                    // 占位配方里没有「原型」字段 → 留空，退化为「按骨架分组」；
                    // 配置表补上原型后这里填 r.ArchetypeId 即可，规则与分组逻辑都不用动。
                    ArchetypeId = string.Empty,
                    SizeMultiplier = r.Size,
                    Slots = slots.ToArray(),
                    Natives = natives.ToArray()
                });
            }
            return list;
        }
    }
}
