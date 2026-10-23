// 《浮生渡》表现层 · 带骨骼角色 prefab 的元数据 + 动画驱动
//
// 【为什么要有 metrics】
//   AI 生成的模型站直高度是 1.014~1.031 米，而游戏里相机锚点 / CharacterController(height=1.8)
//   / 脚下选中环全是按 1.8 调的。导入时把"未缩放的绑定身高"记下来，运行时就能按
//   CharacterModels.TargetStandHeight 归一到目标身高 —— 系数只在一个地方定义。
//
// 【为什么要有 motion】
//   旧的 StaticCharacterMotion 是给**无骨骼静态网格**做"整体颠簸"的假动作，
//   挂在真骨骼角色上会和 Mecanim 动画叠加（永久 3.6° 前倾 + 左右摇摆）。
//   换成这个类：只把实际水平速度写进 Controller 已有的 Speed 参数，
//   由 Idle / Walk / Run 状态机自己决定播哪个动画。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>由 GlbRigImporter 在导入时写入，记录该模型的原始绑定身高。</summary>
    public sealed class CharacterModelMetrics : MonoBehaviour
    {
        public float BindHeight = 1f;
    }

    /// <summary>
    /// 用角色实际位移驱动 Animator 的 Speed 参数（静止 → Idle，走 → Walk，跑 → Run）。
    /// 挂在"模型容器"上，读的是容器的世界坐标 —— 这样本地玩家、
    /// 网络远端玩家、怪物都能用同一套逻辑，不需要各自接线。
    /// </summary>
    public sealed class RiggedCharacterMotion : MonoBehaviour
    {
        public Animator Anim;

        /// <summary>Speed 参数的平滑时间常数（秒）。避免速度抖动导致状态机来回切。</summary>
        public float Smoothing = 0.09f;

        private Vector3 _last;
        private float _speed;
        private bool _init;

        private void OnEnable()
        {
            _last = transform.position;
            _init = true;
        }

        private void LateUpdate()
        {
            if (Anim == null) return;
            if (!_init) { _last = transform.position; _init = true; }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 delta = transform.position - _last;
            _last = transform.position;
            delta.y = 0f;

            float raw = delta.magnitude / dt;
            // 指数平滑：帧率无关
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(Smoothing, 0.0001f));
            _speed = Mathf.Lerp(_speed, raw, a);

            Anim.SetFloat("Speed", _speed);
        }

        /// <summary>由 PlayerActor / MonsterActor 在需要时调用（攻击/受击/死亡表现）。</summary>
        public void Punch(float amount = 1f) { if (Anim != null) Anim.SetTrigger("Attack"); }

        public void Hit(float amount = 1f) { if (Anim != null) Anim.SetTrigger("Hit"); }

        public void Die() { if (Anim != null) Anim.SetTrigger("Dead"); }
    }
}
