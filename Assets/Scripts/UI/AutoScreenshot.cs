// 《浮生渡》UI 层 · 自动截图（无人值守验证用）
// 目的：CI / 冒烟验证需要"看到真实画面"，而不是只看日志说它没崩。
// 参数：
//   -fdShot <path>      延迟若干秒后写入一张 PNG（默认延迟 6 秒）
//   -fdShotDelay <秒>   自定义延迟
//   -fdQuit             截图完成后自动退出（批处理场景用）
//   -fdModelShot        角色证件照模式：藏掉 HUD、把主相机摆到玩家正面 3m 平视后截图
//                       （用来肉眼确认玩家模型是直立人形而不是别的形态）
// 不带任何参数时完全不执行，不影响正常游玩。
using System.Collections;
using Fushengdu.Gameplay;
using UnityEngine;

namespace Fushengdu.UI
{
    public sealed class AutoScreenshot : MonoBehaviour
    {
        private string _path;
        private float _delay = 6f;
        private bool _quit;
        private bool _modelShot;

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
                    case "-fdModelShot":
                        _modelShot = true;
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

            if (_modelShot)
            {
                yield return ModelShot();
                if (_quit) Application.Quit();
                yield break;
            }

            ScreenCapture.CaptureScreenshot(_path, 1);
            Debug.Log("[浮生渡] 已写入截图：" + _path);
            yield return new WaitForSeconds(1.2f);

            if (_quit)
            {
                Application.Quit();
            }
        }

        /// <summary>
        /// 角色证件照：藏 HUD → 主相机移到玩家正前方平视 → 截图 → 复原。
        /// 之所以要单独做，是因为第三人称镜头俯视 + 底部技能栏会把玩家压在画面边缘，看不清形态。
        /// </summary>
        private IEnumerator ModelShot()
        {
            PlayerActor p = null;
            float waited = 0f;
            while (p == null && waited < 15f)
            {
                p = LocalPlayerLocator.Current;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (p == null)
            {
                Debug.LogError("[浮生渡] 证件照失败：15 秒内玩家未出生（请带 -fdSolo）");
                yield break;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[浮生渡] 证件照失败：找不到主相机");
                yield break;
            }

            Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
            for (int i = 0; i < canvases.Length; i++) canvases[i].enabled = false;

            var smr = p.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
                Debug.Log(string.Format("[浮生渡] 证件照目标：class={0} bounds={1}",
                    p.Class, smr.bounds.size.ToString("F3")));
            else
                Debug.LogWarning("[浮生渡] 证件照：玩家无 SkinnedMeshRenderer，按程序化占位人取景");

            // 诊断：把玩家身上所有渲染器的状态打出来（排查"模型在但拍不到"）
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                Debug.Log(string.Format(
                    "[证件照] Renderer {0} type={1} enabled={2} activeInHierarchy={3} layer={4} bounds={5} shader={6}",
                    r.name, r.GetType().Name, r.enabled, r.gameObject.activeInHierarchy, r.gameObject.layer,
                    r.bounds.size.ToString("F3"),
                    r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "null"));
            }

            // 取证专用：自定义 Fushengdu/Stylized 在本渲染路径下会报错成品红/不可见，
            // 这里把玩家渲染器临时换成 URP Lit 浅色材质（运行时实例，不改工程资源），
            // 只为看清几何形态；还原时把原材质引用写回去。
            var backupMats = new System.Collections.Generic.List<Material[]>();
            var targets = new System.Collections.Generic.List<Renderer>();
            Material lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (lit == null) lit = new Material(Shader.Find("Standard"));
            if (lit != null)
            {
                lit.color = new Color(0.86f, 0.72f, 0.55f);
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "SelectionRing") continue;   // 选中环保持原样
                    backupMats.Add(r.sharedMaterials);
                    targets.Add(r);
                    var one = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < one.Length; i++) one[i] = lit;
                    r.sharedMaterials = one;
                }
                Debug.Log("[证件照] 取证材质已替换：渲染器 " + targets.Count + " 个 → URP Lit");
            }
            else Debug.LogError("[证件照] 找不到 URP Lit / Standard shader，材质无法替换");

            // 决定性实验：在玩家位置放一个亮红立方体。
            // 立方体可见而玩家不可见 → 玩家蒙皮网格渲染有问题；两者都不可见 → 相机/坐标问题。
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.name = "EvidenceProbe";
            probe.transform.position = p.transform.position + Vector3.up * 1.0f + p.transform.right * 1.8f;
            probe.transform.localScale = Vector3.one;
            var probeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (probeMat == null) probeMat = new Material(Shader.Find("Standard"));
            if (probeMat != null) probeMat.color = new Color(1f, 0.15f, 0.15f);
            probe.GetComponent<Renderer>().sharedMaterial = probeMat;

            Vector3 playerCenter = smr != null ? smr.bounds.center : p.transform.position + Vector3.up * 0.9f;
            Debug.Log("[证件照] 探针 世界=" + probe.transform.position.ToString("F2")
                + " 屏幕=" + cam.WorldToScreenPoint(probe.transform.position).ToString("F0")
                + " | 玩家中心 世界=" + playerCenter.ToString("F2")
                + " 屏幕=" + cam.WorldToScreenPoint(playerCenter).ToString("F0")
                + " | 屏幕尺寸=" + Screen.width + "x" + Screen.height);

            if (smr != null)
                Debug.Log(string.Format(
                    "[证件照] SMR forceRenderingOff={0} updateWhenOffscreen={1} localBounds={2} bones={3} rootBone={4} quality={5}",
                    smr.forceRenderingOff, smr.updateWhenOffscreen, smr.localBounds.size.ToString("F3"),
                    smr.bones != null ? smr.bones.Length : -1,
                    smr.rootBone != null ? smr.rootBone.name : "null", smr.quality));

            // 关键诊断：把当前蒙皮结果烘出来，看顶点是否塌缩（塌缩=骨骼/绑定姿势异常）
            var anim2 = p.GetComponentInChildren<Animator>();
            Debug.Log("[证件照] Animator=" + (anim2 != null ? ("cullingMode=" + anim2.cullingMode
                + " 有Controller=" + (anim2.runtimeAnimatorController != null)
                + " avatarHuman=" + (anim2.avatar != null && anim2.avatar.isHuman)) : "无"));
            if (smr != null && smr.sharedMesh != null)
            {
                int nullBones = 0;
                if (smr.bones != null) foreach (var b in smr.bones) if (b == null) nullBones++;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                Debug.Log("[证件照] 蒙皮结果 vertexCount=" + smr.sharedMesh.vertexCount
                    + " nullBones=" + nullBones
                    + " bakeBounds=" + baked.bounds.size.ToString("F3")
                    + " bakeCenter=" + baked.bounds.center.ToString("F3"));
                Object.Destroy(baked);
            }
            else Debug.LogError("[证件照] SMR 或 sharedMesh 为空 —— 模型资源没进包");

            // 不挪相机：主相机是第三人称跟随相机，每帧会被跟随脚本拉回原位；
            // 手动相机在 URP 下渲染蒙皮网格也不可靠。改为「隐藏 HUD + 收窄 FOV」，
            // 用游戏自己的视角截图，玩家必然在画面内。
            float oldFov = cam.fieldOfView;
            cam.fieldOfView = 30f;

            yield return null;
            yield return null;
            yield return null;

            ScreenCapture.CaptureScreenshot(_path, 1);
            Debug.Log("[浮生渡] 已写入证件照（隐藏 HUD 的第三人称视角，fov=" + cam.fieldOfView + "）：" + _path);
            yield return new WaitForSeconds(1.5f);

            cam.fieldOfView = oldFov;
            Object.Destroy(probe);
            for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = true;

            // 还原取证材质
            for (int i = 0; i < targets.Count && i < backupMats.Count; i++)
                if (targets[i] != null) targets[i].sharedMaterials = backupMats[i];
        }
    }
}
