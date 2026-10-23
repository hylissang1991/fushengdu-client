// 《浮生渡》表现层 · 面向相机（用于任务感叹号、世界标记）
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public sealed class Billboard : MonoBehaviour
    {
        public bool LockYawOnly = true;

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = cam.transform.position - transform.position;
            if (LockYawOnly) dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
        }
    }
}
