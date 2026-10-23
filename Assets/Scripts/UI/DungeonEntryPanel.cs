// 《浮生渡》UI · 副本完整入口面板（M2）
// Hub（主城）触发入口：列出 4 个代表本（1001 工厂 / 2001 固定 / 3001 10人团本 / 4001 20人团本），
// 显示名称 / 等级要求 / 人数；若 AffixPool 非空则「加载公示」抽到的 2 词缀（名称+描述+互斥说明，
// 复用 AffixRuntime.Draw 同一套抽取逻辑，保证预览 == 实际）；显示本周顶级产出已用/上限；
// 「进入」按钮确认后携带公示词缀 EnterDungeon（预览==实际）。
// 放在 UI 程序集，复用 UiKit；不另起框架。
using System.Collections.Generic;
using UnityEngine;
using Fushengdu.Core;
using Fushengdu.Gameplay;

namespace Fushengdu.UI
{
    /// <summary>副本完整入口面板（纯程序化创建，复用 UiKit）。</summary>
    public static class DungeonEntryPanel
    {
        // 四档代表本
        private static readonly int[] RepresentativeIds = { 1001, 2001, 3001, 4001 };

        private static GameObject _canvasGO;

        /// <summary>打开副本入口面板（确保所需单例已存在）。</summary>
        public static void Show()
        {
            Hide();
            EnsureSingletons();

            var canvas = UiKit.CreateCanvas("DungeonEntryCanvas", null);
            _canvasGO = canvas.gameObject;

            // 全屏半透明底板
            UiKit.PanelSolid("Backdrop", canvas.transform, new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));

            // 主面板（参考分辨率 1920x1080 空间，居中；宽 880 / 高 900）
            var panel = UiKit.Panel("EntryPanel", canvas.transform, new Color(0.10f, 0.11f, 0.15f, 0.97f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-440f, -450f), new Vector2(440f, 450f));

            UiKit.Label("副本入口", panel.transform, 30, Color.white, new Vector2(24f, -24f), new Vector2(820f, 44));

            float y = 84f;
            foreach (int id in RepresentativeIds)
                y = AddDungeonRow(panel.transform, id, y);

            var close = UiKit.Button("关闭", panel.transform, new Vector2(280f, -820f), new Vector2(160f, 36f),
                new Color(0.5f, 0.2f, 0.2f));
            close.onClick.AddListener(Hide);
        }

        /// <summary>关闭并销毁入口面板。</summary>
        public static void Hide()
        {
            if (_canvasGO != null) { Object.Destroy(_canvasGO); _canvasGO = null; }
        }

        // -------------------------------------------------------- 单例保障
        private static void EnsureSingletons()
        {
            if (DungeonInstanceManager.Instance == null)
                new GameObject("DungeonInstanceManager").AddComponent<DungeonInstanceManager>();
            if (DungeonProgressStore.Instance == null)
                new GameObject("DungeonProgressStore").AddComponent<DungeonProgressStore>();
        }

        // -------------------------------------------------------- 单行：一本
        private static float AddDungeonRow(Transform parent, int id, float top)
        {
            var def = Core.DungeonTable.Default.Get(id);
            if (def == null) return top;

            float x = 24f;
            float w = 792f;

            // 标题行：名称 · 类型 · 等级 · 人数
            UiKit.Label(def.Name + "  ·  " + KindName(def.Kind) + "  ·  等级 " + def.RequiredLevel + "  ·  " + def.MinPlayers + "-" + def.MaxPlayers + "人",
                parent, 22, new Color(1f, 0.9f, 0.6f), new Vector2(x, -top), new Vector2(w, 30));
            float y = top + 38f;

            // 加载公示：抽 2 词缀（与进本实际抽取同一套逻辑）
            List<Core.AffixDef> drawn = null;
            if (def.AffixPool != null && def.AffixPool.Length > 0)
            {
                drawn = AffixRuntime.Draw(def.AffixPool, 99);
                for (int i = 0; i < drawn.Count; i++)
                {
                    var a = drawn[i];
                    if (a == null) continue;
                    UiKit.Label("归墟词缀 " + (i + 1) + "：" + a.Name + " — " + a.Description,
                        parent, 16, new Color(0.8f, 0.85f, 1f), new Vector2(x + 12f, -y), new Vector2(w - 12f, 22));
                    y += 26f;
                }
                int g0 = drawn.Count > 0 && drawn[0] != null ? drawn[0].MutexGroup : 0;
                int g1 = drawn.Count > 1 && drawn[1] != null ? drawn[1].MutexGroup : 0;
                if (g0 != 0 || g1 != 0)
                {
                    UiKit.Label("（互斥说明：同互斥组的词缀不会同时出现；本次组 " + (g0 == 0 ? "-" : g0.ToString()) +
                        " / " + (g1 == 0 ? "-" : g1.ToString()) + "）",
                        parent, 14, new Color(0.7f, 0.7f, 0.7f), new Vector2(x + 12f, -y), new Vector2(w - 12f, 20));
                    y += 24f;
                }
            }

            // 周帽 / 首通状态
            var store = DungeonProgressStore.Instance;
            int cap = store != null ? store.WeeklyCapLimit(def.LootRuleKey) : 0;
            int used = store != null ? store.WeeklyCapUsed(id) : 0;
            bool firstClear = store != null && store.IsFirstCleared(id);
            string capLine;
            if (cap > 0)
            {
                string qname = Core.ItemText.QualityName(Core.DungeonProgressRules.PremiumTier(def.LootRuleKey));
                capLine = "本周顶级产出（" + qname + "）：" + used + " / " + cap + (firstClear ? "　[已首通]" : "　[未首通]");
            }
            else
            {
                capLine = firstClear ? "状态：已首通" : "状态：未首通";
            }
            UiKit.Label(capLine, parent, 15,
                (cap > 0 && used >= cap) ? new Color(1f, 0.4f, 0.4f) : new Color(0.8f, 0.95f, 0.8f),
                new Vector2(x, -y), new Vector2(w, 22));
            y += 28f;

            // 进入按钮：携带公示词缀 → EnterDungeon（预览 == 实际）
            var btn = UiKit.Button("进入 " + def.Name, parent, new Vector2(x, -y), new Vector2(220f, 34f),
                new Color(0.2f, 0.5f, 0.3f));
            int capturedId = id;
            List<Core.AffixDef> capturedDraw = drawn;
            btn.onClick.AddListener(() =>
            {
                DungeonInstanceManager.Instance.EnterDungeon(capturedId, null, capturedDraw);
                Hide();
            });
            y += 50f;

            y += 12f; // 行间距
            return y;
        }

        private static string KindName(Core.DungeonKind k)
        {
            switch (k)
            {
                case Core.DungeonKind.Factory5: return "5人随机工厂本";
                case Core.DungeonKind.Fixed5: return "5人固定本";
                case Core.DungeonKind.Raid10: return "10人团本";
                case Core.DungeonKind.Raid20: return "20人团本";
                default: return "";
            }
        }
    }
}
