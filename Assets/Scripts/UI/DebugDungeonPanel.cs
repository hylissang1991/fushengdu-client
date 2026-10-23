// 《浮生渡》UI · 副本系统调试面板（Slice 1 最小入口 / 验证用）
// 临时调试 UI：4 个代表本进入按钮（1001/2001/3001/4001）+ 击杀 BOSS + 退出。
// 完整 UI 留给 Slice 2。本面板用 RuntimeInitializeOnLoadMethod 自启动，无需改场景。
using UnityEngine;
using UnityEngine.UI;
using Fushengdu.Gameplay;

namespace Fushengdu.UI
{
    public sealed class DebugDungeonPanel : MonoBehaviour
    {
        private DungeonInstanceManager _mgr;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindObjectOfType<DebugDungeonPanel>() != null) return;
            new GameObject("DebugDungeonPanel").AddComponent<DebugDungeonPanel>();
        }

        private void Awake()
        {
            EnsureManager();
            BuildUI();
        }

        private void EnsureManager()
        {
            if (DungeonInstanceManager.Instance == null)
            {
                var go = new GameObject("DungeonInstanceManager");
                go.AddComponent<DungeonInstanceManager>();
            }
            _mgr = DungeonInstanceManager.Instance;

            // 进度存档单例（M6）：确保持久化可用；并挂接结算面板（UI 程序集）→ 不反向引用 Gameplay
            if (DungeonProgressStore.Instance == null)
            {
                var storeGO = new GameObject("DungeonProgressStore");
                storeGO.AddComponent<DungeonProgressStore>();
            }
            DungeonInstanceManager.OnSettlement = DungeonSettlementPanel.Show;
        }

        private void BuildUI()
        {
            var canvasGO = new GameObject("DebugDungeonCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform)).transform;
            panel.SetParent(canvas.transform);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(12f, 12f);
            rt.sizeDelta = new Vector2(240f, 360f);

            int y = 0;
            AddButton(panel, "进入 工厂本(1001)", () => _mgr.EnterDungeon(1001, null), ref y);
            AddButton(panel, "进入 固定本(2001)", () => _mgr.EnterDungeon(2001, null), ref y);
            AddButton(panel, "进入 10人团本(3001)", () => _mgr.EnterDungeon(3001, null), ref y);
            AddButton(panel, "进入 20人团本(4001)", () => _mgr.EnterDungeon(4001, null), ref y);
            AddButton(panel, "击杀 当前BOSS[0]", () => _mgr.DebugKillBoss(0), ref y);
            AddButton(panel, "击杀 当前BOSS[末]", () => _mgr.DebugKillBoss(_mgr.BossCount - 1), ref y);
            AddButton(panel, "退出副本(回主城)", () => _mgr.DebugExit(), ref y);
        }

        private static void AddButton(Transform parent, string label, System.Action onClick, ref int y)
        {
            var btnGO = new GameObject(label, typeof(RectTransform));
            btnGO.transform.SetParent(parent);
            var brt = btnGO.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(8f, -8f - y * 40f);
            brt.sizeDelta = new Vector2(224f, 34f);

            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnGO.AddComponent<Image>();
            btn.colors = new ColorBlock
            {
                normalColor = new Color(0.15f, 0.15f, 0.2f),
                highlightedColor = new Color(0.3f, 0.3f, 0.4f),
                pressedColor = new Color(0.1f, 0.1f, 0.15f),
                colorMultiplier = 1f
            };

            var txt = new GameObject("Text").AddComponent<Text>();
            txt.transform.SetParent(btnGO.transform);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 14;

            btn.onClick.AddListener(() => onClick());
            y++;
        }
    }
}
