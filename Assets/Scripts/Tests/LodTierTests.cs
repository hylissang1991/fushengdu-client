// 《浮生渡》LOD 档位换算单测 · 验证驱动（先写测试，再实现）
// @trace 形态图鉴 §1.1（LOD 切换距离表：LOD0 <10m / LOD1 10–25m / LOD2 25–50m / LOD3 >50m / Impostor >100m）
// @trace 形态图鉴 §2.4.0 规则 F-1（index ≥ LodCounts.Length → 按末级，不得取 0）
// 运行方式：Unity → Window → General → Test Runner → EditMode → Run All
//          或命令行 -executeMethod Fushengdu.Core.Tests.SelfCheckRunner.RunAll
using NUnit.Framework;

namespace Fushengdu.Core.Tests
{
    [TestFixture]
    public class LodTierTests
    {
        [Test]
        public void 距离分档_边界值与区间归属()
        {
            Assert.AreEqual(LodTier.Lod0, LodTier.IndexFromDistance(0f), "0m");
            Assert.AreEqual(LodTier.Lod0, LodTier.IndexFromDistance(9.99f), "<10m 仍是 LOD0");
            Assert.AreEqual(LodTier.Lod1, LodTier.IndexFromDistance(10f), "10m 起 LOD1");
            Assert.AreEqual(LodTier.Lod1, LodTier.IndexFromDistance(24.99f), "10–25m");
            Assert.AreEqual(LodTier.Lod2, LodTier.IndexFromDistance(25f), "25m 起 LOD2");
            Assert.AreEqual(LodTier.Lod2, LodTier.IndexFromDistance(49.99f), "25–50m");
            Assert.AreEqual(LodTier.Lod3, LodTier.IndexFromDistance(50f), "50m 起 LOD3");
            Assert.AreEqual(LodTier.Lod3, LodTier.IndexFromDistance(99.99f), "50–100m");
            Assert.AreEqual(LodTier.Impostor, LodTier.IndexFromDistance(100f), "100m 起 Impostor");
            Assert.AreEqual(LodTier.Impostor, LodTier.IndexFromDistance(500f), ">100m");
        }

        [Test]
        public void 距离分档_负数与NaN归LOD0_贴脸是保守解()
        {
            Assert.AreEqual(LodTier.Lod0, LodTier.IndexFromDistance(-5f));
            Assert.AreEqual(LodTier.Lod0, LodTier.IndexFromDistance(float.NaN));
        }

        [Test]
        public void 越界一律按末级_绝不取0()
        {
            // LodCounts 是 int[4]，而 Impostor 是第 5 态（index=4）→ 必越界
            Assert.AreEqual(3, LodTier.ClampToChain(LodTier.Impostor, 4), "Impostor 落到末级 LOD3，不是 0");
            Assert.AreEqual(3, LodTier.ClampToChain(9, 4), "任意越界值都落末级");
            Assert.AreEqual(3, LodTier.ClampToChain(3, 4), "不越界时原样返回");
            Assert.AreEqual(0, LodTier.ClampToChain(0, 4));
            Assert.AreEqual(0, LodTier.ClampToChain(-1, 4), "下越界归 0（贴脸方向，安全）");
            Assert.AreEqual(1, LodTier.ClampToChain(5, 2), "链长 2 时末级是 1");
            Assert.AreEqual(0, LodTier.ClampToChain(2, 0), "退化链长返回 0");
        }

        [Test]
        public void 距离直取槽档_Impostor落末级()
        {
            Assert.AreEqual(3, LodTier.SlotIndexFromDistance(120f, 4));
            Assert.AreEqual(0, LodTier.SlotIndexFromDistance(3f, 4));
            Assert.AreEqual(1, LodTier.SlotIndexFromDistance(18f, 4));
        }

        [Test]
        public void Impostor判定()
        {
            Assert.IsTrue(LodTier.IsImpostorIndex(LodTier.Impostor));
            Assert.IsFalse(LodTier.IsImpostorIndex(LodTier.Lod3));
        }

        [Test]
        public void 体型相对差_分母取较大值_与比较次序无关()
        {
            Assert.AreEqual(0f, LodTier.SizeDeltaRatio(2f, 2f), 0.0001f);
            Assert.AreEqual(0.0909f, LodTier.SizeDeltaRatio(2.0f, 2.2f), 0.0001f, "0.2/2.2");
            Assert.AreEqual(0.0909f, LodTier.SizeDeltaRatio(2.2f, 2.0f), 0.0001f, "次序无关");
            Assert.AreEqual(0.381f, LodTier.SizeDeltaRatio(1.3f, 2.1f), 0.001f, "0.8/2.1");
            Assert.AreEqual(0f, LodTier.SizeDeltaRatio(0f, 0f), 0.0001f, "退化输入不除零");
        }
    }
}
