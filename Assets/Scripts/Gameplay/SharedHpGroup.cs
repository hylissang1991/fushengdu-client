// 《浮生渡》表现层 · 双体 / 合体真实共享血量分组
// 替代原「任一阵亡 → 其余 Enrage ×1.3」近似：组内所有成员共用一个血量池，
// 任一成员受击都扣减统一池，所有成员按同一比例显示血量；池空则全灭。
// 通过 ActorState.SharedHpDamageSink 把伤害从「单体扣血」重定向到共享池，Core 层仅持委托引用，不依赖 UnityEngine。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>双体 / 合体真实共享血量分组。</summary>
    public sealed class SharedHpGroup
    {
        /// <summary>组内成员（双体 = 2，合体 = 3）。</summary>
        public readonly List<MonsterActor> Members = new List<MonsterActor>();

        /// <summary>统一血量池当前值与上限（= 各成员 HpMax 之和）。</summary>
        public float TotalHp;
        public float TotalHpMax;

        /// <summary>是否处于真实共享状态（成员 ≥ 2 才激活；单个成员或缺失同伴时退化为普通单体）。</summary>
        public bool Active;

        /// <summary>用一组同场生成的 BOSS 初始化分组，并接管它们的伤害入口。</summary>
        public void Init(List<MonsterActor> members)
        {
            Members.Clear();
            float max = 0f, hp = 0f;
            foreach (var m in members)
            {
                if (m == null || m.State == null) continue;
                Members.Add(m);
                max += m.State.HpMax;
                hp += m.State.Hp;
                // 接管伤害入口：后续对该成员的所有伤害先经过共享池
                m.State.SharedHpDamageSink = Sink;
            }
            TotalHpMax = max;
            TotalHp = hp;
            Active = Members.Count >= 2;
            Sync();
        }

        /// <summary>伤害入口（绑定到各成员 ActorState.SharedHpDamageSink）。返回 true = 已处理（扣减共享池并同步）。</summary>
        private bool Sink(float amount)
        {
            if (!Active) return false;
            TotalHp -= amount;
            if (TotalHp <= 0f)
            {
                TotalHp = 0f;
                KillAll();
            }
            Sync();
            return true;
        }

        /// <summary>按共享池比例重写每个存活成员显示血量（保证「真实共享」视觉与逻辑一致）。</summary>
        public void Sync()
        {
            if (TotalHpMax <= 0f) return;
            float ratio = Mathf.Clamp01(TotalHp / TotalHpMax);
            foreach (var m in Members)
            {
                if (m == null || m.State == null || m.State.Dead) continue;
                m.State.Hp = m.State.HpMax * ratio;
            }
        }

        private void KillAll()
        {
            foreach (var m in Members)
            {
                if (m == null || m.State == null) continue;
                m.State.Hp = 0f;
                m.State.Dead = true;
            }
        }
    }
}
