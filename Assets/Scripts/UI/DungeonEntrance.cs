// 《浮生渡》UI · 主城 Hub 副本正式入口
// 在出生点附近生成「副本传送门」视觉，玩家靠近时于 HUD 显示「副本入口」按钮，
// 点击打开 DungeonEntryPanel（与调试面板解耦，成为正式入口）。
// 放在 UI 程序集（UI → Gameplay → Core），可引用 DungeonEntryPanel。
using UnityEngine;
using UnityEngine.UI;
using Fushengdu.Gameplay;

namespace Fushengdu.UI
{
    /// <summary>主城 Hub 副本正式入口：传送门视觉 + 靠近显示的 HUD 入口按钮。</summary>
    public sealed class DungeonEntrance : MonoBehaviour
    {
        private static DungeonEntrance _inst;
        private const float ProximityRadius = 8f;
        private static readonly Vector3 PortalPos = new Vector3(3f, 0f, 3f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindObjectOfType<DungeonEntrance>() != null) return;
            new GameObject("DungeonEntrance").AddComponent<DungeonEntrance>();
        }

        private GameObject _buttonGO;

        private void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(gameObject); return; }
            _inst = this;
            EnsureSingletons();
            BuildPortal();
            BuildHudButton();
        }

        /// <summary>确保所需单例存在，并接线结算面板（官方入口自包含，不依赖调试面板）。</summary>
        private void EnsureSingletons()
        {
            if (DungeonInstanceManager.Instance == null)
                new GameObject("DungeonInstanceManager").AddComponent<DungeonInstanceManager>();
            if (DungeonProgressStore.Instance == null)
                new GameObject("DungeonProgressStore").AddComponent<DungeonProgressStore>();
            DungeonInstanceManager.OnSettlement = DungeonSettlementPanel.Show;
        }

        /// <summary>主城出生点附近的副本传送门视觉（底座 + 旋转光柱）。</summary>
        private void BuildPortal()
        {
            var portal = new GameObject("DungeonPortal");
            portal.transform.position = PortalPos;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.transform.SetParent(portal.transform);
            disc.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            disc.transform.localScale = new Vector3(1.6f, 0.2f, 1.6f);
            disc.GetComponent<Renderer>().material.color = new Color(0.2f, 0.45f, 0.6f);

            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.transform.SetParent(portal.transform);
            column.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            column.transform.localScale = new Vector3(0.9f, 3f, 0.9f);
            column.GetComponent<Renderer>().material.color = new Color(0.4f, 0.8f, 1f, 0.7f);
            column.AddComponent<PortalSpinner>();
        }

        /// <summary>靠近传送门时显示的 HUD 入口按钮（屏幕空间覆盖层）。</summary>
        private void BuildHudButton()
        {
            var canvasGO = new GameObject("DungeonEntranceCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            _buttonGO = new GameObject("BtnDungeonEnter", typeof(RectTransform));
            _buttonGO.transform.SetParent(canvasGO.transform);
            var brt = _buttonGO.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0f);
            brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 24f);
            brt.sizeDelta = new Vector2(200f, 40f);

            var btn = _buttonGO.AddComponent<Button>();
            var img = _buttonGO.AddComponent<Image>();
            img.color = new Color(0.18f, 0.4f, 0.55f, 0.96f);
            btn.targetGraphic = img;
            btn.colors = new ColorBlock
            {
                normalColor = new Color(0.18f, 0.4f, 0.55f),
                highlightedColor = new Color(0.3f, 0.55f, 0.7f),
                pressedColor = new Color(0.1f, 0.25f, 0.35f),
                colorMultiplier = 1f
            };

            var txt = new GameObject("Text").AddComponent<Text>();
            txt.transform.SetParent(_buttonGO.transform);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;
            txt.text = "副本入口";
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 20;

            btn.onClick.AddListener(() => DungeonEntryPanel.Show());
            _buttonGO.SetActive(false); // 靠近传送门才显示
        }

        private void Update()
        {
            if (_buttonGO == null) return;
            var player = LocalPlayerLocator.Current;
            if (player == null) { _buttonGO.SetActive(false); return; }
            _buttonGO.SetActive(Vector3.Distance(player.transform.position, PortalPos) <= ProximityRadius);
        }
    }

    /// <summary>传送门光柱旋转动画（纯表现）。</summary>
    public sealed class PortalSpinner : MonoBehaviour
    {
        private void Update() { transform.Rotate(0f, 50f * Time.deltaTime, 0f); }
    }
}
