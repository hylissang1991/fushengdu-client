// 《浮生渡》表现层 · NPC（对话 / 接取与交付任务 / 商人）
// @trace 职业系统 GDD S-12（任务）
// 【重要】Unity 要求 MonoBehaviour 的类名必须与 .cs 文件名一致，否则该脚本在场景反序列化时
// 会变成「missing script」，进而导致 player 构建的 level0 损坏（启动崩溃）。
// 本文件只放 NpcEntity 一个类，就是为了满足这条规则。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public enum NpcRole
    {
        QuestGiver = 0, // 任务 NPC（村长处者）
        Merchant = 1,   // 商人
        Innkeeper = 2   // 复活点绑定者（旅店老板）
    }

    public sealed class NpcEntity : MonoBehaviour
    {
        public int NpcId = 9001;
        public string NpcName = "处者";
        public NpcRole Role = NpcRole.QuestGiver;
        public List<int> QuestIds = new List<int>();

        private static readonly Dictionary<int, NpcEntity> _registry = new Dictionary<int, NpcEntity>();

        public static void ClearRegistry()
        {
            _registry.Clear();
        }

        public static NpcEntity Find(int npcId)
        {
            NpcEntity n;
            return _registry.TryGetValue(npcId, out n) ? n : null;
        }

        public static IReadOnlyDictionary<int, NpcEntity> All()
        {
            return _registry;
        }

        public static void ClearAll()
        {
            _registry.Clear();
        }

        private void Awake()
        {
            // 场景里的 NPC 早期是默认胶囊体，观感等同于"原型占位"。
            // 这里在运行时按角色类型拼装低模（见 RuntimeArt），无需任何外部美术资源。
            RuntimeArt.AttachNpcModel(gameObject, Role == NpcRole.Merchant, NpcName);
        }

        private void OnEnable()
        {
            _registry[NpcId] = this;
        }

        private void OnDisable()
        {
            if (_registry.ContainsKey(NpcId) && _registry[NpcId] == this) _registry.Remove(NpcId);
        }
    }
}
