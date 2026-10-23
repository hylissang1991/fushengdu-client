// 《浮生渡》编辑器工具 · 修正 Mixamo 动画的 root motion 提取设置
//
// 问题现象：静止姿势骨架正常（Hip y=0.505），一播动画整具骨架下沉 0.53m
// （Hip → -0.026、L_Foot → -0.511），网格被撕成碎片。
//
// 根因：Mixamo FBX 默认把 hips 的垂直/水平位移与朝向提取成 root motion。
// 而我们的角色是"纯骨骼 + 程序化网格"，Animator 的 applyRootMotion 关闭，
// 被提取出去的位移就无处可去 —— hips 直接塌回原点，脚穿到地面以下半米。
//
// 修法：把这些位移与朝向 **烘焙进姿势**（Bake Into Pose），骨架内部保持正确高度，
// 脚仍然落在地面（不需要 root motion 驱动位移，我们靠 CharacterController 走路）。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class FixClipRootMotion
    {
        private const string MixamoDir = "Assets/Art/Animations/Mixamo";

        [MenuItem("浮生渡/20. 修正动画 root motion（Bake Into Pose）")]
        public static void Run()
        {
            string[] files = Directory.GetFiles(MixamoDir, "*.fbx");
            int fixedCount = 0, skipped = 0;

            foreach (string f in files)
            {
                string path = f.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { Debug.LogWarning("[FixClip] 不是 ModelImporter：" + path); skipped++; continue; }

                var clips = imp.defaultClipAnimations;
                if (clips == null || clips.Length == 0) { skipped++; continue; }

                bool changed = false;
                foreach (var c in clips)
                {
                    // 朝向 / 高度 / 水平位移：全部烘焙进姿势，不提取为 root motion
                    if (!c.lockRootRotation) { c.lockRootRotation = true; changed = true; }
                    c.keepOriginalOrientation = true;

                    if (!c.lockRootHeightY) { c.lockRootHeightY = true; changed = true; }
                    c.keepOriginalPositionY = true;
                    c.heightFromFeet = false;      // Based Upon = Original

                    if (!c.lockRootPositionXZ) { c.lockRootPositionXZ = true; changed = true; }
                    c.keepOriginalPositionXZ = true;
                }

                if (changed)
                {
                    imp.clipAnimations = clips;
                    imp.SaveAndReimport();
                    fixedCount++;
                }
            }

            Debug.Log(string.Format("[FixClip] 完成：{0} 个 FBX 已改为 Bake Into Pose，{1} 个无需/无法处理",
                fixedCount, skipped));
            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }
    }
}
