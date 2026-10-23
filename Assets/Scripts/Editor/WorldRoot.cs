// 《浮生渡》编辑器工具 · 东荒大陆场景骨架构建器
// 把 WorldTable 的 14 张区域图实例化为场景中的区域根节点（纯空 Transform，命名 Region_<id>_<name>），
// 作为后续地形 / 刷怪 / 任务 / 副本入口的挂载点。
// 不挂自定义 MonoBehaviour 组件 —— 保持出包纯净（Editor 程序集不进玩家包，且根节点无自定义脚本引用）。
// 用法：菜单「浮生渡 / Build EastWild Region Roots」一键生成 14 个区域根节点。
using Fushengdu.Core;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class WorldRootBuilder
    {
        [MenuItem("浮生渡/Build EastWild Region Roots")]
        static void Build()
        {
            if (WorldTable.Default.Count != 14)
                Debug.LogWarning($"[浮生渡] WorldTable 区域数异常：{WorldTable.Default.Count}（应为 14）");

            var root = GameObject.Find("EastWildWorld");
            if (root == null)
            {
                root = new GameObject("EastWildWorld");
                Debug.Log("[浮生渡] 已创建世界根节点 EastWildWorld");
            }

            // 清除旧的区域根节点，保证幂等
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child.name.StartsWith("Region_"))
                    Object.DestroyImmediate(child.gameObject);
            }

            foreach (var r in WorldTable.Default.All())
            {
                var go = new GameObject($"Region_{r.Id}_{r.Name}");
                go.transform.SetParent(root.transform, false);
            }

            Debug.Log($"[浮生渡] 东荒 {WorldTable.Default.Count} 个区域根节点已生成于 EastWildWorld 下");
        }
    }
}
