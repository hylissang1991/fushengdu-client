// 《浮生渡》表现层 · 采集点
// 【重要】类名必须与文件名一致 —— 否则场景反序列化时变成 missing script，
// 会导致 player 构建的 level0 损坏（启动崩溃）。详见 NpcEntity.cs 顶部说明。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>采集点：产出任务 / 材料物品。</summary>
    public sealed class GatherNode : MonoBehaviour
    {
        public int ItemDefId = 3001;
        public int AmountPerGather = 1;
        public float RespawnSeconds = 30f;
        public float InteractRange = 2.5f;

        private float _timer;
        private bool _depleted;
        private Renderer _renderer;

        public bool Available => !_depleted;

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
        }

        private void Update()
        {
            if (!_depleted) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _depleted = false;
            if (_renderer != null) _renderer.enabled = true;
        }

        public bool TryGather(out int itemDefId, out int amount)
        {
            itemDefId = ItemDefId;
            amount = AmountPerGather;
            if (_depleted) return false;
            _depleted = true;
            _timer = RespawnSeconds;
            if (_renderer != null) _renderer.enabled = false;
            return true;
        }
    }
}
