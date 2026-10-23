// 《浮生渡》UI 层 · 基础构件（全部程序化创建，避免手写 .prefab YAML）
// @trace D-11 思源系字体、D-12 可访问性 Standard（最小 24 px @1080p 的等效字号按分辨率缩放）
// 【知识缺口 / 待补】D-11 要求的思源黑体（Source Han Sans）字体文件尚未入库；
// 本文件先用系统「微软雅黑」动态字体兜底，思源字体入库后只需替换 UiKit.Font 的实现。
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Fushengdu.UI
{
    public static class UiKit
    {
        private static Font _font;

        /// <summary>全局 UI 字体（中文可渲染）。</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                // 优先：项目中若已放入思源黑体则用它（D-11）
                Font loaded = Resources.Load<Font>("Fonts/SourceHanSansSC-Regular");
                if (loaded != null)
                {
                    _font = loaded;
                    return _font;
                }
                // 兜底：系统字体（Windows 中文）
                _font = CreateOsFont("Microsoft YaHei") ?? CreateOsFont("SimHei") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        private static Font CreateOsFont(string name)
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(name, 16);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>字号：以 1080p 为基准按屏幕高度缩放（D-12）。</summary>
        public static int Scaled(int baseSize1080p)
        {
            float scale = Screen.height / 1080f;
            if (scale < 0.75f) scale = 0.75f;
            return Mathf.RoundToInt(baseSize1080p * scale);
        }

        public static Canvas CreateCanvas(string name, Transform parent)
        {
            EnsureEventSystem();
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>确保场景存在 EventSystem（UI 按钮点击的前提）。项目使用旧 Input Manager（activeInputHandler=0），故配 StandaloneInputModule。
        /// 缺失会导致所有 Button.onClick 永不触发——「什么也点不了」的根因。</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            Debug.Log("[浮生渡] 已创建 EventSystem + StandaloneInputModule（UI 按钮可点击）");
        }

        public static GameObject Panel(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = UiSprites.Panel;
            image.type = Image.Type.Sliced;
            image.color = color;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        /// <summary>实心全遮底板（启动器 / 全屏弹窗用）——半透明九宫格叠在 3D 画面上会看不清字。</summary>
        public static GameObject PanelSolid(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = UiSprites.Opaque;
            image.type = Image.Type.Sliced;
            image.color = color;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        public static Text Text(string content, Transform parent, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = Scaled(size);
            t.color = color;
            t.alignment = anchor;
            t.text = content;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return t;
        }

        /// <summary>
        /// 单行标签。**pivot 必须是左上角 (0,1)**：全项目所有调用点的坐标都是按"文本框左上角"写的，
        /// 默认 (0.5,0.5) 会让每段文字整体左移半个宽度 —— 上一版左下的 "Lv.1 / 渡银" 和启动器
        /// 的说明文字就是被这个半宽推到了屏幕外（表现为"文字被切掉一半"）。
        /// </summary>
        public static Text Label(string content, Transform parent, int size, Color color, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = Scaled(size);
            t.color = color;
            t.text = content;
            t.alignment = TextAnchor.MiddleLeft;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
            return t;
        }

        public static Button Button(string label, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Color bg)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = UiSprites.Button;
            image.type = Image.Type.Sliced;
            image.color = bg;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = image;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.15f, 0.95f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var rt = image.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            // 与 Label 统一为"左上角定位"：anchoredPosition 就是按钮左上角，尺寸向右下展开。
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;

            var t = Text(label, go.transform, 18, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }

        /// <summary>创建一条血条 / 资源条（背景 + 填充）。</summary>
        public static Image CreateBar(string name, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Color fillColor)
        {
            var bgGo = new GameObject(name + "_Bg");
            bgGo.transform.SetParent(parent, false);
            var bg = bgGo.AddComponent<Image>();
            bg.sprite = UiSprites.BarBg;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0f, 0f, 0f, 0.72f);
            RectTransform bgRect = bg.rectTransform;
            bgRect.anchorMin = new Vector2(0f, 1f);
            bgRect.anchorMax = new Vector2(0f, 1f);
            bgRect.anchoredPosition = anchoredPosition;
            bgRect.sizeDelta = sizeDelta;

            var fillGo = new GameObject(name + "_Fill");
            fillGo.transform.SetParent(bgRect, false);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = UiSprites.BarFill;
            fill.color = fillColor;
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.pivot = new Vector2(0f, 0.5f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
            return fill;
        }

        /// <summary>品质色（经济 GDD §2.3.1 与美术圣经 §3.3 对齐）。</summary>
        public static Color QualityColor(Core.ItemQuality q)
        {
            switch (q)
            {
                case Core.ItemQuality.Common: return new Color(0.91f, 0.91f, 0.91f);
                case Core.ItemQuality.Fine: return new Color(0.30f, 0.85f, 0.35f);
                case Core.ItemQuality.Rare: return new Color(0.25f, 0.55f, 1.00f);
                case Core.ItemQuality.Epic: return new Color(0.72f, 0.35f, 0.95f);
                case Core.ItemQuality.Legendary: return new Color(1.00f, 0.62f, 0.15f);
                case Core.ItemQuality.SubMythic: return new Color(1.00f, 0.45f, 0.10f);
                case Core.ItemQuality.Mythic: return new Color(0.83f, 0.24f, 0.16f);
                case Core.ItemQuality.Transcendent: return new Color(1.00f, 0.84f, 0.00f);
                default: return Color.white;
            }
        }
    }
}
