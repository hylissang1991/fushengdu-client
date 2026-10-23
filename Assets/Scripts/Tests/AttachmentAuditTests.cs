// 《浮生渡》挂件配置校验单测 · 验证驱动（先写测试，再实现）
// @trace 形态图鉴 §2.4.0 规则 C / C-1（数量是唯一区分通道 → hard error）
// @trace 形态图鉴 §2.4.0 规则 C-1 另开一条（同骨架换皮 warning，不设 hard error）
// @trace 形态图鉴 §2.4.0 规则 B-1（建议上限 N ≤ 12 → warning）
// @trace 形态图鉴 §8.1.1 R-10（片数在 64px 剪影上不可读）
// 运行方式：Unity → Window → General → Test Runner → EditMode → Run All
//          或命令行 -executeMethod Fushengdu.Core.Tests.SelfCheckRunner.RunAll
using System.Collections.Generic;
using NUnit.Framework;

namespace Fushengdu.Core.Tests
{
    [TestFixture]
    public class AttachmentAuditTests
    {
        private const string Sk = "SK-05";
        private const string Head = "SLOT_Head";
        private const string Back = "SLOT_Back";
        private const string Float = "SLOT_Float";

        private static MonsterAppearance Make(string id, string skeleton, string archetype,
            float size, SlotAttachmentRow[] slots, NativeCountRow[] natives)
        {
            return new MonsterAppearance
            {
                Id = id,
                Name = id,
                SkeletonId = skeleton,
                ArchetypeId = archetype,
                SizeMultiplier = size,
                Slots = slots,
                Natives = natives
            };
        }

        private static SlotAttachmentRow[] Slots(params SlotAttachmentRow[] rows)
        {
            return rows;
        }

        private static NativeCountRow[] Natives(params NativeCountRow[] rows)
        {
            return rows;
        }

        [Test]
        public void 只差N_挂件ID与槽分配全同_报hard_error()
        {
            // 规则 C-1 的反面教材原型：D21 × R9 曾靠「背后符文屏 3 片 vs 6 片」区分 → 差异率 7.0%
            var a = Make("A", Sk, "", 2.0f,
                Slots(new SlotAttachmentRow(Head, "AT_GreekHead", 1), new SlotAttachmentRow(Back, "AT_RuneScreen", 3)),
                Natives());
            var b = Make("B", Sk, "", 2.0f,
                Slots(new SlotAttachmentRow(Head, "AT_GreekHead", 1), new SlotAttachmentRow(Back, "AT_RuneScreen", 6)),
                Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a, b });

            Assert.AreEqual(1, AttachmentAudit.CountErrors(f), "唯一区分通道是数量 → hard error");
            Assert.AreEqual("C-1", f[0].RuleId);
            StringAssert.Contains("唯一区别是 N", f[0].Message);
        }

        [Test]
        public void 只差N但体型差超过10percent_不报_已有非数量通道()
        {
            var a = Make("A", Sk, "", 2.0f,
                Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 3)), Natives());
            var b = Make("B", Sk, "", 2.6f,   // 0.6/2.6 = 23%
                Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 6)), Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a, b });

            Assert.AreEqual(0, f.Count, "体型差 ≥10% 时规则 C-1 明文不报");
        }

        [Test]
        public void 挂件ID不同_即使N不同_也不报()
        {
            var a = Make("A", Sk, "", 2.0f,
                Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 3)), Natives());
            var b = Make("B", Sk, "", 2.0f,
                Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 6), new SlotAttachmentRow(Head, "AT_SlabHead", 1)),
                Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a, b });

            Assert.AreEqual(0, f.Count, "挂件 ID 集合不同 → 已有形状通道");
        }

        [Test]
        public void 原生数量挂点只差N_同样报hard_error()
        {
            // 规则 C-1：扫描范围必须含 BP_Orbit / BP_Crystal / BP_Plate（数量差异最常出现在这里）
            var a = Make("M12", Sk, "", 2.0f, Slots(), Natives(new NativeCountRow("BP_Orbit", 3)));
            var b = Make("B16", Sk, "", 2.0f, Slots(), Natives(new NativeCountRow("BP_Orbit", 6)));

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a, b });

            Assert.AreEqual(1, AttachmentAudit.CountErrors(f));
            StringAssert.Contains("BP_Orbit", f[0].Message);
        }

        [Test]
        public void 挂件ID与N全同_体型差5percent_报换皮warning_不是error()
        {
            var a = Make("A", Sk, "", 2.0f, Slots(new SlotAttachmentRow(Head, "AT_GreekHead", 1)), Natives());
            var b = Make("B", Sk, "", 2.1f, Slots(new SlotAttachmentRow(Head, "AT_GreekHead", 1)), Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a, b });

            Assert.AreEqual(0, AttachmentAudit.CountErrors(f), "换皮不设 hard error");
            Assert.AreEqual(1, AttachmentAudit.CountWarnings(f));
            Assert.AreEqual("C-1W", f[0].RuleId);
            StringAssert.Contains("同骨架换皮", f[0].Message);
        }

        [Test]
        public void 挂件ID与N全同_体型差20percent_不报()
        {
            var a = Make("A", Sk, "", 2.0f, Slots(new SlotAttachmentRow(Head, "AT_GreekHead", 1)), Natives());
            var b = Make("B", Sk, "", 2.5f, Slots(new SlotAttachmentRow(Head, "AT_GreekHead", 1)), Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a, b });

            Assert.AreEqual(0, f.Count, "体型差 ≥10% 已有非数量通道");
        }

        [Test]
        public void N超过建议上限12_报warning不是error()
        {
            var a = Make("A", Sk, "", 2.0f, Slots(new SlotAttachmentRow(Float, "AT_StarRing", 13)), Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { a });

            Assert.AreEqual(0, AttachmentAudit.CountErrors(f));
            Assert.AreEqual(1, AttachmentAudit.CountWarnings(f));
            Assert.AreEqual("B-1", f[0].RuleId);
        }

        [Test]
        public void 同一BOSS的不同阶段_共用Id_不参与配对()
        {
            var p1 = Make("R11", Sk, "", 2.6f, Slots(new SlotAttachmentRow(Float, "AT_HalfRing", 1)), Natives());
            var p2 = Make("R11", Sk, "", 2.6f, Slots(new SlotAttachmentRow(Float, "AT_HalfRing", 4)), Natives());

            List<AuditFinding> f = AttachmentAudit.Run(new[] { p1, p2 });

            Assert.AreEqual(0, f.Count, "规则 C-1 工程边界③：同一 BOSS 的阶段 / 形态不比");
        }

        [Test]
        public void 跨骨架或跨原型_不在组内_不配对()
        {
            var a = Make("A", "SK-05", "", 2.0f, Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 3)), Natives());
            var b = Make("B", "SK-07", "", 2.0f, Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 6)), Natives());

            Assert.AreEqual(0, AttachmentAudit.Run(new[] { a, b }).Count, "跨骨架");

            var c = Make("C", "SK-05", "Proto-1", 2.0f, Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 3)), Natives());
            var d = Make("D", "SK-05", "Proto-2", 2.0f, Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 6)), Natives());

            Assert.AreEqual(0, AttachmentAudit.Run(new[] { c, d }).Count, "跨原型");

            var e = Make("E", "SK-05", "Proto-1", 2.0f, Slots(new SlotAttachmentRow(Back, "AT_RuneScreen", 3)), Natives());
            Assert.AreEqual(1, AttachmentAudit.Run(new[] { c, e }).Count, "同骨架同原型 → 组内要配对");
        }

        [Test]
        public void 签名与分组键_稳定且不含数量()
        {
            var a = Make("A", Sk, "P1", 2.0f,
                Slots(new SlotAttachmentRow(Head, "AT_X", 1), new SlotAttachmentRow(Back, "AT_Y", 3)),
                Natives(new NativeCountRow("BP_Orbit", 6)));
            var b = Make("B", Sk, "P1", 2.0f,
                Slots(new SlotAttachmentRow(Back, "AT_Y", 9), new SlotAttachmentRow(Head, "AT_X", 7)),
                Natives(new NativeCountRow("BP_Orbit", 2)));

            Assert.AreEqual(AttachmentSignature(a), AttachmentSignature(b), "ID 签名不含数量、与书写顺序无关");
            Assert.AreNotEqual(AttachmentAudit.CountSignatureOf(a), AttachmentAudit.CountSignatureOf(b));
            Assert.AreEqual("SK-05|P1", AttachmentAudit.GroupKeyOf(a));
        }

        [Test]
        public void 同挂件组合聚类_只返回成员不小于2的组_且按骨架隔离()
        {
            var b16 = Make("B16", Sk, "", 2.1f, Slots(), Natives(new NativeCountRow("BP_Orbit", 6)));
            var m12 = Make("M12", Sk, "", 1.3f, Slots(), Natives(new NativeCountRow("BP_Orbit", 3)));
            var other = Make("X", "SK-07", "", 2.0f, Slots(), Natives(new NativeCountRow("BP_Orbit", 6)));
            var solo = Make("Y", Sk, "", 2.0f, Slots(new SlotAttachmentRow(Head, "AT_Z", 1)), Natives());

            List<List<MonsterAppearance>> clusters =
                AttachmentAudit.GroupByAttachmentSignature(new[] { b16, m12, other, solo });

            Assert.AreEqual(1, clusters.Count, "SK-07 那只是另一个骨架，不成组；solo 只有一只");
            Assert.AreEqual(2, clusters[0].Count);
            Assert.AreEqual("B16", clusters[0][0].Id);
            Assert.AreEqual("M12", clusters[0][1].Id);
        }

        private static string AttachmentSignature(MonsterAppearance m)
        {
            return AttachmentAudit.AttachmentSignatureOf(m);
        }
    }
}
