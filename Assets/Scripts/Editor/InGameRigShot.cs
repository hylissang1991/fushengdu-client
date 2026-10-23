// 《浮生渡》编辑器工具 · 玩家角色「真实场景内」Play 模式三视角取证
//
// 【为什么不是再用 GlbImportShot】那个脚本验的是"空场里的 rig prefab"。
// 躺倒/侧身缺陷发生在真实游戏里（Player.prefab → RuntimeArt → CharacterModels 全链路），
// 所以取证必须在真场景（NewcomerZone）里走 StartSolo 的真实出生路径。
//
// 【产出】
//   idle_1_front / idle_2_right / idle_3_back   待机三视角（相机按 transform.forward 摆）
//   walk_1_front / walk_2_side                  沿固定世界方向真实移动时的正面/侧面
//   每张图都配一条日志：transform 朝向 vs 视觉朝向（左右手骨叉积）的偏差角 ——
//   偏差角 ≈ 0° 才算「模型正面 = GameObject.forward」达标。
//
// 用法：
//   Unity.exe -batchmode -projectPath <proj>        ←不要加 -quit，否则进不了 Play 模式
//             -executeMethod Fushengdu.Editor.InGameRigShot.Run
//             -fdShotDir D:/AI_Workspace/浮生渡/shots/p5_002 -logFile <log>
using System.IO;
using UnityEditor;
using UnityEngine;
using Fushengdu.Gameplay;

namespace Fushengdu.Editor
{
    public static class InGameRigShot
    {
        private static string _dir = "D:/AI_Workspace/浮生渡/shots/p5_002";

        private enum Phase { WaitPlayer, IdleViews, TurnStart, WalkViews, Done }
        private static Phase _phase;
        private static int _wait;
        private static PlayerActor _player;
        private static Animator _anim;
        private static Camera _cam;
        private static Transform _hip, _head;
        private static int _view;
        private static readonly Vector3 WalkDir = new Vector3(1f, 0f, 0f); // 固定世界方向

        [MenuItem("浮生渡/22. 游戏内角色三视角取证", priority = 220)]
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-fdShotDir") _dir = args[i + 1];
            Directory.CreateDirectory(_dir);

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            // 打开真实游戏场景 —— 取证的是玩家实际会看到的画面
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/NewcomerZone.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            _phase = Phase.WaitPlayer;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
            Debug.Log("[InGameShot] 已打开 NewcomerZone，进入 Play 模式等玩家出生…");
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (++_wait > 7200)
            {
                Debug.LogError("[InGameShot] 超时（phase=" + _phase + "），退出");
                Finish(2);
                return;
            }

            switch (_phase)
            {
                case Phase.WaitPlayer:
                    _player = LocalPlayerLocator.Current;
                    if (_player == null && Time.frameCount > 90 && !_Kicked)
                    {
                        _Kicked = true;
                        var hud = Object.FindObjectOfType<Fushengdu.UI.GameHud>();
                        if (hud != null)
                        {
                            hud.ApplyDefaultSettings();
                            hud.StartSolo();
                            Debug.Log("[InGameShot] 手动触发 StartSolo（命令行未带 -fdSolo）");
                        }
                        else Debug.LogError("[InGameShot] 找不到 GameHud，无法启动会话");
                    }
                    if (_player != null && Time.frameCount > 30) EnterIdle();
                    break;

                case Phase.IdleViews:
                    if (Time.frameCount % 5 != 0) return;   // 等几帧让 Animator 稳定
                    LogAlign("idle");
                    CaptureView();
                    _view++;
                    if (_view >= 3) { _phase = Phase.TurnStart; _wait = 0; }
                    break;

                case Phase.TurnStart:
                    // 走真实移动代码路径（PlayerActor.Move → CharacterController + Slerp 转向），
                    // RiggedCharacterMotion 会把实际位移喂给 Animator.Speed
                    _player.Move(WalkDir, Time.deltaTime);
                    if (_wait > 90) { _phase = Phase.WalkViews; _view = 0; }
                    break;

                case Phase.WalkViews:
                    _player.Move(WalkDir, Time.deltaTime);
                    if (Time.frameCount % 5 != 0) return;
                    LogAlign("walk");
                    CaptureWalkView();
                    _view++;
                    if (_view >= 2)
                    {
                        Report();
                        Finish(0);
                    }
                    break;
            }
        }

        private static bool _Kicked;

        private static void EnterIdle()
        {
            _wait = 0;
            _view = 0;
            _phase = Phase.IdleViews;

            var camGo = new GameObject("EvidenceCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.fieldOfView = 45f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 200f;
            _cam.enabled = false;   // 只用手动 Render() 出图，不参与游戏渲染

            _anim = _player.GetComponentInChildren<Animator>();
            var smr = _player.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log(string.Format("[InGameShot] 玩家已出生：name={0} class={1} female={2} pos={3} rot={4}",
                _player.State != null ? _player.State.Name : "?",
                _player.Class, _player.Female, _player.transform.position.ToString("F2"),
                _player.transform.eulerAngles.ToString("F1")));
            if (smr != null)
            {
                Debug.Log(string.Format("[InGameShot] SMR 世界 bounds size={0}（Y 应≈1.8）center={1}",
                    smr.bounds.size.ToString("F3"), smr.bounds.center.ToString("F3")));
                foreach (var b in smr.bones)
                {
                    if (b == null) continue;
                    if (_hip == null && (b.name == "Hip" || b.name == "Pelvis")) _hip = b;
                    if (b.name == "Head") _head = b;
                }
            }
            else Debug.LogError("[InGameShot] 玩家身上没有 SkinnedMeshRenderer —— 模型链路没接上！");
        }

        /// <summary>视觉朝向：左手骨 - 右手骨 = 人的左方向；fwd = up × left。</summary>
        private static Vector3 VisualFacing()
        {
            if (_anim == null) return Vector3.zero;
            var lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (lh == null || rh == null) return Vector3.zero;
            Vector3 left = lh.position - rh.position;
            left.y = 0f;
            if (left.sqrMagnitude < 1e-6f) return Vector3.zero;
            return Vector3.Cross(Vector3.up, left.normalized).normalized;
        }

        private static void LogAlign(string tag)
        {
            Vector3 vis = VisualFacing();
            Vector3 fwd = _player.transform.forward;
            float off = Vector3.SignedAngle(fwd, vis, Vector3.up);
            Debug.Log(string.Format(
                "[InGameShot] [{0}] 对齐偏差={1:F1}°（0=模型正面=transform.forward）| transform.fwd={2} 视觉fwd={3}",
                tag, off, fwd.ToString("F2"), vis.sqrMagnitude > 0f ? vis.ToString("F2") : "n/a"));
            if (_hip != null && _head != null)
                Debug.Log(string.Format("[InGameShot] [{0}] 脊柱倾角={1:F1}°（>55° 视为躺倒）",
                    tag, Vector3.Angle(_head.position - _hip.position, Vector3.up)));
        }

        private static void CaptureView()
        {
            // 0=正面 1=右侧面 2=背面（相对玩家 transform）
            float yaw = _view * 90f;
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * _player.transform.forward;
            Shoot("idle_" + (_view + 1) + new[] { "_front", "_right", "_back" }[_view],
                _player.transform.position + dir * 2.8f + Vector3.up * 1.15f);
        }

        private static void CaptureWalkView()
        {
            // 0=行进正面（相机在移动方向前方，角色应面朝镜头走来）1=行进侧面
            Vector3 side = Quaternion.AngleAxis(90f, Vector3.up) * WalkDir;
            Vector3 dir = _view == 0 ? WalkDir : side;
            Shoot("walk_" + (_view + 1) + (_view == 0 ? "_front" : "_side"),
                _player.transform.position + dir * 3.2f + Vector3.up * 1.15f);
        }

        private static void Shoot(string name, Vector3 camPos)
        {
            _cam.transform.position = camPos;
            _cam.transform.LookAt(_player.transform.position + Vector3.up * 0.95f, Vector3.up);

            const int W = 720, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log("[InGameShot] 出图 " + name);
        }

        private static void Report()
        {
            var smr = _player.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log(string.Format(
                "[InGameShot] 结论：身高={0:F3} m | Animator={1} | applyRootMotion={2}",
                smr != null ? smr.bounds.size.y : -1f,
                _anim != null && _anim.runtimeAnimatorController != null
                    ? _anim.runtimeAnimatorController.name : "无",
                _anim != null && _anim.applyRootMotion));
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}
