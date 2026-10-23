// 《浮生渡》UI 层 · 程序化界面皮肤
// 背景：HUD 原先全是"纯色 Rectangle"，没有任何边框/圆角/切图，观感等同于调试面板。
// 这里在运行时合成 9-slice 雪碧图（圆角 + 描边 + 渐变），让 UiKit 的 Panel/Button/Bar 自动获得"游戏 UI"的质感。
// 不依赖任何外部图片资源，也不用 Additional Sprite Atlas（个人开发者的现实约束）。
using UnityEngine;

namespace Fushengdu.UI
{
    public static class UiSprites
    {
        private static Sprite _panel, _frame, _slot, _barBg, _barFill, _button, _toast, _opaque;

        /// <summary>面板：圆角 + 内填充 + 描边（用于所有黑色半透明底）。</summary>
        public static Sprite Panel => _panel ?? (_panel = BuildPanel("SP_Panel", 64, 10, 0.52f, 0.95f, 11f));

        /// <summary>描边框：只有边框，中心透明（用于技能槽、目标框）。</summary>
        public static Sprite Slot => _slot ?? (_slot = BuildSlot("SP_Slot", 64, 8));

        /// <summary>较亮的面板（按钮默认态）。</summary>
        public static Sprite Button => _button ?? (_button = BuildPanel("SP_Button", 64, 10, 0.85f, 1f, 11f));

        /// <summary>条底（黑，带描边）。</summary>
        public static Sprite BarBg => _barBg ?? (_barBg = BuildBarBg("SP_BarBg", 32, 5));

        /// <summary>条填充（上亮下暗，形成体积感）。</summary>
        public static Sprite BarFill => _barFill ?? (_barFill = BuildBarFill("SP_BarFill", 32, 5));

        /// <summary>提示条底色（偏暖）。</summary>
        public static Sprite Toast => _toast ?? (_toast = BuildPanel("SP_Toast", 64, 10, 0.62f, 0.9f, 13f));

        /// <summary>
        /// 实心底板：用于启动器 / 弹窗这类"必须挡住背后 3D 画面"的层。
        /// 【为什么需要】Panel 的九宫格中心是 0.52 的半透明填充，再乘 Image.color 的 alpha，
        /// 实际不透明度最多 0.5 —— 启动器叠在明亮的新手村上时文字直接糊掉。
        /// </summary>
        public static Sprite Opaque => _opaque ?? (_opaque = BuildPanel("SP_Opaque", 64, 12, 1f, 1f, 4f));

        // ---------------------------------------------------------------- 生成器

        private static Sprite BuildPanel(string name, int size, int border, float fillAlpha, float edgeAlpha, float radius)
        {
            var px = new Color32[size * size];
            Clear(px);
            FillRoundRect(px, size, 0, 0, size - 1, size - 1, radius, Col(1f, fillAlpha));
            StrokeRoundRect(px, size, 1, 1, size - 2, size - 2, radius, Col(1f, edgeAlpha), 2f);
            return ToSprite(name, px, size, border);
        }

        private static Sprite BuildSlot(string name, int size, int border)
        {
            var px = new Color32[size * size];
            Clear(px);
            FillRoundRect(px, size, 0, 0, size - 1, size - 1, 10f, Col(1f, 0.30f));
            StrokeRoundRect(px, size, 1, 1, size - 2, size - 2, 10f, Col(1f, 0.95f), 2f);
            // 上方一条高光，模拟"嵌进去的凹槽"
            FillRoundRect(px, size, 3, size - 6, size - 4, size - 4, 4f, Col(1f, 0.10f));
            return ToSprite(name, px, size, border);
        }

        private static Sprite BuildBarBg(string name, int size, int border)
        {
            var px = new Color32[size * size];
            Clear(px);
            FillRoundRect(px, size, 0, 0, size - 1, size - 1, 6f, Col(0f, 0.85f));
            StrokeRoundRect(px, size, 1, 1, size - 2, size - 2, 6f, Col(1f, 0.55f), 2f);
            return ToSprite(name, px, size, border);
        }

        private static Sprite BuildBarFill(string name, int size, int border)
        {
            var px = new Color32[size * size];
            Clear(px);
            for (int y = 0; y < size; y++)
            {
                float t = y / (float)(size - 1);
                // 上 35% 高光 → 中间本色 → 底部压暗
                float v = t > 0.62f ? Mathf.Lerp(1f, 0.72f, (t - 0.62f) / 0.38f)
                    : t < 0.28f ? Mathf.Lerp(0.85f, 1f, t / 0.28f) : 1f;
                for (int x = 0; x < size; x++) px[y * size + x] = Col(v, 1f);
            }
            return ToSprite(name, px, size, border);
        }

        private static Sprite ToSprite(string name, Color32[] px, int size, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            // pixelsPerUnit 必须是 100（Unity UI 的隐含约定）：9-slice 的边框按 px/100 换算成场景单位，
            // 才能在任意尺寸的 RectTransform 上保持"边框宽度不变、只有中间拉伸"。
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // ---------------------------------------------------------------- 光栅工具

        private static Color32 Col(float v, float a)
        {
            byte bv = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * 255f), 0, 255);
            byte ba = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(a) * 255f), 0, 255);
            return new Color32(bv, bv, bv, ba);
        }

        private static Color32 Blend(Color32 dst, Color32 src)
        {
            float sa = src.a / 255f;
            if (sa <= 0f) return dst;
            float da = dst.a / 255f;
            float oa = sa + da * (1f - sa);
            if (oa <= 0f) return new Color32(0, 0, 0, 0);
            byte r = (byte)Mathf.RoundToInt((src.r * sa + dst.r * da * (1f - sa)) / oa);
            byte g = (byte)Mathf.RoundToInt((src.g * sa + dst.g * da * (1f - sa)) / oa);
            byte b = (byte)Mathf.RoundToInt((src.b * sa + dst.b * da * (1f - sa)) / oa);
            return new Color32(r, g, b, (byte)Mathf.RoundToInt(oa * 255f));
        }

        private static void Clear(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
        }

        private static void SetPx(Color32[] px, int size, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            int i = y * size + x;
            px[i] = Blend(px[i], c);
        }

        private static bool InRoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            if (x < x0 || x > x1 || y < y0 || y > y1) return false;
            float cx = Mathf.Clamp(x, x0 + r, x1 - r);
            float cy = Mathf.Clamp(y, y0 + r, y1 - r);
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r + 0.01f;
        }

        private static void FillRoundRect(Color32[] px, int size, float x0, float y0, float x1, float y1, float r, Color32 c)
        {
            for (int y = 0; y <= size; y++)
                for (int x = 0; x <= size; x++)
                    if (InRoundRect(x, y, x0, y0, x1, y1, r)) SetPx(px, size, x - 0, y - 0, c);
        }

        private static void StrokeRoundRect(Color32[] px, int size, float x0, float y0, float x1, float y1, float r, Color32 c, float width)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inside = InRoundRect(x, y, x0, y0, x1, y1, r);
                    bool inner = InRoundRect(x, y, x0 + width, y0 + width, x1 - width, y1 - width, Mathf.Max(0f, r - width));
                    if (inside && !inner) SetPx(px, size, x, y, c);
                }
            }
        }
    }
}
