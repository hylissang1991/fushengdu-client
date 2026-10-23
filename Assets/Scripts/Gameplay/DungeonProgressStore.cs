// 《浮生渡》表现层 · 副本进度存档（独立 JSON 持久化）
// 序列化放本文件（Gameplay，可引 Unity）；Core/DungeonProgress 只持有纯数据。
// 路径：Application.persistentDataPath/fushengdu_dungeon_v1.json
// 职责：Load / Save（JSON via JsonUtility + DTO 转换 HashSet/Dictionary）/
//       TryResetIfNewWeek（本地时间跨过「周一 05:00」清空周帽与周保底，保留首通/近路）。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>副本进度存档（独立 JSON）。单例 MonoBehaviour，离线单人权威端持有。</summary>
    public sealed class DungeonProgressStore : MonoBehaviour
    {
        public static DungeonProgressStore Instance { get; private set; }

        private const string FileName = "fushengdu_dungeon_v1.json";
        private Core.DungeonProgressData _data = new Core.DungeonProgressData();

        // —— DTO：JsonUtility 不支持 HashSet/Dictionary，用 List 承载后还原 ——
        [Serializable]
        private sealed class Dto
        {
            public List<int> firstClears;
            public List<int> capIds;
            public List<int> capCounts;
            public List<int> mythicIds;
            public List<bool> mythicClaimed;
            public List<int> shortcuts;
            public long lastResetTicks;
        }

        private void Awake()
        {
            Instance = this;
            Load();
        }

        private string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        // ----------------------------------------------------- 加载 / 保存
        public void Load()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                _data = new Core.DungeonProgressData();
                return;
            }
            try
            {
                string json = File.ReadAllText(path);
                Dto d = JsonUtility.FromJson<Dto>(json);
                if (d == null) { _data = new Core.DungeonProgressData(); return; }
                _data = new Core.DungeonProgressData
                {
                    FirstClears = new HashSet<int>(d.firstClears ?? new List<int>()),
                    Shortcuts = new HashSet<int>(d.shortcuts ?? new List<int>()),
                    WeeklyCapUsed = new Dictionary<int, int>(),
                    WeeklyMythicClaimed = new Dictionary<int, bool>(),
                    LastResetTicks = d.lastResetTicks
                };
                if (d.capIds != null && d.capCounts != null)
                    for (int i = 0; i < d.capIds.Count && i < d.capCounts.Count; i++)
                        _data.WeeklyCapUsed[d.capIds[i]] = d.capCounts[i];
                if (d.mythicIds != null && d.mythicClaimed != null)
                    for (int i = 0; i < d.mythicIds.Count && i < d.mythicClaimed.Count; i++)
                        _data.WeeklyMythicClaimed[d.mythicIds[i]] = d.mythicClaimed[i];
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DungeonProgress] 读取存档失败，使用空存档：" + e.Message);
                _data = new Core.DungeonProgressData();
            }
        }

        public void Save()
        {
            var d = new Dto
            {
                firstClears = new List<int>(_data.FirstClears),
                shortcuts = new List<int>(_data.Shortcuts),
                capIds = new List<int>(),
                capCounts = new List<int>(),
                mythicIds = new List<int>(),
                mythicClaimed = new List<bool>(),
                lastResetTicks = _data.LastResetTicks
            };
            foreach (var kv in _data.WeeklyCapUsed) { d.capIds.Add(kv.Key); d.capCounts.Add(kv.Value); }
            foreach (var kv in _data.WeeklyMythicClaimed) { d.mythicIds.Add(kv.Key); d.mythicClaimed.Add(kv.Value); }

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DungeonProgress] 写入存档失败：" + e.Message);
            }
        }

        // ----------------------------------------------------- 周重置
        /// <summary>若本地时间已跨过「周一 05:00」则清空周帽/周保底（首通/近路保留），并刷新基准。</summary>
        public void TryResetIfNewWeek()
        {
            long now = DateTime.Now.Ticks;
            long boundary = LastMonday0500Ticks(DateTime.Now);
            if (_data.LastResetTicks == 0)
            {
                // 首次运行：锚定到本周基准，不立即重置
                _data.LastResetTicks = boundary;
                Save();
                return;
            }
            if (_data.LastResetTicks < boundary)
            {
                _data.WeeklyCapUsed.Clear();
                _data.WeeklyMythicClaimed.Clear();
                _data.LastResetTicks = boundary;
                Debug.Log("[DungeonProgress] 跨周重置：清空周帽/周保底（首通/近路保留）");
                Save();
            }
        }

        /// <summary>调试用：模拟「进入新的一周」，强制触发周重置逻辑（验证用）。</summary>
        public void DebugSimulateNewWeek()
        {
            _data.LastResetTicks = DateTime.Now.AddDays(-8).Ticks;
            TryResetIfNewWeek();
        }

        /// <summary>计算最近一个「周一 05:00」本地时间 Ticks（若当前早于本周一 05:00，则取上一周）。</summary>
        private static long LastMonday0500Ticks(DateTime now)
        {
            DateTime monday = now.Date;
            while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);
            DateTime boundary = new DateTime(monday.Year, monday.Month, monday.Day, 5, 0, 0);
            if (now < boundary) boundary = boundary.AddDays(-7);
            return boundary.Ticks;
        }

        // ----------------------------------------------------- 查询 / 写入 API
        public bool IsFirstCleared(int dungeonId) => _data.FirstClears.Contains(dungeonId);

        public void MarkFirstClear(int dungeonId)
        {
            if (_data.FirstClears.Add(dungeonId)) Save();
        }

        public int WeeklyCapLimit(string ruleKey) => Core.DungeonProgressRules.WeeklyCap(ruleKey);

        public int WeeklyCapUsed(int dungeonId)
        {
            int v;
            return _data.WeeklyCapUsed.TryGetValue(dungeonId, out v) ? v : 0;
        }

        public void AddWeeklyCap(int dungeonId, int n)
        {
            if (n <= 0) return;
            int v;
            _data.WeeklyCapUsed.TryGetValue(dungeonId, out v);
            _data.WeeklyCapUsed[dungeonId] = v + n;
            Save();
        }

        public bool IsMythicClaimed(int dungeonId)
        {
            bool v;
            return _data.WeeklyMythicClaimed.TryGetValue(dungeonId, out v) && v;
        }

        public void SetMythicClaimed(int dungeonId, bool value)
        {
            _data.WeeklyMythicClaimed[dungeonId] = value;
            Save();
        }
    }
}
