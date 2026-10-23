// 《浮生渡》表现层 · 玩家角色
// @trace D-28（三大陆 × 7 原型 = 21 命名职业；三大陆职业名不同，但技能 / 使用武器 / 装备穿戴完全一致）
// @trace D-28 实现铁律：技能数值、资源系统、平衡调整一律以 ClassArchetype（7 原型）为准，绝不以 ClassId（21 命名职业）为准
// @trace ADR-001 N-2：客户端不计算任何影响结果的数据，只发「意图」。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>玩家持久数据（进本地存档）。</summary>
    [System.Serializable]
    public sealed class CharacterData
    {
        public string Name = "无名渡者";
        public int ClassId;   // Core.ClassId
        public bool Female;
        public int Level = 1;
        public int Xp;
        public int Silver;
        public List<int> BagItemDefIds = new List<int>();
        public List<int> EquippedItemDefIds = new List<int>();
        public List<int> QuestProgressQuestIds = new List<int>();
        public List<int> QuestProgressCounts = new List<int>();
        public List<int> QuestDoneIds = new List<int>();
        public int TotalKills;
    }

    /// <summary>
    /// 玩家实体：持有成长、背包、任务日志，并把「输入意图」提交给权威端。
    /// </summary>
    public sealed class PlayerActor : ActorEntity
    {
        public Core.Inventory Inventory = new Core.Inventory();
        public Core.QuestLog Quests = new Core.QuestLog();
        public Core.ProgressionState Progression = new Core.ProgressionState();
        public int Silver;
        public bool Female;

        /// <summary>当前选中目标（客户端本地状态）。</summary>
        public ActorEntity Target;

        /// <summary>权威端桥接（离线 / 网络两种实现，见 ADR-001 N-8）。</summary>
        public IWorldAuthority Authority;

        /// <summary>技能栏（职业决定）。</summary>
        public Core.SkillId[] SkillBar = new Core.SkillId[0];

        /// <summary>本地冷却镜像（仅用于 UI 显示，真相在服务端）。</summary>
        public readonly Dictionary<int, float> LocalCooldowns = new Dictionary<int, float>();

        private CharacterController _controller;
        private float _verticalVelocity;

        public Core.ClassId Class => State != null ? State.Class : Core.ClassId.None;

        protected override void Awake()
        {
            base.Awake();
            _controller = GetComponent<CharacterController>();
            if (_controller == null) _controller = gameObject.AddComponent<CharacterController>();
            _controller.height = 1.8f;
            _controller.radius = 0.35f;
            _controller.center = new Vector3(0f, 0.9f, 0f);
        }

        /// <summary>
        /// 初始化角色。
        /// </summary>
        /// <param name="grantStartingWeapon">
        /// 是否发放出生武器。**读档时必须传 false**——存档背包里已含该武器，
        /// 再发一把就会每次读档多一把（缺陷 N-5）。
        /// </param>
        public void Setup(Core.ClassId classId, bool female, string name, int level, bool grantStartingWeapon = true)
        {
            if (State == null) State = new Core.ActorState();
            State.Class = classId;
            State.Faction = Core.ActorFaction.Player;
            State.Name = name;
            State.ResourceId = classId == Core.ClassId.SwordCultivator ? Core.ResourceType.Qi : Core.ResourceType.Rage;
            State.ResourceMax = 100f;
            State.Resource = 0f;
            Female = female;

            Inventory.Clear();
            if (grantStartingWeapon)
            {
                // 出生武器（1 级白装），保证 W/S 有值
                int weaponId = classId == Core.ClassId.SwordCultivator ? 1001 : 1002;
                Inventory.Add(weaponId, false);
                int uid = Inventory.Bag.Count > 0 ? Inventory.Bag[Inventory.Bag.Count - 1].Uid : 0;
                Inventory.Equip(uid, Core.ItemTable.Default.Get(weaponId));
            }

            SkillBar = Core.SkillTable.SlotOrder(classId);
            Progression.Level = level;
            RefreshFromEquipment();
            Core.Progression.ApplyLevel(State, level);
            State.Hp = State.HpMax;
            State.Armor = 12f + level * 2f;

            // 拼装角色模型（换职业会重建；纯表现层，不参与任何数值结算）
            RuntimeArt.AttachPlayerModel(gameObject, classId, female);
        }

        /// <summary>把背包中已穿戴装备的数值汇总进 ActorState（穿脱装备后必须调用）。</summary>
        public void RefreshFromEquipment()
        {
            float eap, esp, ehp, armor, wdps, wspeed;
            Inventory.Aggregate(Core.ItemTable.Default, out eap, out esp, out ehp, out armor, out wdps, out wspeed);
            State.E_AP = eap;
            State.E_SP = esp;
            State.E_HP = ehp;
            State.Armor = armor + 12f + State.Level * 2f;
            State.WeaponDps = wdps > 0f ? wdps : 8f;
            State.WeaponSpeed = wspeed > 0f ? wspeed : 2f;
        }

        public void SetTarget(ActorEntity target)
        {
            Target = target;
        }

        /// <summary>请求施放技能（只发意图 —— ADR-001 N-2）。</summary>
        public void RequestCast(Core.SkillId skill)
        {
            if (Authority == null) return;

            // 客户端本地冷却镜像：只为 UI 响应（真相仍在服务端 —— ADR-001 N-2）
            Core.SkillDef def = Core.SkillTable.Get(skill);
            if (def != null)
            {
                LocalCooldowns[(int)def.Slot] = def.Cooldown;
                if (def.UsesGcd) LocalCooldowns[0] = Core.SkillTable.GlobalCooldown;
            }

            int targetId = Target != null ? Target.ActorId : 0;
            Authority.RequestCast(ActorId, skill, targetId);
        }

        // 本地冷却镜像衰减（仅 UI）。索引 0 存公共 CD，1–7 对应技能槽。
        // 零热路径分配：固定遍历 0–7，不做任何 new（ADR-001 N-6）。
        private void Update()
        {
            float dt = Time.deltaTime;
            for (int slot = 0; slot <= 7; slot++)
            {
                float v;
                if (!LocalCooldowns.TryGetValue(slot, out v)) continue;
                v -= dt;
                if (v <= 0f) LocalCooldowns.Remove(slot);
                else LocalCooldowns[slot] = v;
            }
        }

        /// <summary>请求与 NPC 交互（对话 / 交任务）。</summary>
        public void RequestInteract(int npcId)
        {
            if (Authority != null) Authority.RequestInteract(ActorId, npcId);
        }

        /// <summary>请求拾取（直接进包，非掷骰场景）。</summary>
        public void RequestPickup(int itemUid)
        {
            if (Authority != null) Authority.RequestPickup(ActorId, itemUid);
        }

        /// <summary>提交拾取三选一（D-19）。</summary>
        public void RequestLootChoice(int sessionId, Core.LootChoice choice)
        {
            if (Authority != null) Authority.RequestLootChoice(ActorId, sessionId, choice);
        }

        /// <summary>请求穿戴背包中指定物品（缺陷 N-2：背包穿戴入口）。</summary>
        public void RequestEquip(int itemUid)
        {
            if (Authority != null) Authority.RequestEquip(ActorId, itemUid);
        }

        /// <summary>请求立即复活，跳过剩余等待（缺陷 N-1）。</summary>
        public void RequestRespawn()
        {
            if (Authority != null) Authority.RequestRespawn(ActorId);
        }

        /// <summary>是否处于等待复活状态（UI 显示死亡遮罩用）。</summary>
        public bool IsAwaitingRespawn => State != null && Core.RespawnRules.IsAwaitingRespawn(State);

        /// <summary>本地移动（表现层直接执行，服务端做位移终点校验 —— GDD §5.6 P-3）。</summary>
        public void Move(Vector3 direction, float dt)
        {
            if (State == null || State.Dead) return;
            float speed = CurrentMoveSpeed();
            Vector3 horizontal = direction;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude > 1f) horizontal.Normalize();

            // 重力（简易，避免依赖地形碰撞体精度）
            _verticalVelocity -= 18f * dt;
            Vector3 motion = horizontal * speed;
            motion.y = _verticalVelocity;

            if (_controller != null)
            {
                _controller.Move(motion * dt);
                if (_controller.isGrounded) _verticalVelocity = -1f;
            }
            else
            {
                transform.position += motion * dt;
            }

            if (horizontal.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(horizontal), 12f * dt);
        }

        public void Teleport(Vector3 to)
        {
            if (_controller != null) _controller.enabled = false;
            transform.position = to;
            if (_controller != null) _controller.enabled = true;
        }

        /// <summary>导出存档数据。</summary>
        public CharacterData ToSaveData()
        {
            var data = new CharacterData
            {
                Name = State.Name,
                ClassId = (int)State.Class,
                Female = Female,
                Level = Progression.Level,
                Xp = Progression.Xp,
                Silver = Silver,
                TotalKills = Progression.TotalKills
            };
            foreach (Core.ItemInstance it in Inventory.Bag)
            {
                data.BagItemDefIds.Add(it.DefId);
            }
            foreach (Core.EquipSlot slot in System.Enum.GetValues(typeof(Core.EquipSlot)))
            {
                int uid;
                if (Inventory.TryGetEquipped(slot, out uid))
                {
                    data.EquippedItemDefIds.Add((int)slot);
                    data.EquippedItemDefIds.Add(uid);
                }
            }
            foreach (Core.QuestProgress p in Quests.SnapshotActive())
            {
                data.QuestProgressQuestIds.Add(p.QuestId);
                data.QuestProgressCounts.Add(p.Count);
            }
            foreach (int done in Quests.SnapshotDone()) data.QuestDoneIds.Add(done);
            return data;
        }

        /// <summary>从存档恢复。</summary>
        public void ApplySaveData(CharacterData data)
        {
            // 缺陷 N-5：读档不再发放出生武器（存档背包里已含），否则每次读档多一把
            Setup((Core.ClassId)data.ClassId, data.Female, data.Name, data.Level, grantStartingWeapon: false);
            Progression.Xp = data.Xp;
            Progression.Level = data.Level;
            Silver = data.Silver;
            Progression.TotalKills = data.TotalKills;

            Inventory.Clear();
            foreach (int defId in data.BagItemDefIds) Inventory.Add(defId, false);
            for (int i = 0; i + 1 < data.EquippedItemDefIds.Count; i += 2)
            {
                Core.EquipSlot slot = (Core.EquipSlot)data.EquippedItemDefIds[i];
                int uid = data.EquippedItemDefIds[i + 1];
                Core.ItemInstance inst = Inventory.Find(uid);
                if (inst == null) continue;
                // 直接写入装备槽（存档恢复不走「穿戴」流程，避免重复生成 uid）
                Inventory.Equip(uid, Core.ItemTable.Default.Get(inst.DefId));
            }

            // 兜底：存档损坏或为空时补一把出生武器，保证伤害公式的 W/S 不为 0
            int weaponUid;
            if (!Inventory.TryGetEquipped(Core.EquipSlot.Weapon, out weaponUid))
            {
                int weaponId = (Core.ClassId)data.ClassId == Core.ClassId.SwordCultivator ? 1001 : 1002;
                int newUid = Inventory.Add(weaponId, false);
                Inventory.Equip(newUid, Core.ItemTable.Default.Get(weaponId));
            }

            var progressList = new List<Core.QuestProgress>();
            for (int i = 0; i < data.QuestProgressQuestIds.Count; i++)
            {
                progressList.Add(new Core.QuestProgress
                {
                    QuestId = data.QuestProgressQuestIds[i],
                    Count = i < data.QuestProgressCounts.Count ? data.QuestProgressCounts[i] : 0
                });
            }
            Quests.Restore(progressList, data.QuestDoneIds);

            RefreshFromEquipment();
            Core.Progression.ApplyLevel(State, data.Level);
            State.Hp = State.HpMax;
        }
    }
}
