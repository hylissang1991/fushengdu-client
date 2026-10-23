// 《浮生渡》表现层 · 刷怪点标记
// 【重要】类名必须与文件名一致 —— 否则场景反序列化时变成 missing script，
// 会导致 player 构建的 level0 损坏（启动崩溃）。详见 NpcEntity.cs 顶部说明。
// 本组件只是场景里的"标记"，本身不带任何网络代码（Gameplay 程序集不允许引用 FishNet）：
//   - 联机/主机模式：由 Fushengdu.Network.FishNet.FishNetSession 在服务器就绪后读取本标记，
//     按 MonsterPrefab（已注册的可生成预制体）实例化怪物，并在服务端设置 MonsterDefId。
//   - 单机离线模式：由 OfflineAuthority 读取本标记直接生成非联网怪物（见 OfflineAuthority.cs）。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>刷怪点标记（由 Editor 脚本摆放，运行时按定义实例化怪物）。</summary>
    public sealed class MonsterSpawnPoint : MonoBehaviour
    {
        public int MonsterDefId = 1;
        public int Count = 3;
        public float ScatterRadius = 4f;

        /// <summary>已注册的可生成怪物预制体（由编辑器构建脚本注入）。仅联机/主机模式由网络层使用。</summary>
        public GameObject MonsterPrefab;
    }
}
