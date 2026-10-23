// 《浮生渡》UI 层 · 世界名牌（怪物血条 / NPC 名字）
// 目的：没有名牌时玩家只能靠"屏幕右下角那个目标框"才知道在打谁，MMO 的基本信息层是缺失的。
// 实现方式：单个覆盖 Canvas + 每帧把世界坐标投影到屏幕（比每个怪物挂一个世界空间 Canvas 便宜得多）。
using System.Collections.Generic;
using Fushengdu.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Fushengdu.UI
{
    public sealed class NameplateOverlay : MonoBehaviour
    {
        private sealed class Plate
        {
            public GameObject Root;
            public Text Label;
            public UnityEngine.UI.Image Fill;
            public Transform Target;
            public float HeadHeight;
            public bool IsNpc;
        }

        private Canvas _canvas;
        private readonly Dictionary<Object, Plate> _plates = new Dictionary<Object, Plate>();
        private float _scanTimer;
        private Camera _cam;

        private void Awake()
        {
            _canvas = UiKit.CreateCanvas("NameplateOverlay", null);
            _canvas.sortingOrder = 900;
            _canvas.transform.SetParent(transform, false);
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 0.4f;
                Scan();
            }

            UpdatePositions();
        }

        private void Scan()
        {
            MonsterActor[] monsters = Object.FindObjectsOfType<MonsterActor>();
            for (int i = 0; i < monsters.Length; i++)
            {
                MonsterActor m = monsters[i];
                if (m == null) continue;
                if (!_plates.ContainsKey(m))
                {
                    Core.ActorState st = m.State;
                    bool elite = st != null && (st.IsElite || st.IsBoss);
                    float head = elite ? 2.6f : 2.0f;
                    if (st != null && st.IsBoss) head = 3.2f;
                    _plates[m] = CreatePlate(m.transform, head, false,
                        string.IsNullOrEmpty(st != null ? st.Name : null) ? "怪物" : st.Name, true);
                }
            }

            if (NpcEntity.All() != null)
            {
                foreach (KeyValuePair<int, NpcEntity> kv in NpcEntity.All())
                {
                    NpcEntity npc = kv.Value;
                    if (npc == null || _plates.ContainsKey(npc)) continue;
                    _plates[npc] = CreatePlate(npc.transform, 2.35f, true, npc.NpcName, false);
                }
            }
        }

        private Plate CreatePlate(Transform target, float headHeight, bool isNpc, string name, bool showBar)
        {
            var plate = new Plate { Target = target, HeadHeight = headHeight, IsNpc = isNpc };

            var root = new GameObject("Plate_" + name);
            root.transform.SetParent(_canvas.transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(220f, 46f);
            plate.Root = root;

            plate.Label = UiKit.Text(name, root.transform, 15,
                isNpc ? new Color(0.98f, 0.85f, 0.45f) : new Color(0.98f, 0.55f, 0.52f), TextAnchor.LowerCenter);
            plate.Label.rectTransform.anchorMin = new Vector2(0f, 0.55f);
            plate.Label.rectTransform.anchorMax = Vector2.one;
            plate.Label.rectTransform.offsetMin = Vector2.zero;
            plate.Label.rectTransform.offsetMax = Vector2.zero;
            plate.Label.resizeTextForBestFit = false;

            if (showBar)
            {
                UnityEngine.UI.Image fill = UiKit.CreateBar("Np_Hp", root.transform, Vector2.zero,
                    new Vector2(130f, 10f), new Color(0.82f, 0.20f, 0.18f));
                RectTransform bgRect = fill.rectTransform.parent as RectTransform;
                bgRect.anchorMin = new Vector2(0.5f, 0f);
                bgRect.anchorMax = new Vector2(0.5f, 0f);
                bgRect.pivot = new Vector2(0.5f, 0f);
                bgRect.anchoredPosition = new Vector2(0f, 0f);
                plate.Fill = fill;
            }
            return plate;
        }

        private void UpdatePositions()
        {
            foreach (KeyValuePair<Object, Plate> kv in _plates)
            {
                Plate p = kv.Value;
                if (p.Root == null) continue;
                Object key = kv.Key;
                bool alive = key != null;
                if (!alive)
                {
                    p.Root.SetActive(false);
                    continue;
                }

                Transform t = p.Target;
                if (t == null)
                {
                    p.Root.SetActive(false);
                    continue;
                }

                Vector3 world = t.position + Vector3.up * p.HeadHeight;
                Vector3 screen = _cam.WorldToScreenPoint(world);
                bool visible = screen.z > 0f;
                float dist = Vector3.Distance(_cam.transform.position, t.position);
                if (dist > 55f) visible = false;

                p.Root.SetActive(visible);
                if (!visible) continue;

                RectTransform rt = p.Root.transform as RectTransform;
                float scale = Mathf.Clamp(1.1f - dist / 90f, 0.62f, 1f);
                rt.localScale = new Vector3(scale, scale, 1f);
                rt.anchoredPosition = new Vector2(screen.x, screen.y);

                if (p.Fill != null)
                {
                    var actor = key as ActorEntity;
                    if (actor != null && actor.State != null)
                    {
                        float ratio = actor.State.HpMax > 0f ? actor.State.Hp / actor.State.HpMax : 0f;
                        p.Fill.fillAmount = Mathf.Clamp01(ratio);
                        bool hidden = actor.State.Dead;
                        p.Root.SetActive(!hidden);
                    }
                }
            }
        }
    }
}
