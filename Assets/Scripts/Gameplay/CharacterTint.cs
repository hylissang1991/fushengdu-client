// 《浮生渡》表现层 · 角色染色组
// 目的：闪红 / 死亡隐显 / 无敌闪烁这类"整只模型"的表现，不能只改一个 Renderer
// （模型由多个部件拼成，取第一个 Renderer 只会让眼睛变色）。
// 用法：由 RuntimeArt 在拼装完模型后挂在模型根节点上，MonsterActor / PlayerActor 通过它统一操作。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public sealed class CharacterTint : MonoBehaviour
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Color> _baseColors = new List<Color>();
        private readonly List<Material> _materials = new List<Material>();

        /// <summary>收集子节点全部 Renderer 并记录原色（只记录一次）。</summary>
        public void Collect()
        {
            _renderers.Clear();
            _baseColors.Clear();
            _materials.Clear();

            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                // 每个部件用独立材质实例，避免一只怪闪红导致全场同材质变红
                Material mat = r.material;
                _renderers.Add(r);
                _materials.Add(mat);
                _baseColors.Add(mat.HasProperty("_Color") ? mat.color : Color.white);
            }
        }

        public void SetTint(Color tint, float amount)
        {
            for (int i = 0; i < _materials.Count; i++)
            {
                Material m = _materials[i];
                if (m == null) continue;
                m.color = Color.Lerp(_baseColors[i], tint, amount);
            }
        }

        public void ResetTint()
        {
            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] != null) _materials[i].color = _baseColors[i];
            }
        }

        public void SetVisible(bool visible)
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = visible;
            }
        }

        /// <summary>背包 / 界面预览用：复制一份不带逻辑的模型。</summary>
        public Renderer FirstRenderer => _renderers.Count > 0 ? _renderers[0] : null;
    }
}
