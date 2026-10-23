// 《浮生渡》编辑器工具 · 给 rig 角色建 Animator Controller 并接上 Mixamo 动画
//
// 【状态机】按 MMO 表现需要的最小集合：
//   Idle ←→ Walk ←→ Run（Speed  float 混合过渡）
//   Any State --Attack(trigger)--> Atk（三段攻击随机挑一个）
//   Any State --Hit(trigger)--->   HitReaction
//   Any State --Dead(trigger)-->   Dead
// 过渡条件全用 Speed/Trigger，玩法侧只需 animator.SetFloat("Speed", x) / SetTrigger("Attack")。
//
// 用法：Unity.exe -batchmode -quit -projectPath <proj>
//       -executeMethod Fushengdu.Editor.AnimatorSetup.Run -logFile <log>
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class AnimatorSetup
    {
        private const string MixamoDir = "Assets/Art/Animations/Mixamo";
        private const string RigDir = "Assets/Resources/Art/Characters/rig";
        private const string ControllerDir = "Assets/Resources/Art/Characters/rig/Controllers";

        // 本地状态名 → Mixamo 文件名
        private static readonly Dictionary<string, string> Clips = new Dictionary<string, string>
        {
            { "Idle", "idle" },
            { "Walk", "walk" },
            { "Run", "run" },
            { "Atk1", "atk_windup" },
            { "Atk2", "atk_slash" },
            { "Atk3", "atk_overhead" },
            { "AtkSpin", "atk_spin" },
            { "CastPush", "cast_push" },
            { "CastUp", "cast_up" },
            { "CastBuff", "cast_buff" },
            { "Dodge", "dodge" },
            { "HitReaction", "hit" },
            { "Dead", "dead" },
            { "Victory", "victory" },
        };

        [MenuItem("浮生渡/13. 建角色 Animator Controller", priority = 130)]
        public static void Run()
        {
            Directory.CreateDirectory(ControllerDir);

            // 载入 19 个 clip（每个 FBX 里 1 个 clip，takeName 都叫 mixamo.com，按来源文件区分）
            var cache = new Dictionary<string, AnimationClip>();
            foreach (var kv in Clips)
            {
                string path = Path.Combine(MixamoDir, kv.Value + ".fbx");
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var a in all)
                    if (a is AnimationClip c && !c.name.StartsWith("Take"))
                    {
                        cache[kv.Key] = c;
                        break;
                    }
                if (cache.ContainsKey(kv.Key) == false)
                    Debug.LogWarning("[AnimatorSetup] 找不到 clip：" + path);
            }
            Debug.Log(string.Format("[AnimatorSetup] 载入 clip {0}/{1}", cache.Count, Clips.Count));

            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { RigDir }))
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!prefabPath.EndsWith(".prefab")) continue;
                string name = Path.GetFileNameWithoutExtension(prefabPath);
                BuildController(name, cache);
            }
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        private static void BuildController(string name, Dictionary<string, AnimationClip> cache)
        {
            string path = Path.Combine(ControllerDir, name + ".controller");
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (controller == null)
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // 幂等：清掉旧状态再建
            foreach (var s in sm.states) sm.RemoveState(s.state);

            var idle = New(sm, "Idle", cache);   // 默认状态
            var walk = New(sm, "Walk", cache);
            var run = New(sm, "Run", cache);
            var atk1 = New(sm, "Atk1", cache);
            var atk2 = New(sm, "Atk2", cache);
            var atk3 = New(sm, "Atk3", cache);
            var atkSpin = New(sm, "AtkSpin", cache);
            var cast = New(sm, "CastPush", cache);
            var hit = New(sm, "HitReaction", cache);
            var dead = New(sm, "Dead", cache);

            sm.defaultState = idle;

            // —— 移动混合
            var iw = T(idle, walk, 0.12f); iw.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");
            var wr = T(walk, run, 0.15f); wr.AddCondition(AnimatorConditionMode.Greater, 1.2f, "Speed");
            var wi = T(walk, idle, 0.2f); wi.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
            var rw = T(run, walk, 0.2f); rw.AddCondition(AnimatorConditionMode.Less, 1.2f, "Speed");

            // —— 一次性动作（Any State 触发，播完回 Idle）
            OneShot(controller, "Attack", atk1, 1.0f);
            OneShot(controller, "Attack", atk2, 1.0f);
            OneShot(controller, "Attack", atk3, 1.0f);
            OneShot(controller, "Attack", atkSpin, 1.0f);
            OneShot(controller, "Cast", cast, 1.0f);
            OneShot(controller, "Hit", hit, 1.0f);
            OneShot(controller, "Dead", dead, 1.0f);

            // —— 挂到 prefab 的 Animator 上
            string prefabPath = Path.Combine(RigDir, name + ".prefab");
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null) { Debug.LogWarning("[AnimatorSetup] 无 prefab：" + prefabPath); return; }
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            if (animator.avatar == null)
                animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Path.Combine(RigDir, name + ".avatar"));
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(go);

            Debug.Log("[AnimatorSetup] " + name + " → " + path + "（14 状态，Speed/Attack/Cast/Hit/Dead 驱动）");
        }

        private static AnimatorState New(AnimatorStateMachine sm, string key, Dictionary<string, AnimationClip> cache)
        {
            var st = sm.AddState(key);
            if (cache.TryGetValue(key, out var c)) st.motion = c;
            return st;
        }

        private static AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur)
        {
            var tr = from.AddTransition(to);
            tr.hasExitTime = false;
            tr.duration = dur;
            return tr;
        }

        /// <summary>Any State → 目标状态（Trigger 条件），目标状态退出时间 1.0 自动回 Idle。</summary>
        private static void OneShot(UnityEditor.Animations.AnimatorController controller, string trigger,
            AnimatorState target, float exitTime)
        {
            var sm = controller.layers[0].stateMachine;
            var tr = sm.AddAnyStateTransition(target);
            tr.hasExitTime = false;
            tr.duration = 0.08f;
            tr.canTransitionToSelf = false;
            tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            // 目标播完回 Idle
            var back = target.AddTransition(sm.defaultState);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.12f;
            back.hasFixedDuration = false;
        }
    }
}
