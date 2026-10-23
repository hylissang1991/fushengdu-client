// 《浮生渡》表现层 · 本地存档（角色 / 等级 / 装备 / 任务进度）
// 从 WorldEntities.cs 抽出独立成文件，以满足「类名必须与文件名一致」规则
//（否则场景反序列化出现 missing script，导致 player 构建的 level0 损坏、启动崩溃）。
using System.IO;
using System.Text;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>本地存档（角色 / 等级 / 装备 / 任务进度）。</summary>
    public static class SaveSystem
    {
        private const string FileName = "fushengdu_save_v1.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists()
        {
            return File.Exists(SavePath);
        }

        public static void Save(CharacterData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SavePath, json, Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[存档] 写入失败：" + ex.Message);
            }
        }

        public static CharacterData Load()
        {
            try
            {
                if (!Exists()) return null;
                string json = File.ReadAllText(SavePath, Encoding.UTF8);
                return JsonUtility.FromJson<CharacterData>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[存档] 读取失败：" + ex.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (Exists()) File.Delete(SavePath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[存档] 删除失败：" + ex.Message);
            }
        }
    }
}
