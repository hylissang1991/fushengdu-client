// 《浮生渡》领域层 · 挂件配置校验（纯规则，不依赖 UnityEngine，可单测）
//
// 【为什么要有它】
//   §8.1.1 实测结论 R-10：**片数 / 个数在 64px 剪影上不可读**。历史上踩过两次：
//     · D21 × R9 靠「背后符文屏 3 片 vs 6 片」区分 → 差异率 7.0%，撞脸；
//     · §8.3 三只眼球靠「板数 12 vs 24」区分 → 剪影判定违规。
//   两条都在**建模开工前**才被发现。本校验器的作用是把这类错误提前到**配置阶段**暴露。
//
// 【它查什么（两条判定，必须分开）】
//   ① 规则 C-1（hard error）—— 两只实体**只有 N 不同**（挂件 ID 集合 + 槽分配完全相同、
//      且至少一处 N 不同）。数量是唯一区分通道 → 违规。
//   ② 规则 C-1W（warning）—— 挂件 ID 集合全同、N 也全同、且**体型差 <10%** →
//      「同骨架换皮 · 剪影撞脸风险」。**不设 hard error**，由剪影比对工具终裁。
//      两条必须分开：并进同一判定会让 hard error 误伤**合法换皮**。
//   ③ 规则 B-1（warning）—— 单条 N > 12（建议上限），提示评审面数与 64px 可读性。
//
// 【为什么不报的情形（规则 C-1 明文）】
//   · 同时还差体型 ≥10%、或差任一槽的挂件 ID / 形状 → 已有非数量通道，不报；
//   · N 全同 + 挂件 ID 全同、只差体型 / 配色 → 本校验器只管数量通道，不报；
//   · **同一 BOSS 的不同阶段 / 形态不参与跨实体配对**（Id 相同即跳过）。
//
// 【配对范围】
//   按「骨架 × 原型」分组，**只在组内成对比较**（跨原型剪影天然不同，不必比）。
//   全量 2900 对铺开的报告没人看得下去，不做。
//
// 【数据来源】
//   本文件只认 MonsterAppearance（校验用的最小视图），**不是怪物配置表本体**。
//   配置表定稿后由编辑器侧的 adapter 一行切换数据源，规则不动。
//
// @trace 形态图鉴 §2.4.0 规则 C / C-1（数量唯一区分 = hard error）
// @trace 形态图鉴 §2.4.0 规则 C-1 另开一条（同骨架换皮 warning）
// @trace 形态图鉴 §2.4.0 规则 B-1（建议上限 N ≤ 12，warning）
// @trace 形态图鉴 §8.1.1 R-10（片数在剪影上不可读）
using System;
using System.Collections.Generic;
using System.Text;

namespace Fushengdu.Core
{
    /// <summary>配置表的一行挂件：`AT_Xxx, SLOT_YYY, 缩放, N`（规则 A：一槽一行，N 是实例数）。</summary>
    public readonly struct SlotAttachmentRow
    {
        public readonly string SlotId;
        public readonly string AttachmentId;
        public readonly int Count;

        public SlotAttachmentRow(string slotId, string attachmentId, int count)
        {
            SlotId = slotId ?? string.Empty;
            AttachmentId = attachmentId ?? string.Empty;
            Count = count < 1 ? 1 : count;
        }
    }

    /// <summary>
    /// 骨架原生的「数量可变」挂点用量（规则 D：`BP_Orbit` / `BP_Crystal` / `BP_Plate` / `BP_Chain`）。
    /// 配置表**不写**这些，但校验器必须扫 —— 数量差异最常出现在这里。
    /// </summary>
    public readonly struct NativeCountRow
    {
        public readonly string FamilyId;
        public readonly int Count;

        public NativeCountRow(string familyId, int count)
        {
            FamilyId = familyId ?? string.Empty;
            Count = count < 1 ? 1 : count;
        }
    }

    /// <summary>校验输入的最小视图（不是配置表本体）。</summary>
    public sealed class MonsterAppearance
    {
        // 静态字段必须声明在实例字段**之前**：C# 实例字段初始化按文本顺序执行，
        // 若写在后面，Slots / Natives 的默认值会拿到尚未赋值的 null。
        public static readonly SlotAttachmentRow[] EmptySlots = new SlotAttachmentRow[0];
        public static readonly NativeCountRow[] EmptyNatives = new NativeCountRow[0];

        /// <summary>实体身份。同一 BOSS 的不同阶段 / 形态**共用同一个 Id** → 自动不参与配对。</summary>
        public string Id = string.Empty;

        public string Name = string.Empty;

        /// <summary>骨架（SK-01…SK-08）。分组维度之一。</summary>
        public string SkeletonId = string.Empty;

        /// <summary>原型。分组维度之一；为空则退化为「按骨架分组」。</summary>
        public string ArchetypeId = string.Empty;

        /// <summary>体型倍率。C-1 / C-1W 的体型差判定依据。</summary>
        public float SizeMultiplier = 1f;

        public SlotAttachmentRow[] Slots = EmptySlots;
        public NativeCountRow[] Natives = EmptyNatives;
    }

    public enum AuditSeverity
    {
        Warning = 0,
        Error = 1
    }

    public sealed class AuditFinding
    {
        public AuditSeverity Severity;
        public string RuleId = string.Empty;
        public string GroupKey = string.Empty;
        public string LeftId = string.Empty;
        public string RightId = string.Empty;
        public string Message = string.Empty;

        public override string ToString()
        {
            string tag = Severity == AuditSeverity.Error ? "ERROR" : "WARN ";
            return string.Format("[{0}] {1} | {2} × {3} | {4}",
                tag, RuleId, LeftId, RightId, Message);
        }
    }

    public static class AttachmentAudit
    {
        /// <summary>规则 B-1：建议上限 N ≤ 12（超过需单独评审面数与 64px 可读性）。warning 级。</summary>
        public const int RecommendedMaxCount = 12;

        /// <summary>规则 C-1W：体型差 &lt; 10% 才算「没有非数量通道」。</summary>
        public const float ReskinSizeTolerance = 0.10f;

        /// <summary>挂件 ID 签名：槽 + 挂件 ID + 原生族 ID，**不含数量**。</summary>
        public static string AttachmentSignatureOf(MonsterAppearance m)
        {
            var keys = new List<string>();
            for (int i = 0; i < m.Slots.Length; i++)
                keys.Add("S:" + m.Slots[i].SlotId + "=" + m.Slots[i].AttachmentId);
            for (int i = 0; i < m.Natives.Length; i++)
                keys.Add("N:" + m.Natives[i].FamilyId);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(keys[i]);
            }
            return sb.ToString();
        }

        /// <summary>数量签名：**同样的一组键，但带上各自的 N**。与 ID 签名配合即可判定「只有 N 不同」。</summary>
        public static string CountSignatureOf(MonsterAppearance m)
        {
            var keys = new List<string>();
            for (int i = 0; i < m.Slots.Length; i++)
                keys.Add("S:" + m.Slots[i].SlotId + "=" + m.Slots[i].AttachmentId + "×" + m.Slots[i].Count);
            for (int i = 0; i < m.Natives.Length; i++)
                keys.Add("N:" + m.Natives[i].FamilyId + "×" + m.Natives[i].Count);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(keys[i]);
            }
            return sb.ToString();
        }

        public static string GroupKeyOf(MonsterAppearance m)
        {
            return (m.SkeletonId ?? string.Empty) + "|" + (m.ArchetypeId ?? string.Empty);
        }

        /// <summary>规则 B-1：单条 N 超过建议上限 → warning（不 error）。</summary>
        public static void CheckCountUpperBound(MonsterAppearance m, List<AuditFinding> sink)
        {
            for (int i = 0; i < m.Slots.Length; i++)
            {
                if (m.Slots[i].Count > RecommendedMaxCount)
                    sink.Add(Warn("B-1", GroupKeyOf(m), m.Id, m.Id,
                        string.Format("{0} 的 N={1} 超过建议上限 {2}，需单独评审面数与 64px 剪影可读性",
                            m.Slots[i].AttachmentId, m.Slots[i].Count, RecommendedMaxCount)));
            }
            for (int i = 0; i < m.Natives.Length; i++)
            {
                if (m.Natives[i].Count > RecommendedMaxCount)
                    sink.Add(Warn("B-1", GroupKeyOf(m), m.Id, m.Id,
                        string.Format("原生挂点 {0} 的 N={1} 超过建议上限 {2}，需单独评审面数与 64px 剪影可读性",
                            m.Natives[i].FamilyId, m.Natives[i].Count, RecommendedMaxCount)));
            }
        }

        /// <summary>
        /// 全量校验：按「骨架 × 原型」分组，组内成对比较。
        /// 返回按严重程度（Error 在前）排序的结果。
        /// </summary>
        public static List<AuditFinding> Run(IList<MonsterAppearance> entities)
        {
            var findings = new List<AuditFinding>();
            // 注意：不能因为条目少于 2 条就提前返回 —— 规则 B-1（单条 N > 12）是**逐条**判定，
            // 与配对无关。单条目数据集也必须跑完 CheckCountUpperBound。
            if (entities == null) return findings;

            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] == null) continue;
                CheckCountUpperBound(entities[i], findings);
            }

            for (int i = 0; i < entities.Count; i++)
            {
                MonsterAppearance a = entities[i];
                if (a == null) continue;

                for (int j = i + 1; j < entities.Count; j++)
                {
                    MonsterAppearance b = entities[j];
                    if (b == null) continue;

                    // 同一 BOSS 的不同阶段 / 形态不参与跨实体配对
                    if (string.Equals(a.Id, b.Id, StringComparison.Ordinal)) continue;

                    // 只在「骨架 × 原型」组内配对
                    if (!string.Equals(GroupKeyOf(a), GroupKeyOf(b), StringComparison.Ordinal)) continue;

                    string sigA = AttachmentSignatureOf(a);
                    string sigB = AttachmentSignatureOf(b);
                    // 挂件 ID 集合 / 槽分配不同 → 已存在形状通道，两条判定都不触发
                    if (!string.Equals(sigA, sigB, StringComparison.Ordinal)) continue;

                    // 挂件 ID 全同 → 再看体型通道：规则 C-1 明文「同时还差体型 ≥10% → 不报」，
                    // 这条豁免对 C-1 与 C-1W **两条判定同时生效**（体型是有效非数量通道）。
                    float delta = LodTier.SizeDeltaRatio(a.SizeMultiplier, b.SizeMultiplier);
                    if (delta >= ReskinSizeTolerance) continue;

                    string cntA = CountSignatureOf(a);
                    string cntB = CountSignatureOf(b);
                    string group = GroupKeyOf(a);

                    if (!string.Equals(cntA, cntB, StringComparison.Ordinal))
                    {
                        // 规则 C-1：挂件 ID / 槽分配全同、体型差 <10%，唯一区别就是 N → hard error
                        findings.Add(Err("C-1", group, a.Id, b.Id,
                            string.Format("挂件 ID 集合与槽分配完全相同、体型相对差仅 {0:P1}，唯一区别是 N（{1} → {2}）；"
                                + "数量在 64px 剪影上不可读（R-10），必须改为形状 / 体型 / 另一槽的差异",
                                delta, cntA, cntB)));
                    }
                    else
                    {
                        // 规则 C-1W：N 也全同 → 同骨架换皮 · 剪影撞脸风险（warning，由剪影比对工具终裁）
                        findings.Add(Warn("C-1W", group, a.Id, b.Id,
                            string.Format("挂件 ID 集合与 N 全同，体型相对差仅 {0:P1}（<{1:P0}）→ 同骨架换皮 · 剪影撞脸风险；"
                                + "§0.3 第 2 条禁止靠换色出新 BOSS，由剪影比对工具终裁",
                                delta, ReskinSizeTolerance)));
                    }
                }
            }

            findings.Sort(CompareFindings);
            return findings;
        }

        /// <summary>
        /// 按「（骨架 × 原型） + 挂件 ID 签名」聚类，**只返回成员 ≥2 的组**。
        /// 用途：**信息性输出** —— 让美术侧一眼看到哪些实体共用同一套挂件，
        /// 它们只靠体型 / N / 配色区分。C-1 明文豁免的组合（体型差 ≥10%）不会出现在实际判定里，
        /// 但这种「靠体型一个通道兜底」的组合值得被看见：体型一旦调近，立刻变 hard error。
        /// 例：SK-05 的 B16 与 M-12 挂件完全相同，只差符石 6 vs 3 与体型 2.1× vs 1.3×。
        /// </summary>
        public static List<List<MonsterAppearance>> GroupByAttachmentSignature(IList<MonsterAppearance> entities)
        {
            var buckets = new Dictionary<string, List<MonsterAppearance>>(StringComparer.Ordinal);
            var order = new List<string>();

            if (entities != null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    MonsterAppearance m = entities[i];
                    if (m == null) continue;

                    string key = GroupKeyOf(m) + "\u0001" + AttachmentSignatureOf(m);
                    List<MonsterAppearance> bucket;
                    if (!buckets.TryGetValue(key, out bucket))
                    {
                        bucket = new List<MonsterAppearance>();
                        buckets[key] = bucket;
                        order.Add(key);
                    }
                    bucket.Add(m);
                }
            }

            var result = new List<List<MonsterAppearance>>();
            for (int i = 0; i < order.Count; i++)
            {
                List<MonsterAppearance> bucket = buckets[order[i]];
                if (bucket.Count >= 2) result.Add(bucket);
            }
            return result;
        }

        public static int CountErrors(IList<AuditFinding> findings)
        {
            int n = 0;
            for (int i = 0; i < findings.Count; i++)
                if (findings[i].Severity == AuditSeverity.Error) n++;
            return n;
        }

        public static int CountWarnings(IList<AuditFinding> findings)
        {
            int n = 0;
            for (int i = 0; i < findings.Count; i++)
                if (findings[i].Severity == AuditSeverity.Warning) n++;
            return n;
        }

        private static int CompareFindings(AuditFinding x, AuditFinding y)
        {
            if (x.Severity != y.Severity)
                return x.Severity == AuditSeverity.Error ? -1 : 1;
            int c = string.Compare(x.RuleId, y.RuleId, StringComparison.Ordinal);
            if (c != 0) return c;
            c = string.Compare(x.GroupKey, y.GroupKey, StringComparison.Ordinal);
            if (c != 0) return c;
            c = string.Compare(x.LeftId, y.LeftId, StringComparison.Ordinal);
            if (c != 0) return c;
            return string.Compare(x.RightId, y.RightId, StringComparison.Ordinal);
        }

        private static AuditFinding Err(string rule, string group, string a, string b, string msg)
        {
            return new AuditFinding
            {
                Severity = AuditSeverity.Error, RuleId = rule, GroupKey = group,
                LeftId = a, RightId = b, Message = msg
            };
        }

        private static AuditFinding Warn(string rule, string group, string a, string b, string msg)
        {
            return new AuditFinding
            {
                Severity = AuditSeverity.Warning, RuleId = rule, GroupKey = group,
                LeftId = a, RightId = b, Message = msg
            };
        }
    }
}
