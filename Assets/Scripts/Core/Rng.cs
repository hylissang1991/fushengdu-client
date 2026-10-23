// 《浮生渡》领域层 · 确定性随机数
// @trace ADR-001 N-1（领域层纯 C#，可在无 Unity 环境单测）
// 说明：掉落/伤害浮动必须是可复现的，便于写单测与服务端复核（D-14 公平红线：杜绝暗改随机）。
namespace Fushengdu.Core
{
    /// <summary>
    /// xorshift32 确定性随机数。同一 seed 必然产出同一序列，便于单测与服务端复核。
    /// </summary>
    public sealed class Rng
    {
        private uint _state;

        public Rng(uint seed)
        {
            // 0 会让 xorshift 退化为常数序列，必须规避。
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, 1) 浮点。</summary>
        public float NextFloat()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216.0f;
        }

        /// <summary>[min, max) 浮点。</summary>
        public float Range(float min, float max)
        {
            return min + NextFloat() * (max - min);
        }

        /// <summary>[min, max] 闭区间整数（用于掷骰子：D-19）。</summary>
        public int RangeInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            int span = maxInclusive - minInclusive + 1;
            return minInclusive + (int)(NextUInt() % (uint)span);
        }

        /// <summary>
        /// 伤害/治疗随机浮动 R：概念文档 §9.2 规定 0.95 ~ 1.05。
        /// @trace 职业系统 GDD §4.1
        /// </summary>
        public float CombatRoll()
        {
            return Range(0.95f, 1.05f);
        }
    }
}
