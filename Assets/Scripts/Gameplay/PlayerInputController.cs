// 《浮生渡》表现层 · 输入与目标选择
// @trace 职业系统 GDD §2.3（键位 ≤10：1 2 3 4 5 / R / F / Tab / 左键 / 空格）
// @trace D-12 可访问性 Standard：全部键位可重映射（本类集中定义，便于后续接入重映射表）。
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>第三人称跟随相机（简易轨道，不依赖 Cinemachine）。</summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        public float Distance = 8f;
        public float Height = 3.2f;
        public float Yaw;
        public float Pitch = 18f;
        public float YawSpeed = 120f;

        private void LateUpdate()
        {
            if (Target == null) return;
            if (Input.GetMouseButton(1)) Yaw += Input.GetAxis("Mouse X") * YawSpeed * Time.deltaTime;

            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -Distance);
            Vector3 desired = Target.position + new Vector3(0f, Height, 0f) + offset;

            // 简易地形贴地：避免相机穿到地面以下
            float ground = SampleGround(desired);
            if (desired.y < ground + 1.2f) desired.y = ground + 1.2f;

            transform.position = desired;
            transform.LookAt(Target.position + new Vector3(0f, 1.4f, 0f));
        }

        private static float SampleGround(Vector3 at)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(at.x, 200f, at.z), Vector3.down, out hit, 400f))
                return hit.point.y;
            Terrain t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(at) : 0f;
        }
    }

    /// <summary>
    /// 玩家输入：移动 / 选目标 / 技能键 / 交互。
    /// 所有按键只产生「意图」，结果一律由权威端裁决（ADR-001 N-2）。
    /// </summary>
    public sealed class PlayerInputController : MonoBehaviour
    {
        public PlayerActor Player;

        [Header("键位（可重映射入口）")]
        public KeyCode KeySlot1 = KeyCode.Alpha1;
        public KeyCode KeySlot2 = KeyCode.Alpha2;
        public KeyCode KeySlot3 = KeyCode.Alpha3;
        public KeyCode KeySlot4 = KeyCode.Alpha4;
        public KeyCode KeySlot5 = KeyCode.Alpha5;
        public KeyCode KeyUltimate = KeyCode.R;
        public KeyCode KeyTransformation = KeyCode.F;
        public KeyCode KeyTargetCycle = KeyCode.Tab;
        public KeyCode KeyInteract = KeyCode.E;
        public KeyCode KeyJump = KeyCode.Space;

        /// <summary>Q：镜头左转（E 留给交互；右键拖拽也能自由转视角）。</summary>
        public KeyCode KeyTurnLeft = KeyCode.Q;
        public float TurnStep = 45f;

        public float InteractRange = 4f;

        private float _autoSaveTimer;
        private CameraFollow _rig;

        private CameraFollow Rig
        {
            get
            {
                if (_rig != null) return _rig;
                Camera cam = Camera.main;
                if (cam == null) return null;
                _rig = cam.GetComponent<CameraFollow>();
                return _rig;
            }
        }

        private void Update()
        {
            if (Player == null || Player.State == null) return;

            HandleMovement();
            HandleCamera();
            HandleTargeting();
            HandleSkills();
            HandleInteract();
            HandleAutoSave();
        }

        private void HandleMovement()
        {
            if (Player.State.Dead) return;
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 dir = forward * v + right * h;
            Player.Move(dir, Time.deltaTime);
        }

        /// <summary>Q 键转视角：MMO 玩家最常用的"边走边看"，没有它只能靠右键拖拽。</summary>
        private void HandleCamera()
        {
            if (Input.GetKeyDown(KeyTurnLeft) && Rig != null) Rig.Yaw -= TurnStep;
        }

        private void HandleTargeting()
        {
            // 左键点击选目标
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : default(Ray);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 200f))
                {
                    var actor = hit.collider.GetComponentInParent<ActorEntity>();
                    if (actor != null && actor != Player)
                    {
                        Player.SetTarget(actor);
                        return;
                    }
                }
            }

            // Tab：在视野内最近的敌对单位之间循环
            if (Input.GetKeyDown(KeyTargetCycle))
            {
                Player.SetTarget(FindNextTarget());
            }
        }

        private ActorEntity FindNextTarget()
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null) return null;
            ActorEntity best = null;
            float bestDist = float.MaxValue;
            ActorEntity current = Player.Target;
            for (int i = 0; i < world.Actors.Count; i++)
            {
                ActorEntity a = world.Actors[i];
                if (a == null || a.IsDead || a == Player) continue;
                if (a.State.Faction == Core.ActorFaction.Player) continue;
                if (a == current) continue;
                float d = (a.transform.position - Player.transform.position).sqrMagnitude;
                if (d >= bestDist) continue;
                bestDist = d;
                best = a;
            }
            return best;
        }

        private void HandleSkills()
        {
            if (Player.State.Dead) return;
            if (Input.GetKeyDown(KeySlot1)) CastSlot(0);
            if (Input.GetKeyDown(KeySlot2)) CastSlot(1);
            if (Input.GetKeyDown(KeySlot3)) CastSlot(2);
            if (Input.GetKeyDown(KeySlot4)) CastSlot(3);
            if (Input.GetKeyDown(KeySlot5)) CastSlot(4);
            if (Input.GetKeyDown(KeyUltimate)) CastSlot(5);
            if (Input.GetKeyDown(KeyTransformation)) CastSlot(6);
        }

        private void CastSlot(int index)
        {
            if (Player.SkillBar == null || index < 0 || index >= Player.SkillBar.Length) return;
            Player.RequestCast(Player.SkillBar[index]);
        }

        private void HandleInteract()
        {
            if (!Input.GetKeyDown(KeyInteract)) return;
            if (Player == null) return;

            // 死亡状态下禁止一切交互（缺陷 N-1：死亡必须真的「死」）
            if (Player.State != null && Core.RespawnRules.IsActionBlocked(Player.State)) return;

            // 优先：NPC
            NpcEntity nearestNpc = null;
            float npcDist = InteractRange;
            foreach (KeyValuePair<int, NpcEntity> kv in NpcEntity.All())
            {
                float d = Vector3.Distance(Player.transform.position, kv.Value.transform.position);
                if (d > npcDist) continue;
                npcDist = d;
                nearestNpc = kv.Value;
            }
            if (nearestNpc != null)
            {
                Player.RequestInteract(nearestNpc.NpcId);
                return;
            }

            // 其次：采集点
            var nodes = FindObjectsOfType<GatherNode>();
            for (int i = 0; i < nodes.Length; i++)
            {
                GatherNode node = nodes[i];
                if (!node.Available) continue;
                if (Vector3.Distance(Player.transform.position, node.transform.position) > node.InteractRange) continue;
                int defId, amount;
                if (!node.TryGather(out defId, out amount)) continue;
                Player.Quests.OnGather(defId, amount);
                Player.Inventory.Add(defId, false);
                break;
            }
        }

        private void HandleAutoSave()
        {
            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer < 30f) return;
            _autoSaveTimer = 0f;
            SaveSystem.Save(Player.ToSaveData());
        }

        /// <summary>
        /// 退出时补一次存档。自动存档是 30 秒一轮，直接关窗口最多丢半分钟进度 ——
        /// 单机试玩时「打完一只怪就退」这种最常见操作，正好落在丢失区间里。
        /// </summary>
        private void OnApplicationQuit()
        {
            if (Player != null) SaveSystem.Save(Player.ToSaveData());
        }
    }
}
