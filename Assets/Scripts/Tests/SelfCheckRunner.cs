// 《浮生渡》核心规则自检运行器
// 背景：本机 Unity 命令行 Test Runner（Unity 6000.6.7f1 · 国际版）执行
//   -runTests -testPlatform EditMode 时报告「No tests were executed」（发现 0 个用例），
//   但 Fushengdu.Core.Tests.dll 本身编译通过。为不阻塞验证，本文件提供一条**可执行**的自检入口：
//   反射扫描本程序集内所有 [TestFixture] / [Test] 并逐个执行，输出报告。
// 用法（命令行）：
//   Unity.exe -batchmode -quit -projectPath <工程> \
//             -executeMethod Fushengdu.Core.Tests.SelfCheckRunner.RunAll
// 报告输出：工程同级 Logs/core-selfcheck.txt（同时写入 Unity 控制台）
// @trace ADR-001 N-9（所有提交必须附测试证据路径）
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Core.Tests
{
    public static class SelfCheckRunner
    {
        [MenuItem("浮生渡/4. 运行核心规则自检", priority = 40)]
        public static void RunAllMenu()
        {
            RunAll();
        }

        public static void RunAll()
        {
            var report = new StringBuilder();
            int passed = 0, failed = 0;
            var failures = new List<string>();

            Assembly asm = typeof(SelfCheckRunner).Assembly;
            Type[] types = asm.GetTypes();

            report.AppendLine("# 《浮生渡》核心规则自检报告");
            report.AppendLine("# 程序集：" + asm.GetName().Name);
            report.AppendLine("# 时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();

            Array.Sort(types, (a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));

            foreach (Type t in types)
            {
                object[] fixtures = t.GetCustomAttributes(typeof(TestFixtureAttribute), false);
                if (fixtures.Length == 0) continue;

                report.AppendLine("## " + t.Name);

                MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                Array.Sort(methods, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

                foreach (MethodInfo m in methods)
                {
                    object[] tests = m.GetCustomAttributes(typeof(TestAttribute), false);
                    if (tests.Length == 0) continue;

                    try
                    {
                        object instance = Activator.CreateInstance(t);
                        m.Invoke(instance, null);
                        passed++;
                        report.AppendLine("  PASS  " + m.Name);
                    }
                    catch (Exception ex)
                    {
                        Exception inner = ex is TargetInvocationException ? ex.InnerException : ex;
                        failed++;
                        string line = "  FAIL  " + m.Name + "  →  " + (inner != null ? inner.Message : ex.Message);
                        failures.Add(line);
                        report.AppendLine(line);
                    }
                }
                report.AppendLine();
            }

            report.AppendLine("# 汇总：通过 " + passed + " / 失败 " + failed + " / 合计 " + (passed + failed));

            // 输出目录：工程根下的 Logs（即 <工程>/Logs/）
            string path = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs"));
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "core-selfcheck.txt"), report.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[自检] 报告写入失败：" + ex.Message);
            }

            UnityEngine.Debug.Log("[浮生渡] 核心规则自检：通过 " + passed + "，失败 " + failed + "，合计 " + (passed + failed));
            foreach (string f in failures) UnityEngine.Debug.LogError("[自检] " + f);

            // 供 CI 判定：失败则让 batchmode 返回非 0
            if (failed > 0)
                EditorApplication.Exit(1);
        }
    }
}
#endif
