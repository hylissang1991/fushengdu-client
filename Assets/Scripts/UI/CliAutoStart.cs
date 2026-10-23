// 《浮生渡》UI 层 · 命令行自启动入口
// 目的：为端到端验证提供一个「不需要人点 UI」的启动通道。
// 本机只有一台机器，无法靠两台客户端互测 LAN 发现（缺陷 N-3 的最后一块拼图），
// 因此用 `-fdHost` 让真实游戏进程在本机应答 UDP 47770，再用外部扫描脚本验证。
//
// 【关键约束】自启动**不另起一套逻辑**，只调用 GameHud.HostSession / JoinSession，
// 与玩家点「创建主机 / 加入」按钮走的是同一条代码路径 —— 保证测的就是真实路径。
//
// 参数：
//   -fdSolo              → 跳过启动器，用默认角色（剑修 / 男 / 默认名）直接进单机离线模式
//   -fdHost              → 跳过启动器，用默认角色（剑修 / 男 / 默认名）创建主机（LAN）
//   -fdJoin <ip>         → 跳过启动器，用默认角色加入指定 IP（端口取默认 7770）
//   -fdRoom <name>       → 房间名（可选；含 | 或换行时会被 LanBeacon 净化，用于验证净化逻辑）
// 未带上述任何参数时行为完全不变（仍显示原启动器 UI）。
//
// 用法（Windows 独立播放器）：
//   Fushengdu.exe -fdHost -fdRoom 测试房
//   Fushengdu.exe -fdJoin 192.168.1.3
using Fushengdu.Network.Abstraction;
using UnityEngine;

namespace Fushengdu.UI
{
    public sealed class CliAutoStart : MonoBehaviour
    {
        private enum AutoMode
        {
            None = 0, // 未指定：完全保持原行为
            Host = 1,
            Join = 2,
            Solo = 3
        }

        private AutoMode _mode = AutoMode.None;
        private string _address = "";
        private string _room = "";
        private int _port = NetLaunchSettings.Port;

        /// <summary>是否启用了命令行自启动（供其它组件/日志判断）。</summary>
        public static bool Enabled { get; private set; }

        private void Awake()
        {
            ParseCommandLine();
        }

        private void Start()
        {
            if (_mode == AutoMode.None) return;

            GameHud hud = GetComponent<GameHud>();
            if (hud == null) hud = FindObjectOfType<GameHud>();
            if (hud == null)
            {
                Debug.LogError("[浮生渡] 命令行自启动失败：场景中未找到 GameHud");
                return;
            }

            // 默认角色：剑修 / 男 / 默认名（不读 UI 控件，保证无人工干预也能跑）
            hud.ApplyDefaultSettings();
            NetLaunchSettings.RoomName = _room;
            NetLaunchSettings.Port = _port;

            string modeText = _mode == AutoMode.Host ? "创建主机"
                : (_mode == AutoMode.Solo ? "单机离线" : "加入主机");
            Debug.Log("[浮生渡] 命令行自启动：模式=" + modeText
                      + " 地址=" + (_mode == AutoMode.Host ? "(本机)" : _address)
                      + " 端口=" + _port + " 房间=" + _room);

            if (_mode == AutoMode.Host) hud.HostSession(_port);
            else if (_mode == AutoMode.Solo) hud.StartSolo();
            else hud.JoinSession(_address, _port);
        }

        private void ParseCommandLine()
        {
            string[] args;
            try
            {
                args = System.Environment.GetCommandLineArgs();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[浮生渡] 读取命令行参数失败：" + ex.Message);
                return;
            }

            if (args == null || args.Length <= 1) return;

            // 完整回显，便于排查「参数到底有没有传进来」
            Debug.Log("[浮生渡] 启动参数：" + string.Join(" ", args));

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a)) continue;

                switch (a)
                {
                    case "-fdHost":
                        _mode = AutoMode.Host;
                        Enabled = true;
                        break;

                    case "-fdSolo":
                        _mode = AutoMode.Solo;
                        Enabled = true;
                        break;

                    case "-fdJoin":
                        _mode = AutoMode.Join;
                        Enabled = true;
                        // 取下一个参数作为 IP；缺失则回落本机回环地址
                        _address = (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            ? args[i + 1]
                            : NetLaunchSettings.Address;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) i++;
                        break;

                    case "-fdRoom":
                        if (i + 1 < args.Length)
                        {
                            _room = args[i + 1];
                            i++;
                        }
                        break;
                }
            }
        }
    }
}
