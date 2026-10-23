// 《浮生渡》编辑器工具 · 打包独立播放器（自带启动自检 + 失败重打）
// 用法（Windows）：
//   Unity.exe -batchmode -quit -projectPath <client> ^
//     -executeMethod Fushengdu.Editor.PlayerBuilder.Build ^
//     -fdBuildOut D:\AI_Workspace\fushengdu_build
//
// 【为什么要自检】
// batchmode 下 BuildPipeline.BuildPlayer 有时会在资产库尚未完全就绪时出包，
// 报告 Succeeded，但产出的 level0 是坏的；播放器一启动就
//   "The file '.../Fushengdu_Data/level0' is corrupted! ... [Position out of bounds!]" 崩溃（exit 3）。
// 这种坏包只有真正把它跑起来才发现得了，所以这里在打包后立刻用无窗口模式
// 启动一次播放器做冒烟测试；不通过就清目录重打，最多 MaxAttempts 次。
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class PlayerBuilder
    {
        private const int MaxAttempts = 4;

        [MenuItem("浮生渡/6. 打包独立播放器（Win64）", priority = 60)]
        public static void Build()
        {
            string outDir = ArgValue("-fdBuildOut", "D:/AI_Workspace/fushengdu_build");
            string exeName = ArgValue("-fdBuildExe", "Fushengdu.exe");

            // 打包前强制同步导入，尽可能减少"资产库没就绪就出包"的概率
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            var scenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s != null && s.enabled && !string.IsNullOrEmpty(s.path)) scenes.Add(s.path);
            }
            if (scenes.Count == 0) scenes.Add("Assets/Scenes/NewcomerZone.unity");

            string exePath = outDir.TrimEnd('/') + "/" + exeName;

            bool smoke = ArgValue("-fdNoSmoke", "") != "1";

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var opts = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(opts);
                BuildSummary sum = report.summary;
                Debug.Log(string.Format("[浮生渡] 打包结果({0}/{1}): {2} · 大小 {3} MB · 耗时 {4}s",
                    attempt, MaxAttempts, sum.result,
                    (sum.totalSize / 1048576.0).ToString("F1"),
                    sum.totalTime.TotalSeconds.ToString("F1")));

                if (sum.result != BuildResult.Succeeded)
                {
                    DeleteOut(outDir);
                    continue;
                }

                if (!smoke)
                {
                    Debug.Log("[浮生渡] 已跳过启动自检，产物：" + exePath);
                    return;
                }

                if (SmokeTest(exePath, attempt))
                {
                    Debug.Log("[浮生渡] 播放器启动自检通过：" + exePath);
                    return;
                }

                Debug.LogWarning("[浮生渡] 第 " + attempt + " 次打包产出的播放器无法正常启动，清目录重打…");
                DeleteOut(outDir);
            }

            Debug.LogError("[浮生渡] 连续 " + MaxAttempts + " 次打包均未通过启动自检，请检查场景资产");
            EditorApplication.Exit(1);
        }

        /// <summary>无窗口启动一次播放器：能进场景、能出截图、退出码为 0 才算通过。</summary>
        private static bool SmokeTest(string exePath, int attempt)
        {
            if (!File.Exists(exePath)) return false;

            string root = Path.GetDirectoryName(exePath);
            string shot = Path.Combine(root, "smoke_" + attempt + ".png");
            string log = Path.Combine(root, "smoke_" + attempt + ".log");
            if (File.Exists(shot)) { try { File.Delete(shot); } catch (Exception) { } }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-fdSolo -fdShot \"" + shot + "\" -fdShotDelay 4 -fdQuit -logFile \"" + log + "\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(90000)) { try { p.Kill(); } catch (Exception) { } return false; }
                    if (p.ExitCode != 0) return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[浮生渡] 启动自检异常：" + e.Message);
                return false;
            }

            // 截图必须真的落盘：坏包往往在读取场景阶段就崩了，根本走不到截图逻辑
            return File.Exists(shot) && new FileInfo(shot).Length > 4096;
        }

        private static void DeleteOut(string outDir)
        {
            try
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[浮生渡] 清理输出目录失败：" + e.Message);
            }
        }

        private static string ArgValue(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return fallback;
        }
    }
}
