// 《浮生渡》表现层 · 程序化动作（零动画资源）
// 目的：目前没有骨骼动画资产，如果角色只是"贴地滑动"，观感立刻掉到原型水平。
// 这里用极轻量的程序化动作把「走 / 待机」补上：
//   - 位移速度驱动四肢摆动（相位按速度累积，停下自然收口）
//   - 待机呼吸（躯干微幅上下 + 侧向摆动）
//   - 精英/首领体量更大，摆幅更重
// 只改子节点的 localRotation / localPosition，不影响任何玩法数值（ADR-001 N-2）。
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public sealed class CharacterMotion : MonoBehaviour
    {
        private Transform _legL, _legR, _armL, _armR, _body, _root;
        private Quaternion _legLBase, _legRBase, _armLBase, _armRBase, _bodyBase;
        private Vector3 _bodyBasePos;
        private Vector3 _lastPosition;
        private float _phase;
        private float _amplitude;
        private float _idleTime;
        private float _scale = 1f;

        private void Start()
        {
            _root = transform;
            _legL = First(_root, "LegL", "LegFL");
            _legR = First(_root, "LegR", "LegFR");
            _armL = First(_root, "ArmL", "ArmFL");
            _armR = First(_root, "ArmR", "ArmFR");
            _body = First(_root, "Torso", "Body");

            if (_legL != null) _legLBase = _legL.localRotation;
            if (_legR != null) _legRBase = _legR.localRotation;
            if (_armL != null) _armLBase = _armL.localRotation;
            if (_armR != null) _armRBase = _armR.localRotation;
            if (_body != null)
            {
                _bodyBase = _body.localRotation;
                _bodyBasePos = _body.localPosition;
            }

            _lastPosition = _root.position;
            _idleTime = Random.Range(0f, 10f);
            _scale = Mathf.Approximately(_root.localScale.y, 0f) ? 1f : _root.localScale.y;
        }

        private void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 delta = _root.position - _lastPosition;
            delta.y = 0f;
            _lastPosition = _root.position;

            float speed = delta.magnitude / dt;
            float target = Mathf.Clamp01(speed / 4.5f);
            _amplitude = Mathf.Lerp(_amplitude, target, 8f * dt);
            _phase += dt * Mathf.Lerp(2.2f, 9.5f, _amplitude);
            _idleTime += dt;

            float swing = Mathf.Sin(_phase) * 34f * _amplitude;
            float armSwing = -swing * 0.62f;

            if (_legL != null) _legL.localRotation = _legLBase * Quaternion.Euler(swing, 0f, 0f);
            if (_legR != null) _legR.localRotation = _legRBase * Quaternion.Euler(-swing, 0f, 0f);
            if (_armL != null) _armL.localRotation = _armLBase * Quaternion.Euler(armSwing, 0f, 0f);
            if (_armR != null) _armR.localRotation = _armRBase * Quaternion.Euler(-armSwing, 0f, 0f);

            if (_body != null)
            {
                float breathe = Mathf.Sin(_idleTime * 1.7f) * 0.012f;
                float lean = Mathf.Sin(_phase * 2f) * 2.0f * _amplitude;
                _body.localPosition = _bodyBasePos + new Vector3(0f, breathe, 0f);
                _body.localRotation = _bodyBase * Quaternion.Euler(lean, 0f, 0f);
            }

            // 整体上下颠簸（幅度按体量放大）
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.06f * _amplitude * _scale;
            Vector3 local = transform.localPosition;
            local.y = bob;
            transform.localPosition = local;
        }

        private static Transform First(Transform root, string nameA, string nameB = null)
        {
            Transform a = FindDeep(root, nameA);
            if (a != null) return a;
            return nameB != null ? FindDeep(root, nameB) : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
