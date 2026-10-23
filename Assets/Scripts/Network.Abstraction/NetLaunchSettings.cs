// 《浮生渡》网络抽象层 · 启动参数（角色选择 / 端口 / 地址）
// UI 写入本静态类，网络适配层读取；避免 UI 直接依赖网络框架类型（ADR-001 N-8）。
namespace Fushengdu.Network.Abstraction
{
    public static class NetLaunchSettings
    {
        public static int ClassId = 1;            // Fushengdu.Core.ClassId
        public static bool Female;
        public static string PlayerName = "无名渡者";
        public static int StartLevel = 1;

        public static int Port = 7770;
        public static string Address = "127.0.0.1";

        /// <summary>
        /// 房间名（LAN 广播中对外显示）。为空时由网络层取「浮生渡-设备名」。
        /// 命令行 `-fdRoom &lt;name&gt;` 写入本字段，用于验证房间名净化（含 | / 换行 时会被替换）。
        /// </summary>
        public static string RoomName = "";

        /// <summary>本机存档的角色名（联机时用于恢复进度）。</summary>
        public static bool UseLocalSave = true;
    }
}
