// 《浮生渡》渲染侧 · 实体 LOD 档位**唯一真值源**
//
// 【契约】形态图鉴 §2.4.0 规则 F-1 / §1.1：
//   · LOD 等级单一真值源 = 渲染侧（`LODGroup` 当前 index）；
//   · **装配层不得自己按距离阈值计算**，否则两套阈值必然漂移；
//   · 切换距离的权威数据只有 §1.1 那一份：LOD0 <10m / LOD1 10–25m / LOD2 25–50m / LOD3 >50m / Impostor >100m。
//
// 【为什么由本组件 ForceLOD 而不是"观察"Unity 的选择】
//   Unity 自带的 LODGroup 按 **screenRelativeTransitionHeight**（屏幕相对高度）选档，不是米数；
//   若只是"读"它的选择而另有一套米数阈值，就出现两套真值 —— 正是契约要避免的漂移。
//   所以本组件**既算又派发**：算出 index → `LODGroup.ForceLOD(index)` → 暴露同一个 index。
//   派发与暴露是同一个数，永远一致；米数阈值只维护 §1.1 这一份（在 Fushengdu.Core.LodTier）。
//
// 【装配层怎么用】
//   只读 `SlotLodIndex`（已按 LOD 链长度夹过，Impostor 落末级），或 `SlotLodIndexFor(chainLength)`
//   按挂件规格 `LodCounts.Length` 夹。**不要**读距离、不要用 Camera 自己算。
//
// 【性能】每帧只做一次距离比较，零分配（无字符串、无数组、无闭包）。
//   大量实体的批量 tick 收口属后续优化，不在本轮。
//
// @trace 形态图鉴 §1.1（LOD 切换距离表）
// @trace 形态图鉴 §2.4.0 规则 F-1（越界按末级，不得取 0）
// @trace D-13（GTX 1060 性能预算）
using Fushengdu.Core;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class EntityLodState : MonoBehaviour
    {
        [Tooltip("可空。挂上则由本组件统一派发档位；不挂则只暴露 index（供 Impostor 切换等其他系统读）。")]
        [SerializeField] private LODGroup lodGroup;

        [Tooltip("可空。留空用场景主相机（首次取到后缓存，后续不再查）。")]
        [SerializeField] private Camera viewCamera;

        private int _index = LodTier.Lod0;
        private bool _applied;
        private Camera _cachedMain;

        /// <summary>当前档位：0–3 = LOD0–LOD3，4 = Impostor。</summary>
        public int CurrentLodIndex
        {
            get { return _index; }
        }

        /// <summary>
        /// 装配层要用的档位：已按 LOD 链长度夹过（规则 F-1：越界按末级，绝不取 0）。
        /// 对应挂件规格 `LodCounts`(int[4]) 的下标。
        /// </summary>
        public int SlotLodIndex
        {
            get { return LodTier.ClampToChain(_index, LodTier.LodChainLength); }
        }

        /// <summary>是否 Impostor 态（>100m，整体烘焙成贴图，挂件不实例化）。</summary>
        public bool IsImpostor
        {
            get { return LodTier.IsImpostorIndex(_index); }
        }

        /// <summary>按指定链长度取槽档（挂件规格 `LodCounts` 长度不为 4 时用这个）。</summary>
        public int SlotLodIndexFor(int chainLength)
        {
            return LodTier.ClampToChain(_index, chainLength);
        }

        /// <summary>显式指定观察相机（主相机切换时调用；留空则回落到场景主相机）。</summary>
        public void SetViewCamera(Camera cam)
        {
            viewCamera = cam;
            _cachedMain = null;
            _applied = false;
        }

        /// <summary>立即重算并派发一档（不必等下一帧）。</summary>
        public void Refresh()
        {
            Camera cam = ResolveCamera();
            if (cam == null) return;

            Vector3 offset = transform.position - cam.transform.position;
            int next = LodTier.IndexFromDistance(offset.magnitude);

            // 档位没变且已派发过 → 什么都不做（每帧一次距离比较，其余零开销）
            if (next == _index && _applied) return;

            _index = next;
            Apply();
            _applied = true;
        }

        private void Update()
        {
            Refresh();
        }

        private void OnEnable()
        {
            // 启用时立刻对齐一次，避免首帧档位还停在默认值
            _applied = false;
            Refresh();
        }

        private void Apply()
        {
            if (lodGroup == null) return;

            int lodCount = lodGroup.lodCount;
            if (lodCount <= 0) return;

            // Impostor（4）越界时按末级派发 —— 与规则 F-1 同一口径
            int force = LodTier.ClampToChain(_index, lodCount);
            lodGroup.ForceLOD(force);
        }

        private Camera ResolveCamera()
        {
            if (viewCamera != null) return viewCamera;
            if (_cachedMain == null) _cachedMain = Camera.main;
            return _cachedMain;
        }
    }
}
