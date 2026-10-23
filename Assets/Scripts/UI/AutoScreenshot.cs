// 《浮生渡》UI 层 · 自动截图（无人值守验证用）
// 目的：CI / 冒烟验证需要"看到真实画面"，而不是只看日志说它没崩。
// 参数：
//   -fdShot <path>      延迟若干秒后写入一张 PNG（默认延迟 6 秒）
//   -fdShotDelay <秒>   自定义延迟
//   -fdQuit             截图完成后自动退出（批处理场景用）
// 不带任何参数时完全不执行，不影响正常游玩。
using System.Collections;
using UnityEngine;

namespace Fushengdu.UI
{
    public sealed class AutoScreenshot : MonoBehaviour
    {
        private string _path;
        private float _delay = 6f;
        private bool _quit;

        private void Awake()
        {
            string[] args;
            try
            {
                args = System.Environment.GetCommandLineArgs();
            }
            catch
            {
                return;
            }
            if (args == null) return;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-fdShot":
                        _path = (i + 1 < args.Length && !args[i + 1].StartsWith("-")) ? args[++i] : "";
                        break;
                    case "-fdShotDelay":
                        if (i + 1 < args.Length)
                        {
                            float v;
                            if (float.TryParse(args[i + 1], out v)) _delay = v;
                            i++;
                        }
                        break;
                    case "-fdQuit":
                        _quit = true;
                        break;
                }
            }

            if (string.IsNullOrEmpty(_path)) return;
            Debug.Log("[浮生渡] 自动截图已排队：" + _path + " 延迟 " + _delay + " 秒");
            StartCoroutine(CaptureAfterDelay());
        }

        private IEnumerator CaptureAfterDelay()
        {
            yield return new WaitForSeconds(_delay);
            ScreenCapture.CaptureScreenshot(_path, 1);
            Debug.Log("[浮生渡] 已写入截图：" + _path);
            yield return new WaitForSeconds(1.2f);

            if (_quit)
            {
                Application.Quit();
            }
        }
    }
}
