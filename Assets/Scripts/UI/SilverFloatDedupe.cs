// 《浮生渡》UI 层 · 渡银飘字去重器
// 主机（host）下同一渡银事件会经「CombatWorld 直连」与「NetWorldBridge ObserversRpc」两条路径到达 UI，
// 参数完全一致、仅到达时刻相差约 1 帧。本器按 (玩家, 数额, 取整坐标) 在 0.1s 内去重，避免重复飘字。
// 抽成独立类，供 GameHud 运行期使用、亦供无头自检直接断言（避免拷贝逻辑导致"测的不是真代码"）。
using UnityEngine;

namespace Fushengdu.UI
{
    public sealed class SilverFloatDedupe
    {
        private int _lastPlayer, _lastAmount, _lastX, _lastY, _lastZ;
        private float _lastAt;

        /// <summary>是否应当显示本次飘字。同一玩家、同数额、同取整坐标、且距上次不足 0.1s → 视为重复，返回 false。</summary>
        public bool ShouldShow(int playerActorId, int silver, Vector3 worldPos, float now)
        {
            int cx = (int)(worldPos.x * 7.3f), cy = (int)(worldPos.y * 13.1f), cz = (int)(worldPos.z * 5.7f);
            if (playerActorId == _lastPlayer && silver == _lastAmount
                && cx == _lastX && cy == _lastY && cz == _lastZ
                && (now - _lastAt) < 0.1f)
                return false;
            _lastPlayer = playerActorId; _lastAmount = silver;
            _lastX = cx; _lastY = cy; _lastZ = cz; _lastAt = now;
            return true;
        }
    }
}
