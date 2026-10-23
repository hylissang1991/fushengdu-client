// 《浮生渡》表现层 · 静态建模的程序化动作
//
// 【为什么需要它】
// 用户给的建模是**无骨骼、无动画**的单网格（AI 生成 3D 的通病）。CharacterMotion 靠摆动
// LegL/LegR/ArmL/ArmR 这些子节点工作，对单网格完全无效 —— 角色会变成"贴地滑行的雕像"。
// 这里改用**整体变换**做动作，观感上仍然有"走 / 待机 / 受击 / 倒地"：
//   - 走：上下颠簸 + 左右摇摆 + 向前进方向前倾（速度越大越明显）
//   - 待机：极缓的呼吸起伏 + 轻微侧摆
//   - 受击：短促抖动（Hit）
//   - 倒地：绕水平轴翻转（Die）
// 只改 localPosition / localRotation，不碰任何玩法数值（ADR-001 N-2）。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public sealed class StaticCharacterMotion : MonoBehaviour
    {
        private Vector3 _lastPos;
        private float _phase;
        private float _amp;
        private float _idleTime;
        private float _hit;           // 受击抖动剩余时间
        private bool _dead;
        private float _deathT;
        private float _punch;         // 前冲（攻击表现）

        private void Start()
        {
            _lastPos = transform.position;
            _idleTime = Random.Range(0f, 10f);
        }

        /// <summary>攻击表现：向正前方短促一顶。</summary>
        public void Punch(float amount = 1f)
        {
            if (_dead) return;
            _punch = Mathf.Max(_punch, amount);
        }

        /// <summary>受击表现：短促抖动。</summary>
        public void Hit(float amount = 1f)
        {
            if (_dead) return;
            _hit = Mathf.Max(_hit, 0.22f * amount);
        }

        public void Die()
        {
            if (_dead) return;
            _dead = true;
            _deathT = 0f;
        }

        private void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            if (_dead)
            {
                _deathT = Mathf.Min(_deathT + dt * 2.6f, 1f);
                float t = 1f - Mathf.Pow(1f - _deathT, 3f);
                transform.localRotation = Quaternion.Euler(0f, transform.localRotation.eulerAngles.y, t * 88f);
                transform.localPosition = new Vector3(0f, -0.12f * t, 0f);
                return;
            }

            Vector3 delta = transform.position - _lastPos;
            delta.y = 0f;
            _lastPos = transform.position;

            float speed = delta.magnitude / dt;
            float target = Mathf.Clamp01(speed / 4.5f);
            _amp = Mathf.Lerp(_amp, target, 8f * dt);
            _phase += dt * Mathf.Lerp(2.0f, 9.2f, _amp);
            _idleTime += dt;
            _hit = Mathf.Max(0f, _hit - dt);
            _punch = Mathf.Max(0f, _punch - dt * 4.5f);

            // ---- 走：上下颠簸 + 左右摇摆 + 前倾
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.055f * _amp;
            float roll = Mathf.Sin(_phase) * 3.2f * _amp;          // 左右摇摆（度）
            float pitch = 3.6f * _amp;                              // 向前进方向前倾（度）

            // ---- 待机：呼吸
            float breathe = Mathf.Sin(_idleTime * 1.6f) * 0.010f * (1f - _amp);

            // ---- 受击：高频抖动
            float shake = 0f;
            if (_hit > 0f) shake = Mathf.Sin(_hit * 90f) * 0.05f * (_hit / 0.22f);

            float y = bob + breathe;
            float z = _punch * 0.16f;                                // 前冲：沿自身 +Z 顶出去

            transform.localPosition = new Vector3(shake, y, z);
            transform.localRotation = Quaternion.Euler(
                pitch + shake * 40f,
                transform.localRotation.eulerAngles.y,
                roll);
        }
    }
}
