// 《浮生渡》编辑器工具 · 导入并验证 Mixamo 动画资产
//
// 【背景】动画素材在旧项目（玄天问道）里已经下好：19 个纯骨骼 FBX（Mixamo 私有 API 导出，
// skin=false / fbx7 / 30fps）。本工具把它们复制进本项目、按 Humanoid 导入、验证 Avatar 有效、
// 并列出每个文件里的 clip，为后续 Animator 做准备。
//
// 【为什么必须是 Humanoid】Mixamo 骨架（mixamorig:*，65 骨）导入 Unity 后自动识别为 Humanoid；
// Humanoid 动画可以重定向到**任何** Humanoid 角色——这是"用一套动画驱动所有职业"的前提。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.MixamoImport.Run -logFile <log>
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class MixamoImport
    {
        private const string SrcDir = "D:/游戏1/prototype3d/assets/mixamo/anim";
        private const string DstDir = "Assets/Art/Animations/Mixamo";

        // 需要循环播放的 locomotion 片段（按文件名判断；攻击/受技/死亡不循环）
        private static readonly HashSet<string> LoopNames = new HashSet<string>
        {
            "idle", "walk", "run", "combat_idle"
        };

        [MenuItem("浮生渡/9. 导入 Mixamo 动画", priority = 90)]
        public static void Run()
        {
            if (!Directory.Exists(SrcDir))
            {
                Debug.LogError("[Mixamo] 源目录不存在：" + SrcDir);
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(DstDir);
            int copied = 0, skipped = 0;
            foreach (string f in Directory.GetFiles(SrcDir, "*.fbx"))
            {
                string dst = Path.Combine(DstDir, Path.GetFileName(f));
                if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(f).Length) { skipped++; continue; }
                File.Copy(f, dst, true);
                copied++;
            }
            Debug.Log(string.Format("[Mixamo] 复制 {0} 个，跳过 {1} 个（已存在且大小一致）", copied, skipped));

            // 复制完必须刷新：新文件还没进资产数据库，不刷新 FindAssets 会报 Folder not found
            AssetDatabase.Refresh();

            int ok = 0, bad = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { DstDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;

                string name = Path.GetFileNameWithoutExtension(path);
                string before = imp.animationType.ToString();

                // —— Humanoid：Mixamo 标准骨架，Unity 自动配骨
                imp.animationType = ModelImporterAnimationType.Human;
                imp.importAnimation = true;
                imp.optimizeGameObjects = false;   // 保留骨骼层级，重定向更稳
                imp.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;

                // —— 循环设置
                var clips = new List<ModelImporterClipAnimation>(imp.defaultClipAnimations);
                bool loop = LoopNames.Contains(name);
                for (int i = 0; i < clips.Count; i++)
                {
                    var c = clips[i];
                    c.loopTime = loop;
                    c.loopPose = loop;
                    c.keepOriginalOrientation = false;
                    c.keepOriginalPositionY = false;
                    c.lockRootRotation = false;
                    c.lockRootHeightY = false;
                    clips[i] = c;
                }
                if (clips.Count > 0) imp.clipAnimations = clips.ToArray();

                imp.SaveAndReimport();

                // —— 重导入后验证
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var anim = go != null ? go.GetComponent<Animator>() : null;
                var avatar = anim != null ? anim.avatar : null;
                bool human = avatar != null && avatar.isValid && avatar.isHuman;
                var after = imp.defaultClipAnimations;

                string clipInfo = "";
                for (int i = 0; i < after.Length && i < 3; i++)
                    clipInfo += string.Format("{0}({1:F1}s) ", after[i].takeName, after[i].lastFrame / 30f);

                if (human) ok++; else bad++;
                Debug.Log(string.Format("[Mixamo] {0,-16} rig {1}→{2} avatar={3} human={4} clips={5} [{6}]",
                    name, before, imp.animationType,
                    avatar != null ? (avatar.isValid ? "valid" : "INVALID") : "null",
                    human, after.Length, clipInfo));
            }

            AssetDatabase.SaveAssets();
            Debug.Log(string.Format("[Mixamo] 完成：{0} 个 Humanoid 有效，{1} 个异常", ok, bad));
            EditorApplication.Exit(bad > 0 ? 1 : 0);
        }
    }
}
