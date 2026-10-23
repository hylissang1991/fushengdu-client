// 《浮生渡》表现层 · 复活点标记
// @trace 缺陷 N-1：玩家复活时由 CombatWorld.FindNearestRespawnPoint 取最近的复活点
// 【重要】类名必须与文件名一致 —— 否则场景反序列化时变成 missing script，
// 会导致 player 构建的 level0 损坏（启动崩溃）。详见 NpcEntity.cs 顶部说明。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>复活点标记。</summary>
    public sealed class RespawnPointMarker : MonoBehaviour
    {
        public string PointName = "新手村";
    }
}
