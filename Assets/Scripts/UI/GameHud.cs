// 《浮生渡》UI 层 · 启动器 + 核心 HUD + 各面板
// @trace 职业系统 GDD §6（UI 接口）、D-12（可访问性：冷却扫边 + 数字倒计时双通道）
// @trace D-19（拾取窗口：需要 / 掷骰子 / 放弃）
// 全部 UI 由代码构建，不依赖手写 .prefab；与网络框架解耦（只依赖 Fushengdu.Network.Abstraction）。
using System.Collections.Generic;
using Fushengdu.Core;
using Fushengdu.Gameplay;
using Fushengdu.Network.Abstraction;
using UnityEngine;
using UnityEngine.UI;

namespace Fushengdu.UI
{
    /// <summary>
    /// 游戏 UI 根：启动器 → HUD；并挂载背包 / 角色 / 任务 / 拾取窗口。
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        // ------------------------------------------------------------ 状态
        private Canvas _canvas;
        private GameObject _launcher;
        private GameObject _hud;

        private InputField _nameInput;
        private InputField _addressInput;
        private InputField _portInput;
        private Text _classLabel;
        private Text _genderLabel;
        private Text _statusText;
        private Transform _lanList;

        private int _classIndex;      // 0 = 剑修，1 = 狂战士
        private bool _female;

        private PlayerActor _localPlayer;
        private CameraFollow _camera;

        // HUD 元素
        private Image _hpFill;
        private Image _resourceFill;
        private Image _xpFill;
        private Text _hpText;
        private Text _resourceText;
        private Text _levelText;
        private Text _targetNameText;
        private Image _targetHpFill;
        private GameObject _targetFrame;
        private Text _questTracker;
        private Text _buffText;
        private Text _silverText;
        private readonly List<SkillSlotUi> _slots = new List<SkillSlotUi>();
        private Text _toast;
        private float _toastTimer;
        private Text _promptText;
        private GameObject _promptPanel;

        // 面板
        private GameObject _bagPanel;
        private GameObject _charPanel;
        private GameObject _questPanel;
        private GameObject _lootPanel;
        private Text _bagText;
        private Text _charText;
        private Text _questText;
        private Text _lootText;
        private int _lootSessionId;
        private int _lootItemDefId;

        // 背包可交互行（缺陷 N-2：掉落的装备必须能手工穿戴）
        private Transform _bagRowsContent;
        private int _bagRowSignature;

        // 死亡遮罩（缺陷 N-1）
        private GameObject _deathPanel;
        private Text _deathText;
        private float _respawnRemaining;
        private bool _deathShown;

        // 世界事件订阅（只订阅一次）
        private bool _worldHooked;

        // 击杀飘字（渡银 +X）
        private Transform _floatLayer;
        private readonly List<FloatEntry> _floats = new List<FloatEntry>();
        // 渡银飘字去重（主机双路径：① CombatWorld 直连 ② NetWorldBridge ObserversRpc）
        private int _lastSilverPlayer, _lastSilverAmount, _lastSilverX, _lastSilverY, _lastSilverZ;
        private float _lastSilverAt;

        private sealed class SkillSlotUi
        {
            public GameObject Root;
            public Image CooldownMask;
            public Text Name;
            public Text CooldownText;
            public Text Key;
        }

        // ------------------------------------------------------------ 生命周期
        private void Awake()
        {
            _canvas = UiKit.CreateCanvas("GameHud", transform);
            BuildLauncher();
            BuildHud();
            _hud.SetActive(false);

            // 桥接网络层事件到 UI（避免网络程序集反向依赖 UI）
            UiHooksInstaller.Install(this);
        }

        private void Update()
        {
            UpdateNetworkStatus();
            UpdateHud();
            HandleHotkeys();
            UpdateToast();
            UpdateFloatingTexts();
            UpdateInteractPrompt();
        }

        // ------------------------------------------------------------ 启动器
        private void BuildLauncher()
        {
            _launcher = UiKit.PanelSolid("Launcher", _canvas.transform, new Color(0.055f, 0.065f, 0.095f, 0.98f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Transform root = _launcher.transform;
            UiKit.Label("《浮生渡》垂直切片 · 1–15 级 · 局域网联机", root, 34, new Color(0.95f, 0.86f, 0.62f),
                new Vector2(60f, -60f), new Vector2(1200f, 60f));
            UiKit.Label("D-14 公平红线：本期无任何付费内容；掉落率与战斗数值与付费零耦合", root, 18,
                new Color(0.7f, 0.75f, 0.85f), new Vector2(60f, -110f), new Vector2(1400f, 30f));

            // 角色名
            UiKit.Label("角色名", root, 20, Color.white, new Vector2(60f, -170f), new Vector2(160f, 30f));
            _nameInput = CreateInput("NameInput", root, new Vector2(220f, -170f), "无名渡者");

            // 职业
            UiKit.Label("职业", root, 20, Color.white, new Vector2(60f, -220f), new Vector2(160f, 30f));
            var classBtn = UiKit.Button("切换职业", root, new Vector2(300f, -220f), new Vector2(180f, 40f), new Color(0.2f, 0.35f, 0.55f));
            classBtn.onClick.AddListener(OnToggleClass);
            _classLabel = UiKit.Label("剑修（东荒 · 物理 DPS）", root, 20, new Color(0.6f, 0.9f, 1f),
                new Vector2(500f, -220f), new Vector2(700f, 30f));

            // 性别
            var genderBtn = UiKit.Button("切换性别", root, new Vector2(300f, -275f), new Vector2(180f, 40f), new Color(0.2f, 0.35f, 0.55f));
            genderBtn.onClick.AddListener(() => { _female = !_female; RefreshGenderLabel(); });
            _genderLabel = UiKit.Label("男", root, 20, Color.white, new Vector2(500f, -275f), new Vector2(300f, 30f));

            // 单机
            var soloBtn = UiKit.Button("单机开始（离线可玩）", root, new Vector2(150f, -350f), new Vector2(260f, 52f), new Color(0.15f, 0.5f, 0.28f));
            soloBtn.onClick.AddListener(StartSolo);

            // 主机
            var hostBtn = UiKit.Button("创建主机（LAN）", root, new Vector2(150f, -415f), new Vector2(260f, 52f), new Color(0.15f, 0.4f, 0.6f));
            hostBtn.onClick.AddListener(StartHost);

            // 加入
            UiKit.Label("主机地址", root, 20, Color.white, new Vector2(430f, -350f), new Vector2(160f, 30f));
            _addressInput = CreateInput("AddressInput", root, new Vector2(600f, -350f), "127.0.0.1");
            UiKit.Label("端口", root, 20, Color.white, new Vector2(430f, -415f), new Vector2(120f, 30f));
            _portInput = CreateInput("PortInput", root, new Vector2(600f, -415f), "7770");
            _portInput.contentType = InputField.ContentType.IntegerNumber;

            var joinBtn = UiKit.Button("加入", root, new Vector2(860f, -415f), new Vector2(160f, 52f), new Color(0.5f, 0.35f, 0.15f));
            joinBtn.onClick.AddListener(JoinByAddress);

            var lanBtn = UiKit.Button("搜索局域网主机", root, new Vector2(1050f, -415f), new Vector2(240f, 52f), new Color(0.3f, 0.3f, 0.38f));
            lanBtn.onClick.AddListener(BeginLanScan);

            // 局域网列表
            GameObject listPanel = UiKit.Panel("LanList", root, new Color(0f, 0f, 0f, 0.35f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(430f, 120f), new Vector2(-60f, 480f));
            _lanList = listPanel.transform;

            _statusText = UiKit.Label("就绪。", root, 18, new Color(0.8f, 0.9f, 0.8f), new Vector2(60f, -500f), new Vector2(1200f, 30f));

            UiKit.Label("操作：WASD 移动 · Q 左转视角 / 右键拖拽自由转 · 左键或 Tab 选目标 · 1–5 小技能 · R 终极 · F 变身 · E 交互/接任务 · B 背包 · C 角色 · L 任务",
                root, 18, new Color(0.75f, 0.78f, 0.85f), new Vector2(60f, -560f), new Vector2(1600f, 30f));
        }

        private static InputField CreateInput(string name, Transform parent, Vector2 anchoredPosition, string initial)
        {
            var go = UiKit.Panel(name, parent, new Color(0.12f, 0.13f, 0.16f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0f, 1f);   // 与 Label / Button 一致：坐标为左上角
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = new Vector2(240f, 40f);

            var input = go.AddComponent<InputField>();
            var text = UiKit.Text(initial, go.transform, 18, Color.white, TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8f, 2f);
            text.rectTransform.offsetMax = new Vector2(-8f, -2f);
            input.textComponent = text;
            input.text = initial;
            return input;
        }

        private void OnToggleClass()
        {
            _classIndex = _classIndex == 0 ? 1 : 0;
            _classLabel.text = _classIndex == 0 ? "剑修（东荒 · 物理 DPS）" : "狂战士（阿斯加德 · 物理 DPS）";
        }

        private void RefreshGenderLabel()
        {
            _genderLabel.text = _female ? "女" : "男";
        }

        private int PortValue()
        {
            int p;
            return int.TryParse(_portInput != null ? _portInput.text : "7770", out p) ? p : 7770;
        }

        public void StartSolo()
        {
            ApplySettings();
            _launcher.SetActive(false);
            _hud.SetActive(true);

            var go = new GameObject("OfflineSession");
            var session = go.AddComponent<OfflineSession>();
            session.ClassId = (ClassId)NetLaunchSettings.ClassId;
            session.Female = NetLaunchSettings.Female;
            session.PlayerName = NetLaunchSettings.PlayerName;
            session.StartLevel = NetLaunchSettings.StartLevel;
            session.Begin();

            BindLocalPlayer(session.Player);
            ShowToast("单机模式：本机即权威端");
        }

        private void StartHost()
        {
            ApplySettings();
            HostSession(PortValue());
        }

        private void JoinByAddress()
        {
            ApplySettings();
            JoinSession(_addressInput.text.Trim(), PortValue());
        }

        // ------------------------------------------- 会话入口（UI 与命令行共用同一条路径）

        /// <summary>
        /// 创建主机（LAN）。**命令行自启动与 UI 按钮走的是同一个入口**——
        /// 保证端到端验证测的就是真实路径，而不是另起一套的测试路径。
        /// </summary>
        public void HostSession(int port)
        {
            INetSession session = NetSessionLocator.Current;
            if (session == null)
            {
                string msg = "场景中未找到网络会话组件（FishNetSession）";
                SetStatus(msg);
                Debug.LogError("[浮生渡] " + msg);
                return;
            }

            session.StartHost(port);
            _launcher.SetActive(false);
            _hud.SetActive(true);
            SetStatus("正在创建主机… 端口 " + port);

            // 自证日志：外部可用它确认「已进入主机状态」
            Debug.Log("[浮生渡] CLI/UI 已请求创建主机：端口=" + port
                      + " 房间=" + NetLaunchSettings.RoomName
                      + " 角色=" + ((ClassId)NetLaunchSettings.ClassId)
                      + " 发现端口=47770");
        }

        /// <summary>加入指定主机（LAN）。同样被命令行与 UI 共用。</summary>
        public void JoinSession(string address, int port)
        {
            INetSession session = NetSessionLocator.Current;
            if (session == null)
            {
                string msg = "场景中未找到网络会话组件（FishNetSession）";
                SetStatus(msg);
                Debug.LogError("[浮生渡] " + msg);
                return;
            }

            session.StartClient(address, port);
            _launcher.SetActive(false);
            _hud.SetActive(true);
            SetStatus("正在连接 " + address + ":" + port + "…");

            Debug.Log("[浮生渡] CLI/UI 已请求加入主机：" + address + ":" + port);
        }

        /// <summary>命令行自启动用的默认角色：剑修 / 男 / 默认名（不读 UI 控件）。</summary>
        public void ApplyDefaultSettings()
        {
            NetLaunchSettings.ClassId = (int)ClassId.SwordCultivator;
            NetLaunchSettings.Female = false;
            NetLaunchSettings.PlayerName = string.IsNullOrEmpty(NetLaunchSettings.PlayerName)
                ? "无名渡者" : NetLaunchSettings.PlayerName;
            NetLaunchSettings.StartLevel = 1;
        }

        private void BeginLanScan()
        {
            INetSession session = NetSessionLocator.Current;
            if (session == null) { SetStatus("场景中未找到网络会话组件（FishNetSession）"); return; }
            SetStatus("正在搜索局域网主机…");
            session.BeginLanDiscovery(OnLanResult);
        }

        private void OnLanResult(LanHostEntry[] entries)
        {
            for (int i = _lanList.childCount - 1; i >= 0; i--) Destroy(_lanList.GetChild(i).gameObject);
            if (entries == null || entries.Length == 0)
            {
                SetStatus("未发现局域网主机。请确认主机已创建，且防火墙放行 UDP 47770。");
                return;
            }
            for (int i = 0; i < entries.Length; i++)
            {
                LanHostEntry entry = entries[i];
                var btn = UiKit.Button(entry.Name + "  " + entry.Address + ":" + entry.Port, _lanList,
                    new Vector2(140f, -30f - i * 46f), new Vector2(280f, 40f), new Color(0.18f, 0.28f, 0.40f));
                LanHostEntry captured = entry;
                btn.onClick.AddListener(() =>
                {
                    ApplySettings();
                    NetSessionLocator.Current.StartClient(captured.Address, captured.Port);
                    _launcher.SetActive(false);
                    _hud.SetActive(true);
                });
            }
            SetStatus("发现 " + entries.Length + " 个主机，点击加入。");
        }

        private void ApplySettings()
        {
            NetLaunchSettings.ClassId = _classIndex == 0 ? (int)ClassId.SwordCultivator : (int)ClassId.Berserker;
            NetLaunchSettings.Female = _female;
            NetLaunchSettings.PlayerName = string.IsNullOrEmpty(_nameInput.text) ? "无名渡者" : _nameInput.text.Trim();
            NetLaunchSettings.StartLevel = 1;
            NetLaunchSettings.Address = _addressInput.text.Trim();
            NetLaunchSettings.Port = PortValue();
        }

        private void SetStatus(string msg)
        {
            if (_statusText != null) _statusText.text = msg;
        }

        // ------------------------------------------------------------ HUD
        private void BuildHud()
        {
            _hud = UiKit.Panel("Hud", _canvas.transform, new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Transform root = _hud.transform;

            // 左下：玩家血条 / 资源条
            GameObject playerFrame = UiKit.Panel("PlayerFrame", root, new Color(0f, 0f, 0f, 0.45f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 24f), new Vector2(560f, 190f));
            _hpFill = UiKit.CreateBar("HpBar", playerFrame.transform, new Vector2(255f, -40f), new Vector2(500f, 34f), new Color(0.75f, 0.16f, 0.16f));
            _hpText = UiKit.Text("0 / 0", _hpFill.transform, 16, Color.white, TextAnchor.MiddleCenter);
            _resourceFill = UiKit.CreateBar("ResourceBar", playerFrame.transform, new Vector2(255f, -86f), new Vector2(500f, 26f), new Color(0.20f, 0.55f, 0.95f));
            _resourceText = UiKit.Text("0 / 100", _resourceFill.transform, 14, Color.white, TextAnchor.MiddleCenter);
            _xpFill = UiKit.CreateBar("XpBar", playerFrame.transform, new Vector2(255f, -124f), new Vector2(500f, 16f), new Color(0.85f, 0.70f, 0.20f));
            _levelText = UiKit.Label("Lv.1", playerFrame.transform, 20, new Color(1f, 0.92f, 0.7f), new Vector2(20f, -40f), new Vector2(220f, 34f));
            _silverText = UiKit.Label("渡银 0", playerFrame.transform, 18, new Color(0.95f, 0.88f, 0.6f), new Vector2(20f, -86f), new Vector2(220f, 30f));

            // 顶部：目标框
            _targetFrame = UiKit.Panel("TargetFrame", root, new Color(0f, 0f, 0f, 0.5f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-220f, -160f), new Vector2(220f, -40f));
            _targetNameText = UiKit.Text("未选中目标", _targetFrame.transform, 18, Color.white, TextAnchor.MiddleCenter);
            _targetHpFill = UiKit.CreateBar("TargetHp", _targetFrame.transform, new Vector2(0f, -70f), new Vector2(400f, 20f), new Color(0.80f, 0.20f, 0.20f));
            _targetFrame.SetActive(false);

            // 底部：技能栏（7 槽）
            GameObject skillBar = UiKit.Panel("SkillBar", root, new Color(0f, 0f, 0f, 0.4f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-330f, 24f), new Vector2(330f, 130f));
            string[] keys = { "1", "2", "3", "4", "5", "R", "F" };
            for (int i = 0; i < 7; i++)
            {
                float x = -300f + i * 88f;
                var slotGo = UiKit.Panel("Slot" + i, skillBar.transform, new Color(0.16f, 0.17f, 0.21f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x - 38f, -30f), new Vector2(x + 38f, 40f));
                var mask = UiKit.Panel("CdMask", slotGo.transform, new Color(0f, 0f, 0f, 0.62f),
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).GetComponent<Image>();
                mask.type = Image.Type.Filled;
                mask.fillMethod = Image.FillMethod.Radial360;
                mask.fillAmount = 0f;
                mask.fillClockwise = false;

                var nameText = UiKit.Text("--", slotGo.transform, 13, Color.white, TextAnchor.MiddleCenter);
                nameText.rectTransform.offsetMin = new Vector2(2f, 2f);
                nameText.rectTransform.offsetMax = new Vector2(-2f, -2f);
                var cdText = UiKit.Text("", slotGo.transform, 16, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleCenter);
                var keyText = UiKit.Label(keys[i], slotGo.transform, 14, new Color(0.85f, 0.85f, 0.9f),
                    new Vector2(40f, -2f), new Vector2(28f, 20f));

                _slots.Add(new SkillSlotUi { Root = slotGo, CooldownMask = mask, Name = nameText, CooldownText = cdText, Key = keyText });
            }

            // 右上：任务追踪
            GameObject questBox = UiKit.Panel("QuestTracker", root, new Color(0f, 0f, 0f, 0.38f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-460f, -300f), new Vector2(-24f, -24f));
            _questTracker = UiKit.Text("任务：按 L 打开任务日志", questBox.transform, 16, new Color(0.9f, 0.9f, 0.8f), TextAnchor.UpperLeft);
            _questTracker.rectTransform.offsetMin = new Vector2(12f, 8f);
            _questTracker.rectTransform.offsetMax = new Vector2(-12f, -8f);
            _questTracker.horizontalOverflow = HorizontalWrapMode.Wrap;
            _questTracker.verticalOverflow = VerticalWrapMode.Overflow;

            // 左上：增益/减益
            GameObject buffBox = UiKit.Panel("BuffBox", root, new Color(0f, 0f, 0f, 0.35f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -230f), new Vector2(430f, -208f));
            _buffText = UiKit.Text("", buffBox.transform, 15, new Color(0.8f, 0.95f, 1f), TextAnchor.UpperLeft);
            _buffText.rectTransform.offsetMin = new Vector2(10f, 6f);
            _buffText.rectTransform.offsetMax = new Vector2(-10f, -6f);
            _buffText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 交互提示：靠近 NPC 才显示"按 E 与 XX 对话"
            // 【为什么必须有】没有它，玩家走到 NPC 面前也不知道该按什么 —— 直接反馈成"接不了任务"。
            _promptPanel = UiKit.Panel("InteractPrompt", root, new Color(0f, 0f, 0f, 0.6f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-230f, 70f), new Vector2(230f, 116f));
            _promptText = UiKit.Text("", _promptPanel.transform, 18, new Color(1f, 0.92f, 0.70f), TextAnchor.MiddleCenter);
            _promptPanel.SetActive(false);

            // Toast
            var toastGo = UiKit.Panel("Toast", root, new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-320f, 140f), new Vector2(320f, 200f));
            _toast = UiKit.Text("", toastGo.transform, 18, new Color(1f, 0.95f, 0.75f), TextAnchor.MiddleCenter);
            toastGo.SetActive(false);

            // 飘字层：全屏 RectTransform 容器，承载击杀渡银飘字（无 Graphic，不拦截点击）
            var floatGo = new GameObject("FloatLayer");
            floatGo.transform.SetParent(_canvas.transform, false);
            var floatRtf = floatGo.AddComponent<RectTransform>();
            floatRtf.anchorMin = Vector2.zero;
            floatRtf.anchorMax = Vector2.one;
            floatRtf.offsetMin = Vector2.zero;
            floatRtf.offsetMax = Vector2.zero;
            _floatLayer = floatGo.transform;

            BuildPanels(root);
        }

        /// <summary>走到 NPC 附近时给出"按 E 交互"的提示（4.5 m 内）。</summary>
        private void UpdateInteractPrompt()
        {
            if (_promptPanel == null || _promptText == null) return;
            if (_localPlayer == null || _localPlayer.State == null || _localPlayer.State.Dead)
            {
                if (_promptPanel.activeSelf) _promptPanel.SetActive(false);
                return;
            }

            NpcEntity nearest = null;
            float best = 4.5f;
            foreach (KeyValuePair<int, NpcEntity> kv in NpcEntity.All())
            {
                if (kv.Value == null) continue;
                float d = Vector3.Distance(_localPlayer.transform.position, kv.Value.transform.position);
                if (d > best) continue;
                best = d;
                nearest = kv.Value;
            }

            if (nearest == null)
            {
                if (_promptPanel.activeSelf) _promptPanel.SetActive(false);
                return;
            }

            string msg = "按 E 与「" + nearest.NpcName + "」对话";
            if (nearest.Role == NpcRole.QuestGiver) msg += "　（接 / 交任务）";
            else if (nearest.Role == NpcRole.Merchant) msg += "　（买卖）";

            if (!_promptPanel.activeSelf) _promptPanel.SetActive(true);
            if (_promptText.text != msg) _promptText.text = msg;
        }

        private void BuildPanels(Transform root)
        {
            // 背包（可交互：点击行 = 穿戴）
            _bagPanel = UiKit.Panel("BagPanel", root, new Color(0.05f, 0.06f, 0.09f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-320f, -250f), new Vector2(320f, 250f));

            GameObject bagHeader = UiKit.Panel("BagHeader", _bagPanel.transform, new Color(0f, 0f, 0f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -46f), new Vector2(-16f, -8f));
            _bagText = UiKit.Text("背包（B 关闭）· 点击物品穿戴", bagHeader.transform, 16, new Color(1f, 0.92f, 0.72f), TextAnchor.UpperLeft);
            _bagText.rectTransform.offsetMin = Vector2.zero;
            _bagText.rectTransform.offsetMax = Vector2.zero;

            // 可滚动的物品行容器
            GameObject viewport = UiKit.Panel("BagViewport", _bagPanel.transform, new Color(0f, 0f, 0f, 0.25f),
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 16f), new Vector2(-16f, -54f));
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            GameObject content = UiKit.Panel("BagRows", viewport.transform, new Color(0f, 0f, 0f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;
            _bagRowsContent = content.transform;
            _bagPanel.SetActive(false);

            // 角色
            _charPanel = UiKit.Panel("CharPanel", root, new Color(0.05f, 0.06f, 0.09f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300f, -220f), new Vector2(300f, 220f));
            _charText = UiKit.Text("", _charPanel.transform, 16, Color.white, TextAnchor.UpperLeft);
            _charText.rectTransform.offsetMin = new Vector2(16f, 16f);
            _charText.rectTransform.offsetMax = new Vector2(-16f, -16f);
            _charText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _charPanel.SetActive(false);

            // 任务日志
            _questPanel = UiKit.Panel("QuestPanel", root, new Color(0.05f, 0.06f, 0.09f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-340f, -240f), new Vector2(340f, 240f));
            _questText = UiKit.Text("", _questPanel.transform, 16, Color.white, TextAnchor.UpperLeft);
            _questText.rectTransform.offsetMin = new Vector2(16f, 16f);
            _questText.rectTransform.offsetMax = new Vector2(-16f, -16f);
            _questText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _questPanel.SetActive(false);

            // 拾取窗口（D-19：需要 / 掷骰子 / 放弃）
            _lootPanel = UiKit.Panel("LootPanel", root, new Color(0.08f, 0.07f, 0.05f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-260f, -420f), new Vector2(260f, -220f));
            _lootText = UiKit.Text("", _lootPanel.transform, 17, Color.white, TextAnchor.UpperCenter);
            _lootText.rectTransform.offsetMin = new Vector2(14f, 60f);
            _lootText.rectTransform.offsetMax = new Vector2(-14f, -14f);
            _lootText.horizontalOverflow = HorizontalWrapMode.Wrap;

            var needBtn = UiKit.Button("需要", _lootPanel.transform, new Vector2(-160f, -40f), new Vector2(150f, 44f), new Color(0.15f, 0.45f, 0.25f));
            needBtn.onClick.AddListener(() => SubmitLoot(LootChoice.Need));
            var greedBtn = UiKit.Button("掷骰子", _lootPanel.transform, new Vector2(0f, -40f), new Vector2(150f, 44f), new Color(0.4f, 0.35f, 0.15f));
            greedBtn.onClick.AddListener(() => SubmitLoot(LootChoice.Greed));
            var passBtn = UiKit.Button("放弃", _lootPanel.transform, new Vector2(160f, -40f), new Vector2(150f, 44f), new Color(0.35f, 0.18f, 0.18f));
            passBtn.onClick.AddListener(() => SubmitLoot(LootChoice.Pass));
            _lootPanel.SetActive(false);

            BuildDeathOverlay(root);
        }

        /// <summary>死亡遮罩：显示复活倒计时 + 立即复活按钮（缺陷 N-1）。</summary>
        private void BuildDeathOverlay(Transform root)
        {
            _deathPanel = UiKit.Panel("DeathOverlay", root, new Color(0.12f, 0.02f, 0.02f, 0.72f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300f, -120f), new Vector2(300f, 120f));

            _deathText = UiKit.Text("你已阵亡", _deathPanel.transform, 28, new Color(1f, 0.72f, 0.68f), TextAnchor.MiddleCenter);
            _deathText.rectTransform.offsetMin = new Vector2(20f, 46f);
            _deathText.rectTransform.offsetMax = new Vector2(-20f, -20f);

            var btn = UiKit.Button("立即复活（回车）", _deathPanel.transform, new Vector2(0f, -34f), new Vector2(240f, 48f), new Color(0.45f, 0.16f, 0.16f));
            btn.onClick.AddListener(RequestRespawn);
            _deathPanel.SetActive(false);
        }

        private void RequestRespawn()
        {
            if (_localPlayer == null) return;
            _localPlayer.RequestRespawn();
        }

        /// <summary>重建背包的物品行（点击即穿戴）。签名变化时才重建，避免每帧 new UI。</summary>
        private void RebuildBagRows()
        {
            if (_bagRowsContent == null || _localPlayer == null) return;

            IReadOnlyList<ItemInstance> bag = _localPlayer.Inventory.Bag;

            // 签名：物品数 + 已穿戴槽位，变化才重建
            int signature = bag.Count * 131;
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                int uid;
                if (_localPlayer.Inventory.TryGetEquipped(slot, out uid)) signature += uid * 7 + (int)slot;
            }
            if (signature == _bagRowSignature) return;
            _bagRowSignature = signature;

            for (int i = _bagRowsContent.childCount - 1; i >= 0; i--)
                Destroy(_bagRowsContent.GetChild(i).gameObject);

            for (int i = 0; i < bag.Count; i++)
            {
                ItemInstance inst = bag[i];
                ItemDef def = ItemTable.Default.Get(inst.DefId);
                if (def == null) continue;

                bool equipped = false;
                int equippedUid;
                if (def.Slot != EquipSlot.None && _localPlayer.Inventory.TryGetEquipped(def.Slot, out equippedUid))
                    equipped = equippedUid == inst.Uid;

                string label = def.Name + "  [" + ItemText.QualityName(def.Quality) + "]  " + ItemText.SlotName(def.Slot);
                if (equipped) label = "● " + label + "（已穿戴）";
                else if (def.Slot != EquipSlot.None) label = "▸ " + label + "　点击穿戴";

                var row = UiKit.Button(label, _bagRowsContent, Vector2.zero, new Vector2(0f, 40f),
                    equipped ? new Color(0.16f, 0.30f, 0.22f) : new Color(0.14f, 0.15f, 0.19f));
                row.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);

                LayoutElement le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;

                Text rowText = row.GetComponentInChildren<Text>();
                rowText.color = UiKit.QualityColor(def.Quality);
                rowText.fontSize = UiKit.Scaled(15);
                rowText.alignment = TextAnchor.MiddleLeft;

                int capturedUid = inst.Uid;
                if (def.Slot != EquipSlot.None && !equipped)
                {
                    row.onClick.AddListener(() =>
                    {
                        if (_localPlayer != null) _localPlayer.RequestEquip(capturedUid);
                        _bagRowSignature = -1; // 强制下次刷新重建
                    });
                }
                else
                {
                    row.interactable = false;
                }
            }
        }

        private void SubmitLoot(LootChoice choice)
        {
            if (_localPlayer != null) _localPlayer.RequestLootChoice(_lootSessionId, choice);
            _lootPanel.SetActive(false);
            ShowToast("已提交：" + (choice == LootChoice.Need ? "需要" : choice == LootChoice.Greed ? "掷骰子" : "放弃"));
        }

        // ------------------------------------------------------------ 绑定
        private void BindLocalPlayer(PlayerActor player)
        {
            _localPlayer = player;
            if (_localPlayer == null) return;

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                var camGo = new GameObject("Main Camera");
                mainCam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }
            _camera = mainCam.GetComponent<CameraFollow>();
            if (_camera == null) _camera = mainCam.gameObject.AddComponent<CameraFollow>();
            _camera.Target = player.transform;

            var input = player.GetComponent<PlayerInputController>();
            if (input == null) input = player.gameObject.AddComponent<PlayerInputController>();
            input.Player = player;

            RefreshSkillBar();
        }

        private void RefreshSkillBar()
        {
            if (_localPlayer == null) return;
            SkillId[] bar = SkillTable.SlotOrder(_localPlayer.Class);
            for (int i = 0; i < _slots.Count; i++)
            {
                SkillDef def = i < bar.Length ? SkillTable.Get(bar[i]) : null;
                _slots[i].Name.text = def != null ? def.Name : "--";
            }
        }

        /// <summary>联机模式下轮询本地玩家（由 PlayerSpawner 生成后写入 LocalPlayerLocator）。</summary>
        private void UpdateNetworkStatus()
        {
            INetSession session = NetSessionLocator.Current;
            if (session != null && session.Status == NetStatus.Failed && !string.IsNullOrEmpty(session.LastError))
                ShowToast(session.LastError);

            // 联机：本机玩家由网络层生成，UI 通过定位器绑定（UI 不引用网络框架）
            if (_localPlayer == null && LocalPlayerLocator.Current != null)
                BindLocalPlayer(LocalPlayerLocator.Current);

            HookWorldEvents();
        }

        /// <summary>订阅权威端的复活事件（缺陷 N-1）。CombatWorld 可能晚于 HUD 创建，故轮询订阅。</summary>
        private void HookWorldEvents()
        {
            if (_worldHooked) return;
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return;

            _worldHooked = true;
            world.OnPlayerRespawnCountdown += OnRespawnCountdown;
            world.OnPlayerRespawned -= OnPlayerRespawned;
            world.OnPlayerRespawned += OnPlayerRespawned;
            world.OnSilverGained += OnSilverGained;
            UiLootHooks.OnSilverGained += OnSilverGained;
        }

        public void OnRespawnCountdown(int playerActorId, float remaining)
        {
            if (_localPlayer == null || playerActorId != _localPlayer.ActorId) return;
            _respawnRemaining = remaining;
        }

        private void OnPlayerRespawned(int playerActorId, Vector3 point)
        {
            if (_localPlayer == null || playerActorId != _localPlayer.ActorId) return;
            _respawnRemaining = 0f;
            _deathShown = false;
            if (_deathPanel != null) _deathPanel.SetActive(false);
            ShowToast("已在复活点重生");
        }

        // ------------------------------------------------------------ 击杀飘字（渡银 +X）
        private sealed class FloatEntry
        {
            public Text Text;
            public float Age;
            public float BaseX;
            public float BaseY;
        }

        /// <summary>渡银到账飘字：仅本地玩家本人击杀/参战的份额才显示。</summary>
        public void OnSilverGained(int playerActorId, int silver, Vector3 worldPos)
        {
            if (_localPlayer == null || playerActorId != _localPlayer.ActorId) return;
            // 去重：主机（host）下同一渡银事件会经「CombatWorld 直连」与「NetWorldBridge ObserversRpc」两条路径到达，
            // 参数完全一致、仅到达时刻相差约 1 帧；按 (玩家, 数额, 取整坐标) 在 0.1s 内去重，避免重复飘字。
            int cx = (int)(worldPos.x * 7.3f), cy = (int)(worldPos.y * 13.1f), cz = (int)(worldPos.z * 5.7f);
            if (playerActorId == _lastSilverPlayer && silver == _lastSilverAmount
                && cx == _lastSilverX && cy == _lastSilverY && cz == _lastSilverZ
                && (Time.time - _lastSilverAt) < 0.1f)
                return;
            _lastSilverPlayer = playerActorId; _lastSilverAmount = silver;
            _lastSilverX = cx; _lastSilverY = cy; _lastSilverZ = cz; _lastSilverAt = Time.time;
            SpawnSilverFloat(worldPos, silver);
        }

        /// <summary>在击杀点上方生成一枚「渡银 +X」飘字，随后由 UpdateFloatingTexts 上飘并淡出。</summary>
        private void SpawnSilverFloat(Vector3 worldPos, int amount)
        {
            if (_floatLayer == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(worldPos + Vector3.up * 1.4f);
            if (screen.z <= 0f) return; // 击杀点在相机背后则跳过
            float sf = Screen.height / 1080f;
            float rx = (screen.x - Screen.width * 0.5f) / sf;
            float ry = (screen.y - Screen.height * 0.5f) / sf;
            rx += UnityEngine.Random.Range(-20f, 20f);
            ry += UnityEngine.Random.Range(-8f, 12f);
            Text t = UiKit.Text("渡银 +" + amount, _floatLayer, 22, new Color(0.98f, 0.86f, 0.42f), TextAnchor.MiddleCenter);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 46f);
            rt.anchoredPosition = new Vector2(rx, ry);
            _floats.Add(new FloatEntry { Text = t, Age = 0f, BaseX = rx, BaseY = ry });
        }

        /// <summary>每帧推进飘字：上飘 72 参考单位、1.15s 内淡出后销毁。</summary>
        private void UpdateFloatingTexts()
        {
            if (_floats.Count == 0) return;
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                FloatEntry f = _floats[i];
                if (f.Text == null) { _floats.RemoveAt(i); continue; }
                f.Age += Time.deltaTime;
                const float life = 1.15f;
                if (f.Age >= life)
                {
                    Destroy(f.Text.gameObject);
                    _floats.RemoveAt(i);
                    continue;
                }
                float k = f.Age / life;
                float y = f.BaseY + k * 72f;
                f.Text.rectTransform.anchoredPosition = new Vector2(f.BaseX, y);
                float a = k < 0.25f ? 1f : 1f - (k - 0.25f) / 0.75f;
                Color c = f.Text.color;
                c.a = Mathf.Clamp01(a);
                f.Text.color = c;
            }
        }

        /// <summary>死亡遮罩刷新：显示倒计时与「立即复活」。</summary>
        private void UpdateDeathOverlay()
        {
            if (_deathPanel == null || _localPlayer == null || _localPlayer.State == null) return;

            bool dead = Core.RespawnRules.IsActionBlocked(_localPlayer.State);
            if (dead)
            {
                if (!_deathShown)
                {
                    _deathShown = true;
                    ShowToast("你已阵亡，等待复活…");
                }
                _deathPanel.SetActive(true);
                float remain = _localPlayer.State.RespawnTimer > 0f ? _localPlayer.State.RespawnTimer : 0f;
                _deathText.text = "你已阵亡\n\n" + remain.ToString("0.0") + " 秒后自动复活\n（复活点：最近的村落复活点）";
            }
            else if (_deathPanel.activeSelf)
            {
                _deathPanel.SetActive(false);
                _deathShown = false;
            }
        }

        private void UpdateHud()
        {
            if (_localPlayer == null || _localPlayer.State == null) return;
            ActorState s = _localPlayer.State;

            _hpFill.fillAmount = s.HpMax > 0f ? s.Hp / s.HpMax : 0f;
            _hpText.text = Mathf.CeilToInt(s.Hp) + " / " + Mathf.CeilToInt(s.HpMax);
            _resourceFill.fillAmount = s.ResourceMax > 0f ? s.Resource / s.ResourceMax : 0f;
            _resourceText.text = Mathf.CeilToInt(s.Resource) + " / " + Mathf.CeilToInt(s.ResourceMax) +
                (s.ResourceId == ResourceType.Qi ? " 剑气" : s.ResourceId == ResourceType.Rage ? " 怒气" : "");
            _xpFill.fillAmount = _localPlayer.Progression.ProgressRatio();
            _levelText.text = "Lv." + _localPlayer.Progression.Level + "  " + s.Name;
            _silverText.text = "渡银 " + _localPlayer.Silver;

            // 目标框
            ActorEntity target = _localPlayer.Target;
            bool hasTarget = target != null && target.State != null && !target.State.Dead;
            _targetFrame.SetActive(hasTarget);
            if (hasTarget)
            {
                _targetNameText.text = target.DisplayName + "  Lv." + target.State.Level;
                _targetHpFill.fillAmount = target.HealthRatio();
            }

            // 技能栏：冷却扫边 + 数字倒计时双通道（D-12）
            SkillId[] bar = SkillTable.SlotOrder(s.Class);
            for (int i = 0; i < _slots.Count; i++)
            {
                SkillDef def = i < bar.Length ? SkillTable.Get(bar[i]) : null;
                if (def == null) continue;
                float cd = s.CooldownOf(def.Slot);
                float localCd;
                if (_localPlayer.LocalCooldowns.TryGetValue((int)def.Slot, out localCd) && localCd > cd) cd = localCd;

                float ratio = def.Cooldown > 0f ? cd / def.Cooldown : 0f;
                _slots[i].CooldownMask.fillAmount = ratio > 0f ? ratio : 0f;
                _slots[i].CooldownText.text = cd > 0.05f ? cd.ToString("0.0") : "";

                // 需要姿态 / 资源不足 → 置灰提示（GDD §5.4 E-19）
                bool unusable = (def.RequiredStance != StanceId.None && !s.Buffs.InStance(def.RequiredStance))
                    || (def.ResourceDelta < 0f && s.Resource + def.ResourceDelta < 0f);
                _slots[i].Name.color = unusable ? new Color(0.55f, 0.55f, 0.58f) : Color.white;
            }

            // 增益/减益（非颜色通道：文字列出）
            _buffText.text = BuildBuffText(s);

            // 任务追踪
            _questTracker.text = BuildQuestTracker();

            // 死亡遮罩（缺陷 N-1）
            UpdateDeathOverlay();

            if (_bagPanel.activeSelf) RefreshBagPanel();
            if (_charPanel.activeSelf) RefreshCharacterPanel();
            if (_questPanel.activeSelf) RefreshQuestPanel();
        }

        private static string BuildBuffText(ActorState s)
        {
            string text = "";
            BuffInstance[] buffs = s.Buffs.Snapshot();
            for (int i = 0; i < buffs.Length; i++)
            {
                text += BuffDisplayName(buffs[i].Id);
                if (buffs[i].Stacks > 1) text += " ×" + buffs[i].Stacks;
                text += " (" + buffs[i].Remaining.ToString("0.0") + "s)\n";
            }
            if (s.Control != ControlKind.None)
                text += "受控：" + ControlName(s.Control) + " (" + s.ControlRemaining.ToString("0.0") + "s)\n";
            return text;
        }

        private static string BuffDisplayName(BuffId id)
        {
            switch (id)
            {
                case BuffId.YuJianStance: return "御剑姿态";
                case BuffId.QingFengKBonus: return "流云步·剑势";
                case BuffId.DuanYueArmorBreak: return "护甲破损";
                case BuffId.YingLingStance: return "英灵战躯";
                case BuffId.LieGuArmorBreak: return "裂骨破甲";
                case BuffId.WarCryHaste: return "战吼·攻速";
                case BuffId.Slow: return "减速";
                case BuffId.Silence: return "沉默";
                default: return id.ToString();
            }
        }

        private static string ControlName(ControlKind kind)
        {
            switch (kind)
            {
                case ControlKind.Stun: return "定身";
                case ControlKind.Fear: return "恐惧";
                case ControlKind.Slow: return "减速";
                case ControlKind.Silence: return "沉默";
                default: return "—";
            }
        }

        private string BuildQuestTracker()
        {
            if (_localPlayer == null) return "";
            string text = "任务追踪\n";
            foreach (QuestProgress p in _localPlayer.Quests.Active)
            {
                QuestDef def = QuestTable.Default.Get(p.QuestId);
                if (def == null) continue;
                text += "· " + def.Name + "  " + p.Count + "/" + def.RequiredCount +
                        (p.IsComplete(def) ? "  【可交付】" : "") + "\n";
            }
            if (_localPlayer.Quests.Active.Count == 0) text += "（暂无进行中任务，按 E 与 NPC 对话接取）";
            return text;
        }

        /// <summary>背包面板：表头给统计，物品明细走可点击行（点击穿戴 —— 缺陷 N-2）。</summary>
        private void RefreshBagPanel()
        {
            IReadOnlyList<ItemInstance> bag = _localPlayer.Inventory.Bag;

            var sb = new System.Text.StringBuilder();
            sb.Append("背包（B 关闭）　物品 ").Append(bag.Count).Append(" / 40　· 点击物品行即可穿戴");
            if (_localPlayer.State != null)
            {
                sb.Append("　| 攻强 ").Append(_localPlayer.State.PhysicalPower().ToString("0.0"))
                  .Append("　护甲 ").Append(_localPlayer.State.EffectiveArmor().ToString("0.0"));
            }
            _bagText.text = sb.ToString();

            RebuildBagRows();
        }

        private void RefreshCharacterPanel()
        {
            ActorState s = _localPlayer.State;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("角色（C 关闭）");
            sb.AppendLine();
            sb.Append("姓名：").Append(s.Name).Append("    职业：")
              .Append(s.Class == ClassId.SwordCultivator ? "剑修" : "狂战士")
              .Append("    性别：").AppendLine(_localPlayer.Female ? "女" : "男");
            sb.Append("等级：").Append(_localPlayer.Progression.Level)
              .Append("    经验：").Append(_localPlayer.Progression.Xp)
              .Append(" / ").AppendLine(Progression.XpToNext(_localPlayer.Progression.Level).ToString());
            sb.AppendLine();
            sb.Append("生命：").Append(Mathf.CeilToInt(s.Hp)).Append(" / ").AppendLine(Mathf.CeilToInt(s.HpMax).ToString());
            sb.Append("资源：").Append(Mathf.CeilToInt(s.Resource)).Append(" / ").AppendLine(Mathf.CeilToInt(s.ResourceMax).ToString());
            sb.AppendLine();
            sb.Append("力量 ").Append(s.BaseAttributes.Strength).Append("   敏捷 ").Append(s.BaseAttributes.Agility)
              .Append("   智识 ").Append(s.BaseAttributes.Intellect).Append("   体魄 ").Append(s.BaseAttributes.Vitality)
              .Append("   意志 ").AppendLine(s.BaseAttributes.Will.ToString());
            sb.AppendLine();
            sb.Append("攻击强度 P_AP：").AppendLine(s.PhysicalPower().ToString("0.0"));
            sb.Append("武器 DPS W：").Append(s.WeaponDps).Append("    武器速度 S：").AppendLine(s.WeaponSpeed.ToString());
            sb.Append("护甲：").Append(s.EffectiveArmor().ToString("0.0"))
              .Append("    护甲减伤：").AppendLine((s.ArmorMitigation() * 100f).ToString("0.0") + "%");
            sb.Append("总减伤：").AppendLine((s.TotalIncomingMitigation() * 100f).ToString("0.0") + "%");
            sb.AppendLine();
            sb.AppendLine("已穿戴：");
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                int uid;
                if (!_localPlayer.Inventory.TryGetEquipped(slot, out uid)) continue;
                ItemInstance inst = _localPlayer.Inventory.Find(uid);
                if (inst == null) continue;
                ItemDef def = ItemTable.Default.Get(inst.DefId);
                if (def == null) continue;
                sb.Append("  ").Append(ItemText.SlotName(slot)).Append("：").AppendLine(def.Name);
            }
            _charText.text = sb.ToString();
        }

        private void RefreshQuestPanel()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("任务日志（L 关闭）");
            sb.AppendLine();
            sb.AppendLine("进行中：");
            foreach (QuestProgress p in _localPlayer.Quests.Active)
            {
                QuestDef def = QuestTable.Default.Get(p.QuestId);
                if (def == null) continue;
                sb.Append("· ").Append(def.Name).Append("（Lv.").Append(def.Level).Append("）").AppendLine();
                sb.Append("  ").AppendLine(def.Description);
                sb.Append("  进度 ").Append(p.Count).Append("/").Append(def.RequiredCount)
                  .Append(p.IsComplete(def) ? "  → 找 NPC 按 E 交付" : "").AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("已完成：");
            foreach (QuestDef def in QuestTable.Default.All())
            {
                if (!_localPlayer.Quests.IsDone(def.Id)) continue;
                sb.Append("· ").AppendLine(def.Name);
            }
            _questText.text = sb.ToString();
        }

        private void HandleHotkeys()
        {
            // 阵亡时回车 = 立即复活（缺陷 N-1）
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_localPlayer != null && _localPlayer.IsAwaitingRespawn) RequestRespawn();
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                _bagPanel.SetActive(!_bagPanel.activeSelf);
                if (_bagPanel.activeSelf) _bagRowSignature = -1; // 打开时强制重建物品行
            }
            if (Input.GetKeyDown(KeyCode.C)) _charPanel.SetActive(!_charPanel.activeSelf);
            if (Input.GetKeyDown(KeyCode.L)) _questPanel.SetActive(!_questPanel.activeSelf);
            if (Input.GetKeyDown(KeyCode.F1)) ShowToast("操作：WASD 移动 · Q 左转视角 / 右键拖拽自由转 · 左键或 Tab 选目标 · 1–5 小技能 · R 终极 · F 变身 · E 交互/接任务 · B 背包 · C 角色 · L 任务");
        }

        private void UpdateToast()
        {
            if (_toastTimer <= 0f) return;
            _toastTimer -= Time.deltaTime;
            if (_toastTimer > 0f) return;
            _toast.transform.parent.gameObject.SetActive(false);
        }

        public void ShowToast(string message)
        {
            if (_toast == null) return;
            _toast.transform.parent.gameObject.SetActive(true);
            _toast.text = message;
            _toastTimer = 4f;
        }

        // ------------------------------------------------------------ 供外部注入
        public void SetLocalPlayer(PlayerActor player)
        {
            BindLocalPlayer(player);
        }

        /// <summary>由网络层转发：拾取窗口开启。</summary>
        public void OnLootStarted(int sessionId, int itemDefId)
        {
            _lootSessionId = sessionId;
            _lootItemDefId = itemDefId;
            ItemDef def = ItemTable.Default.Get(itemDefId);
            string name = def != null ? def.Name : ("物品#" + itemDefId);
            string quality = def != null ? ItemText.QualityName(def.Quality) : "—";
            _lootText.text = "掉落：" + name + "\n品质：" + quality + "\n（组队产出：拾取绑定 BoP · D-19）\n请选择：";
            _lootPanel.SetActive(true);
            ShowToast("掉落 " + name + "，请在拾取窗口选择");
        }

        public void OnLootResolved(int winnerId, int itemDefId, LootChoice tier)
        {
            _lootPanel.SetActive(false);
            ItemDef def = ItemTable.Default.Get(itemDefId);
            string name = def != null ? def.Name : ("物品#" + itemDefId);
            if (winnerId == 0)
            {
                ShowToast("无人认领，" + name + " 已销毁");
                return;
            }
            string who = _localPlayer != null && _localPlayer.ActorId == winnerId ? "你" : ("玩家#" + winnerId);
            ShowToast(who + " 以「" + (tier == LootChoice.Need ? "需要" : tier == LootChoice.Greed ? "掷骰子" : "放弃") + "」获得 " + name);
        }
    }

    /// <summary>
    /// 网络层 → UI 的事件桥安装器。由 Fushengdu.Network.FishNet 在运行时调用其静态方法。
    /// 这样网络程序集不需要引用 UI 程序集（保持 ADR-001 N-8 的单向依赖）。
    /// </summary>
    public static class UiHooksInstaller
    {
        private static bool _installed;

        /// <summary>把网络抽象层的事件转接到 HUD（单向依赖：UI → 抽象层）。</summary>
        public static void Install(GameHud hud)
        {
            if (hud == null || _installed) return;
            _installed = true;
            UiLootHooks.OnLootStarted += (sessionId, itemDefId, position) => hud.OnLootStarted(sessionId, itemDefId);
            UiLootHooks.OnLootResolved += (winnerId, itemDefId, tier) => hud.OnLootResolved(winnerId, itemDefId, (LootChoice)tier);
            UiLootHooks.OnItemGranted += (playerId, itemDefId) =>
            {
                ItemDef def = ItemTable.Default.Get(itemDefId);
                hud.ShowToast("获得 " + (def != null ? def.Name : ("物品#" + itemDefId)));
            };
            UiLootHooks.OnLevelUp += (playerId, level) => hud.ShowToast("升级！当前等级 " + level);
            UiLootHooks.OnRespawnCountdown += (playerId, remaining) => hud.OnRespawnCountdown(playerId, remaining);
        }
    }
}
