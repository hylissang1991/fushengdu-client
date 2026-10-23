// 《浮生渡》领域层 · LOD 档位（纯规则，不依赖 UnityEngine，可单测）
//
// 【为什么要有它】
//   形态图鉴 §2.4.0 规则 F-1 / §1.1 定死了一条铁律：**LOD 等级单一真值源 = 渲染侧
//   （`LODGroup` 当前 index）**，装配层**不得自己按距离阈值计算** —— 两套阈值必然漂移。
//   而切换距离的权威数据全项目只有一份，就是 §1.1：
//       LOD0 <10m / LOD1 10–25m / LOD2 25–50m / LOD3 >50m / Impostor >100m
//   本文件把「距离 → 档位」与「档位 → 装配层可用档」两条换算做成**纯函数**：
//   渲染侧的 MonoBehaviour 壳（Fushengdu.Gameplay.EntityLodState）只负责取数与派发，
//   规则本身不碰引擎、可单测。装配层将来只准读壳暴露的 index，不准自己算距离。
//
// 【5 态 vs int[4]】
//   LOD 链是 LOD0–LOD3 四档，Impostor（>100m）是第 5 态（整体烘焙成贴图，N 不参与）。
//   挂件规格的 `LodCounts` 是 int[4]（LOD0→LOD3），所以装配层拿到 Impostor（index=4）
//   必然越界 —— 规则 F-1：**越界一律按末级（Length−1），不得因越界取 0**。
//
// 【决策记录 · 为什么是「既算又派发」而不是「读 Unity 的选择」】（team-lead 2026-10-23 批准）
//   背景与备选：
//     · 备选 A（读 Unity 的选择）—— Unity 自带 `LODGroup` 按 **screenRelativeTransitionHeight**
//       （屏幕相对高度，不是米数）选档，且 **6000.6.7f1 没有任何公开的「当前 index」API**
//       （已扫 `UnityEngine.CoreModule.dll` 字符串核实：只有 `ForceLOD`，无 `CurrentLod` / `GetCurrentLod`）。
//       若另立一套米数阈值去"观察"它的结果，就同时存在「Unity 的相对高度」与「§1.1 的米数」两套真值
//       —— 正是契约明令要防的漂移。
//     · 备选 B（既算又派发，**已采纳**）—— 用 §1.1 的米数算出 index → `LODGroup.ForceLOD(index)`
//       → 暴露**同一个** index。派发值与暴露值恒等，米数阈值全项目只剩本文件一份。
//   后果（有意为之，已确认接受）：LODGroup 自带的相对高度阈值不再生效，由本文件的米数表接管。
//   **退役路径**：若将来 Unity 提供公开的当前 LOD index API，改为在 `EntityLodState` 里直接读取该 API，
//   然后让 `IndexFromDistance` / `SlotIndexFromDistance` 退役；`EntityLodState` 对外暴露的
//   `CurrentLodIndex` / `SlotLodIndex` / `SlotLodIndexFor` / `IsImpostor` 四个接口**保持不变**，
//   装配层无需改动。届时请同步删除本段与 §1.1 常量上的"唯一权威数据"表述。
//
// @trace 形态图鉴 §1.1（LOD 切换距离表 · 唯一权威数据）
// @trace 形态图鉴 §2.4.0 规则 F-1（`LodCounts` 越界按末级，不得取 0）
using System;

namespace Fushengdu.Core
{
    /// <summary>LOD 档位换算。纯静态、无状态、无分配。</summary>
    public static class LodTier
    {
        // ---- §1.1 切换距离（米）。改这里等于改图鉴，必须先改图鉴再来改代码 ----
        public const float Lod1MinDistance = 10f;
        public const float Lod2MinDistance = 25f;
        public const float Lod3MinDistance = 50f;
        public const float ImpostorMinDistance = 100f;

        public const int Lod0 = 0;
        public const int Lod1 = 1;
        public const int Lod2 = 2;
        public const int Lod3 = 3;

        /// <summary>第 5 态：Impostor（>100m）。整体烘焙成贴图，挂件不实例化、N 不参与。</summary>
        public const int Impostor = 4;

        /// <summary>LOD 链长度（LOD0–LOD3），与挂件规格 `LodCounts`(int[4]) 对齐。</summary>
        public const int LodChainLength = 4;

        /// <summary>距离（米）→ 档位 index（0–4）。负数与 NaN 归 LOD0（贴脸是保守解）。</summary>
        public static int IndexFromDistance(float distanceMeters)
        {
            if (float.IsNaN(distanceMeters)) return Lod0;
            if (distanceMeters < Lod1MinDistance) return Lod0;
            if (distanceMeters < Lod2MinDistance) return Lod1;
            if (distanceMeters < Lod3MinDistance) return Lod2;
            if (distanceMeters < ImpostorMinDistance) return Lod3;
            return Impostor;
        }

        /// <summary>
        /// 把 index 夹进长度为 chainLength 的链：**越界一律取末级（Length−1），绝不取 0**（规则 F-1）。
        /// chainLength ≤ 0 视为退化，返回 0。
        /// </summary>
        public static int ClampToChain(int index, int chainLength)
        {
            if (chainLength <= 0) return 0;
            if (index < 0) return 0;
            return index >= chainLength ? chainLength - 1 : index;
        }

        /// <summary>距离 → 装配层可用槽档（已按 chainLength 夹过，Impostor 落到末级）。</summary>
        public static int SlotIndexFromDistance(float distanceMeters, int chainLength)
        {
            return ClampToChain(IndexFromDistance(distanceMeters), chainLength);
        }

        /// <summary>是否 Impostor 态（>100m）。</summary>
        public static bool IsImpostorIndex(int index)
        {
            return index >= Impostor;
        }

        /// <summary>
        /// 两只实体的体型相对差（0–1）：`|a−b| / max(a,b)`。
        /// 规则 C-1 / C-1W 的「体型差 ≥10% / &lt;10%」判定用这个值（team-lead 2026-10-23 裁定采用本口径）。
        ///
        /// **口径：对称，分母取 max，不得改为 min。**
        /// 硬要求是**与比较次序无关**——若分母取 min，A 比 B 与 B 比 A 会得出不同结论，
        /// 校验报告将不可复现（同一对实体换个顺序结果就变）。max 口径天然对称。
        ///
        /// **边界差异（改分母就会变，别丢）**：2.0× vs 2.2×
        ///   · max 口径：`0.2 / 2.2 = 9.1%` → &lt;10% → **报** C-1W 换皮 warning；
        ///   · min 口径：`0.2 / 2.0 = 10.0%` → 未 &lt;10% → **不报**。
        /// 也就是说同一对配置，换分母就会在"报/不报"之间翻转。故本口径一旦改动，
        /// 必须同步复核 `AttachmentAudit` 的全部历史结论。
        /// </summary>
        public static float SizeDeltaRatio(float sizeA, float sizeB)
        {
            float hi = Math.Max(Math.Abs(sizeA), Math.Abs(sizeB));
            if (hi <= 0f) return 0f;
            return Math.Abs(sizeA - sizeB) / hi;
        }
    }
}
