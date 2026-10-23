// 《浮生渡》UI · 副本结算面板（M2）
// 退出副本且本场有掉落时由 DungeonInstanceManager.OnSettlement 触发，展示本场掉落清单：
// 物品名 + 品质名 + 品质色（UiKit.QualityColor）。品质名来自 ItemText.QualityName（已补 SubMythic/Mythic/Transcendent）。
// 放在 UI 程序集（UI → Gameplay → Core），以便复用 UiKit，且不被 Gameplay 反向引用。
using System.Collections.Generic;
using UnityEngine;
using Fushengdu.Core;

namespace Fushengdu.UI
{
    /// <summary>副本结算面板（纯程序化创建，复用 UiKit）。</summary>
    public static class DungeonSettlementPanel
    {
        private static GameObject _canvasGO;

        /// <summary>展示本场掉落（runDrops = itemDefId 列表）。点击「知道了」关闭。</summary>
        public static void Show(List<int> runDrops)
        {
            Hide();
            if (runDrops == null || runDrops.Count == 0) return;

            var canvas = UiKit.CreateCanvas("DungeonSettlementCanvas", null);
            _canvasGO = canvas.gameObject;

            // 全屏半透明底板
            UiKit.PanelSolid("Backdrop", canvas.transform, new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));

            // 主面板（参考分辨率 1920x1080 空间，居中）
            var panel = UiKit.Panel("SettlementPanel", canvas.transform, new Color(0.10f, 0.11f, 0.15f, 0.97f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-360f, -420f), new Vector2(360f, 420f));

            UiKit.Label("副本结算", panel.transform, 30, Color.white, new Vector2(24f, -24f), new Vector2(660f, 44));

            float y = 84f;
            for (int i = 0; i < runDrops.Count; i++)
            {
                int id = runDrops[i];
                var def = Core.ItemTable.Default.Get(id);
                string name = def != null ? def.Name : ("物品#" + id);
                Core.ItemQuality q = def != null ? def.Quality : Core.ItemQuality.Common;
                string qname = Core.ItemText.QualityName(q);              // 已补 SubMythic/Mythic/Transcendent，不显"未知"
                Color col = UiKit.QualityColor(q);
                UiKit.Label("· " + name + "  [" + qname + "]", panel.transform, 20, col,
                    new Vector2(24f, -y), new Vector2(660f, 28));
                y += 34f;
            }

            UiKit.Label("（离线单人：掉落已直接入包）", panel.transform, 14, new Color(0.7f, 0.7f, 0.7f),
                new Vector2(24f, -y - 12f), new Vector2(660f, 22));

            var close = UiKit.Button("知道了", panel.transform, new Vector2(240f, -760f), new Vector2(180f, 36f),
                new Color(0.2f, 0.5f, 0.3f));
            close.onClick.AddListener(Hide);
        }

        /// <summary>关闭并销毁结算面板。</summary>
        public static void Hide()
        {
            if (_canvasGO != null) { Object.Destroy(_canvasGO); _canvasGO = null; }
        }
    }
}
