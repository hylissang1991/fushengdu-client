// 《浮生渡》表现层 · 副本 BOSS 刷怪机（Slice 1）
// 不引用 FishNet：离线单人走 new GameObject + AddComponent<MonsterActor>() + Initialize + Register。
// 联机主机的 BossSpawn 由 FishNetSession 负责（本期不在此实现，预留接口）。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>
    /// 按 DungeonDef.BossIds 逐个生成 MonsterActor。
    /// 通用引擎：先保证离线单人能生成并击杀 BOSS（Slice 1 验收路径）。
    /// </summary>
    public sealed class DungeonSpawner
    {
        private readonly CombatWorld _world;

        public DungeonSpawner(CombatWorld world)
        {
            _world = world;
        }

        /// <summary>在 anchor 附近按行生成 BOSS，返回生成的实例列表。</summary>
        public List<MonsterActor> SpawnBosses(int[] bossIds, Vector3 anchor)
        {
            var list = new List<MonsterActor>();
            if (bossIds == null) return list;
            for (int i = 0; i < bossIds.Length; i++)
            {
                Core.MonsterDef def = Core.MonsterTable.Default.Get(bossIds[i]);
                if (def == null) continue;
                Vector3 pos = anchor + new Vector3((i % 3) * 5f - 5f, 0f, -i * 10f);
                MonsterActor actor = CreateMonster(def, pos);
                if (actor != null)
                {
                    // Slice 2a (M3)：挂 BOSS 阶段机（按 bossId 查表；其余 BOSS 走 1 阶段默认档）
                    var ctrl = actor.gameObject.AddComponent<BossPhaseController>();
                    Core.BossPhaseProfile profile = Core.BossPhaseTable.Default.Get(def.Id);
                    ctrl.Init(profile, this, _world, null);
                    list.Add(actor);
                }
            }
            // 双体 / 合体：按 LinkedBossIds 归组并绑定真实共享血量（替代原 Enrage ×1.3 近似）
            BindSharedHpGroups(list);
            return list;
        }

        /// <summary>
        /// 双体（611+612）/ 合体（613+614+615）：把同场生成且 LinkedBossIds 互指的成员归为一组，
        /// 创建 SharedHpGroup 并绑定到每个成员的 BossPhaseController，实现真实共享血量。
        /// </summary>
        private void BindSharedHpGroups(List<MonsterActor> bosses)
        {
            if (bosses == null || bosses.Count < 2) return;

            var byId = new Dictionary<int, MonsterActor>();
            foreach (var b in bosses) if (b != null) byId[b.MonsterDefId] = b;

            var processed = new HashSet<int>();
            foreach (var b in bosses)
            {
                if (b == null) continue;
                Core.BossPhaseProfile prof = Core.BossPhaseTable.Default.Get(b.MonsterDefId);
                if (prof == null || prof.Phases.Count == 0) continue;
                int[] linked = prof.Phases[0].LinkedBossIds;
                if (linked == null || linked.Length <= 1) continue;

                int self = b.MonsterDefId;
                if (processed.Contains(self)) continue;
                // 每个分组只从最小 id 的那一侧处理一次，避免重复建组
                int minId = self;
                foreach (int id in linked) if (id < minId) minId = id;
                if (self != minId) continue;
                processed.Add(self);
                foreach (int id in linked) processed.Add(id);

                var members = new List<MonsterActor>();
                foreach (int id in linked)
                {
                    MonsterActor m;
                    if (byId.TryGetValue(id, out m) && m != null) members.Add(m);
                }
                if (members.Count < 2) continue;

                var group = new SharedHpGroup();
                group.Init(members);
                foreach (var m in members)
                {
                    var ctrl = m.GetComponent<BossPhaseController>();
                    if (ctrl != null) ctrl.BindSharedHpGroup(group);
                }
                Debug.Log("[DungeonSpawner] 双体/合体共享血量分组已建立：成员=" + string.Join(",", linked));
            }
        }

        /// <summary>
        /// 以给定定义生成一只「复制体」怪物（707 镜像 / 710 增殖 / 712 祭坛守卫用）。
        /// statMul 缩放属性（血量、攻强、护甲、装备攻强）；overrideName 可重命名。
        /// 返回实例并已在 CombatWorld 注册；调用方负责在副本退出时清理。
        /// </summary>
        public MonsterActor SpawnCopy(Core.MonsterDef def, Vector3 pos, float statMul = 1f, string overrideName = null)
        {
            if (def == null) return null;
            MonsterActor a = CreateMonster(def, pos);
            if (a != null && statMul != 1f)
            {
                a.State.HpMax *= statMul; a.State.Hp = a.State.HpMax;
                a.State.WeaponDps *= statMul; a.State.E_AP *= statMul; a.State.Armor *= statMul;
            }
            if (a != null && overrideName != null) a.State.Name = overrideName;
            return a;
        }

        /// <summary>复制某个已存在怪物（沿用其定义，位置默认在其附近随机偏移）。</summary>
        public MonsterActor SpawnCopy(MonsterActor source, float statMul = 1f, Vector3? pos = null, string overrideName = null)
        {
            if (source == null || source.Definition == null) return null;
            Vector3 p = pos ?? (source.transform.position + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f)));
            return SpawnCopy(source.Definition, p, statMul, overrideName);
        }

        /// <summary>
        /// 在指定 BOSS 附近召唤小怪（阶段机 SpawnAdds 回调）。
        /// 返回生成的实例列表，交由 BossPhaseController 在销毁时清理。
        /// </summary>
        public List<MonsterActor> SpawnAddsNear(MonsterActor boss, int[] addDefIds, int[] counts)
        {
            var result = new List<MonsterActor>();
            if (boss == null || addDefIds == null) return result;
            Vector3 anchor = boss.transform.position;
            int placed = 0;
            for (int i = 0; i < addDefIds.Length; i++)
            {
                Core.MonsterDef def = Core.MonsterTable.Default.Get(addDefIds[i]);
                if (def == null) continue;
                int count = (counts != null && i < counts.Length) ? counts[i] : 1;
                for (int k = 0; k < count; k++)
                {
                    float ang = placed * 1.7f;
                    Vector3 off = new Vector3(Mathf.Cos(ang) * 4f, 0f, Mathf.Sin(ang) * 4f);
                    MonsterActor a = CreateMonster(def, anchor + off);
                    if (a != null) result.Add(a);
                    placed++;
                }
            }
            return result;
        }

        /// <summary>离线单人生成路径：直接 new GameObject，不依赖网络。</summary>
        private MonsterActor CreateMonster(Core.MonsterDef def, Vector3 pos)
        {
            GameObject go = new GameObject("DungeonBoss_" + def.Id);
            go.transform.position = pos;
            var actor = go.AddComponent<MonsterActor>();
            actor.Initialize(def, 0); // actorId 由 CombatWorld.Register 分配
            if (_world != null) _world.Register(actor);
            return actor;
        }

        /// <summary>清理所有已生成的 BOSS（退出副本时调用）。</summary>
        public void DespawnAll(List<MonsterActor> actors)
        {
            if (actors == null) return;
            for (int i = 0; i < actors.Count; i++)
            {
                if (actors[i] == null) continue;
                if (_world != null) _world.Unregister(actors[i]);
                if (actors[i].gameObject != null) Object.Destroy(actors[i].gameObject);
            }
            actors.Clear();
        }
    }
}
