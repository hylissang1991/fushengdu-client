// 《浮生渡》表现层 · 归墟词缀跨切共享状态（静态）
// 由 AffixRuntime 在进本 Init 时写入、退出时 Reset；供 BossPhaseController / 战斗结算等协作系统读取。
// 之所以放静态而非挂在实例上：同一时刻只有一个副本实例在运行，且 BOSS 周期 AOE 需要读取「时滞」缩放，
// 而 BossPhaseController 并不直接持有 AffixRuntime 引用（避免反向依赖）。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 归墟侵蚀层词缀产生的跨系统效果状态。每进一个副本由 AffixRuntime.Init 调 Reset 清零，
    /// 退出副本由 AffixRuntime.OnDestroy 再 Reset，避免上一场残留污染下一场。
    /// </summary>
    public static class AffixEffects
    {
        /// <summary>711 时滞：BOSS 周期 AOE 间隔 / 预警时间乘数（默认 1，激活 = 0.6，即反应窗口缩短 40%）。</summary>
        public static float TimeLagIntervalMul = 1f;

        /// <summary>713 光暗交替：当前是否处于暗期（暗期玩家伤害 +25%，视野 -40% 在本期简化为仅伤害增益）。</summary>
        public static bool DarkPhaseActive = false;

        /// <summary>714 星序紊乱：是否激活（激活后玩家每次施放技能 CD 随机 ±20%）。</summary>
        public static bool StarChaosActive = false;

        /// <summary>进本 / 退本时清零所有跨切状态。</summary>
        public static void Reset()
        {
            TimeLagIntervalMul = 1f;
            DarkPhaseActive = false;
            StarChaosActive = false;
        }
    }
}
