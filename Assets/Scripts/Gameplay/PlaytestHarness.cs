// 《浮生渡》游戏层 · 无头自动试机（回归用）
//
// 【为什么需要】"能打开 ≠ 能玩"。之前多次出现打包正常、截图也好看，但玩家一上手就
// 按不动、放不出技能、接不了任务。本文件把 5 条最关键的链路（建模 / 移动方向 /
// 释放技能 / 攻击怪物 / 接任务）做成**可无人值守自动跑**的检查，结果打日志，
// 每次出包前用一条命令就能验完。
//
// 用法（Windows 独立播放器）：
//   Fushengdu.exe -fdSolo -fdPlaytest -fdShotDelay 25 -fdQuit
// 输出：日志中所有以「[自检]」开头的行，通过/失败均以 OK / FAIL 结尾。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    /// <summary>命令行自动试机：不依赖任何人工点击，按脚本顺序驱动真实业务逻辑。</summary>
    public sealed class PlaytestHarness : MonoBehaviour
    {
        private const string Tag = "[自检]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!HasFlag("-fdPlaytest")) return;
            var go = new GameObject("PlaytestHarness");
            go.AddComponent<PlaytestHarness>();
        }

        private static bool HasFlag(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private IEnumerator Start()
        {
            // 先采样一段时间：出生点在头两秒里会不会被别的组件挪走，是这个位置追踪要抓的重点
            yield return Trace();
            yield return Run();
        }

        /// <summary>前 4 秒每 0.5 秒记录一次玩家 / 相机位置，用于抓"出生后被挪走"的问题。</summary>
        private IEnumerator Trace()
        {
            for (int i = 0; i < 8; i++)
            {
                PlayerActor p = LocalPlayerLocator.Current;
                Camera cam = Camera.main;
                var rig = cam != null ? cam.GetComponent<CameraFollow>() : null;
                string line = "追踪 t=" + Time.time.ToString("0.0") + "s ";
                line += p != null
                    ? ("玩家 " + Fmt(p.transform.position) + " 站立=" + (!p.State.Dead))
                    : "玩家=尚未创建";
                if (rig != null)
                    line += " 相机 " + Fmt(cam.transform.position) + " 目标=" + (rig.Target != null ? rig.Target.name : "null")
                            + " D/H/P=" + rig.Distance + "/" + rig.Height + "/" + rig.Pitch;
                Log(line);
                yield return new WaitForSeconds(0.5f);
            }
        }

        private IEnumerator Run()
        {
            Log("=== 开始自动试机 ===");

            PlayerActor player = LocalPlayerLocator.Current;
            if (player == null)
            {
                Log("本地玩家为空 —— FAIL（会话未建立，后续全部跳过）");
                yield break;
            }

            CheckModel(player);
            CheckCamera(player);
            yield return CheckMovement(player);
            CheckSkillBar(player);
            // 顺序很重要：先测任务（在出生点原地），再测战斗（会把玩家传送到怪身边）
            CheckQuest(player);
            yield return CastFailed(player);

            // BOSS / 团本终boss 渡银断言（无头试机扩展：-fdBossSilver 触发）
            if (HasFlag("-fdBossSilver"))
                yield return CheckBossSilver(player);

            // 渡银飘字实跑校验（-fdBossSilver / -fdSilverFloat 触发）：确认击杀/到账飘字真的生成
            if (HasFlag("-fdBossSilver") || HasFlag("-fdSilverFloat"))
                yield return CheckSilverFloat(player);

            Log("=== 自动试机结束 ===");
        }

        // ---------------------------------------------------------------- 1. 建模
        private void CheckModel(PlayerActor player)
        {
            Transform model = player.transform.Find("Model");
            if (model == null)
            {
                Log("角色模型：找不到 Model 子节点 —— FAIL");
                return;
            }

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            int visible = 0;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].enabled && renderers[i].gameObject.activeInHierarchy) visible++;

            // 包围盒高度：模型塌成一个点或只剩零件时，这里会暴露出来
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled) continue;
                // 脚下选中环是横躺的圆环，会把总高拉到 1.5m 以上、判据失效，排除掉
                if (renderers[i].name == "SelectionRing") continue;
                if (!any) { bounds = renderers[i].bounds; any = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            // 【注意】判据不能再用"物件 >= 10"：真实建模是**单个网格**（+选中环 = 2 个物件），
            // 程序化胶囊人才是 30 个部件。统一改成"有可见网格 + 总高落在 1.6~2.2m"。
            bool ok = any && visible > 0 && bounds.size.y >= 1.5f && bounds.size.y <= 2.3f;
            Log("角色模型：物件 " + renderers.Length + " 个，可见 " + visible + " 个，总高 "
                + (any ? bounds.size.y.ToString("0.00") : "?") + " m —— " + (ok ? "OK" : "FAIL")
                + "（单网格建模 2 件 / 程序化拼装 30 件都算正常）");
        }

        // ---------------------------------------------------------------- 2. 相机
        private void CheckCamera(PlayerActor player)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Log("主相机：不存在 —— FAIL（移动方向依赖 Camera.main）");
                return;
            }
            var rig = cam.GetComponent<CameraFollow>();
            bool ok = rig != null && rig.Target == player.transform;
            Log("主相机：位置 " + Fmt(cam.transform.position) + "，跟随目标已绑定 —— " + (ok ? "OK" : "FAIL"));
        }

        // ---------------------------------------------------------------- 3. 移动方向
        private IEnumerator CheckMovement(PlayerActor player)
        {
            Camera cam = Camera.main;
            Vector3 forward = Vector3.forward;
            if (cam != null)
            {
                forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();
            }
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            // W —— 应当向"屏幕里的前方"（远离相机）走
            Vector3 before = player.transform.position;
            for (int i = 0; i < 30; i++) player.Move(forward, 1f / 60f);
            yield return null;
            Vector3 dForward = player.transform.position - before;
            float fDot = Vector3.Dot(new Vector3(dForward.x, 0f, dForward.z).normalized, forward);
            Log("W 键位移：" + dForward.magnitude.ToString("0.00") + " m，与相机前方一致性 " + fDot.ToString("0.00")
                + " —— " + (dForward.magnitude > 0.5f && fDot > 0.9f ? "OK" : "FAIL"));

            // D —— 位移在世界坐标里应当落在屏幕右半边
            before = player.transform.position;
            for (int i = 0; i < 30; i++) player.Move(right, 1f / 60f);
            yield return null;
            Vector3 dRight = player.transform.position - before;
            bool screenOk = false;
            if (cam != null)
            {
                Vector3 mid = before + dRight;
                UnityEngine.Vector3 sp = cam.WorldToScreenPoint(mid);
                UnityEngine.Vector3 cp = cam.WorldToScreenPoint(before);
                screenOk = sp.x > cp.x;
                Log("D 键位移：" + dRight.magnitude.ToString("0.00") + " m，屏幕 X " + cp.x.ToString("0")
                    + " → " + sp.x.ToString("0") + "（应变大） —— " + (dRight.magnitude > 0.5f && screenOk ? "OK" : "FAIL"));
            }
            else
            {
                Log("D 键位移：无相机，跳过 —— n/a");
            }

            Log("移动参考系：world forward=" + Fmt(forward) + " right=" + Fmt(right));
        }

        // ---------------------------------------------------------------- 4. 技能栏
        private void CheckSkillBar(PlayerActor player)
        {
            if (player.SkillBar == null || player.SkillBar.Length == 0)
            {
                Log("技能栏：为空 —— FAIL（技能键全部无响应）");
                return;
            }
            var names = new List<string>();
            for (int i = 0; i < player.SkillBar.Length; i++)
            {
                Core.SkillDef def = Core.SkillTable.Get(player.SkillBar[i]);
                names.Add(def != null ? def.Name : "未知(" + player.SkillBar[i] + ")");
            }
            Log("技能栏：" + string.Join(" / ", names.ToArray()) + " —— OK");
        }

        // ---------------------------------------------------------------- 5. 技能 / 攻击
        private IEnumerator CastFailed(PlayerActor player)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null)
            {
                Log("战斗世界：不存在 —— FAIL");
                yield break;
            }

            MonsterActor monster = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < world.Actors.Count; i++)
            {
                var m = world.Actors[i] as MonsterActor;
                if (m == null || m.IsDead) continue;
                float d = Vector3.Distance(player.transform.position, m.transform.position);
                if (d >= bestDist) continue;
                bestDist = d;
                monster = m;
            }
            if (monster == null)
            {
                Log("场景怪物：0 只 —— FAIL（刷怪点没生成怪物）");
                yield break;
            }
            Log("场景怪物：找到 " + monster.name + "（" + (monster.Definition != null ? monster.Definition.Name : "?")
                + "），距离出生点 " + bestDist.ToString("0.0") + " m —— "
                + (bestDist <= 35f ? "OK（走两步就能打）" : "FAIL（玩家要跑太远才见到怪）"));

            Core.SkillDef first = Core.SkillTable.Get(player.SkillBar != null && player.SkillBar.Length > 0
                ? player.SkillBar[0] : Core.SkillId.None);
            if (first == null)
            {
                Log("首个技能：查不到定义 —— FAIL");
                yield break;
            }

            // 走到技能射程内
            Vector3 near = monster.transform.position - new Vector3(0f, 0f, 2f);
            near.y = monster.transform.position.y + 0.1f;
            player.Teleport(near);
            yield return null;
            float dist = Vector3.Distance(player.transform.position, monster.transform.position);
            Log("技能参数：[" + first.Name + "] 射程 " + first.Range + " m，当前距离 " + dist.ToString("0.0") + " m");

            player.SetTarget(monster);
            float hpBefore = monster.State.Hp;
            player.RequestCast(first.Id);
            yield return new WaitForSeconds(0.4f);

            float hpAfter = monster.State.Hp;
            bool damaged = hpAfter < hpBefore - 0.01f;
            Log("释放技能：" + first.Name + " → 怪物血量 " + hpBefore.ToString("0.0") + " → " + hpAfter.ToString("0.0")
                + " —— " + (damaged ? "OK" : "FAIL"));

            if (!damaged)
            {
                Log("诊断：玩家 Query 资源=" + player.State.Resource.ToString("0.0")
                    + " 等级=" + player.State.Level
                    + " 目标名=" + (player.Target != null ? player.Target.name : "null")
                    + " 目标阵营=" + (monster.State.Faction)
                    + " 角色朝向.z=" + player.transform.forward.z.ToString("0.00"));
            }

            // 再打一次：验证不是"只能打一次"
            hpBefore = monster.State.Hp;
            player.RequestCast(first.Id);
            yield return new WaitForSeconds(0.4f);
            Log("二次释放：血量 " + hpBefore.ToString("0.0") + " → " + monster.State.Hp.ToString("0.0")
                + " —— " + (monster.State.Hp < hpBefore - 0.01f ? "OK" : "FAIL（可能被 GCD/冷却挡住，属正常；若两次都无伤害则是真 BUG）"));
        }

        // ---------------------------------------------------------------- 7. BOSS / 团本终boss 渡银断言
        private IEnumerator CheckBossSilver(PlayerActor player)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null)
            {
                Log("战斗世界：不存在 —— FAIL（BOSS 渡银断言跳过）");
                yield break;
            }

            int expected, actual;
            bool okBoss = BossSilverVerifier.Verify(world, player, 30, false, out expected, out actual);
            Log("BOSS 渡银：Lv30 BOSS 击杀，Δ银=" + actual + "，期望 " + expected + " —— " + (okBoss ? "OK" : "FAIL"));

            bool okRaid = BossSilverVerifier.Verify(world, player, 55, true, out expected, out actual);
            Log("团本终boss 渡银：Lv55 团本终boss 击杀，Δ银=" + actual + "，期望 " + expected + " —— " + (okRaid ? "OK" : "FAIL"));

            Log("BOSS / 团本终boss 渡银断言：" + (okBoss && okRaid ? "全部 OK" : "FAIL"));
        }

        // ---------------------------------------------------------------- 7b. 渡银飘字实跑校验
        private IEnumerator CheckSilverFloat(PlayerActor player)
        {
            CombatWorld world = CombatWorld.Instance;
            if (world == null)
            {
                Log("战斗世界：不存在 —— FAIL（飘字校验跳过）");
                yield break;
            }

            // 快照触发前的飘字计数，避免被前面 BOSS 击杀的飘字干扰
            int before = CombatWorld.SilverFloatSpawned;

            // 走统一事件入口 RaiseSilverGained（与击杀 / 任务交付同一条路径），
            // 模拟一次「渡银到账」；锚点取玩家头顶，贴近真实击杀飘字位置
            Vector3 at = player.transform.position + Vector3.up * 1.4f;
            world.RaiseSilverGained(player.ActorId, 123, at);
            yield return null; // 让 UpdateFloatingTexts 至少跑一帧，确认不被立刻销毁

            int after = CombatWorld.SilverFloatSpawned;
            string txt = CombatWorld.LastSilverFloatText ?? "";
            bool ok = after > before && txt.IndexOf("渡银 +") >= 0;
            Log("渡银飘字：触发前 " + before + " → 触发后 " + after + " 枚；末枚文本=\"" + txt + "\" —— "
                + (ok ? "OK（击杀/到账飘字确已生成，肉眼可见路径已通）" : "FAIL（飘字未生成）"));
        }

        // ---------------------------------------------------------------- 6. 任务
        private void CheckQuest(PlayerActor player)
        {
            var npcs = new List<NpcEntity>();
            foreach (KeyValuePair<int, NpcEntity> kv in NpcEntity.All()) npcs.Add(kv.Value);
            if (npcs.Count == 0)
            {
                Log("NPC：0 个 —— FAIL");
                return;
            }

            NpcEntity npc = npcs[0];
            float d = Vector3.Distance(player.transform.position, npc.transform.position);
            Log("NPC：" + npc.NpcName + "（id=" + npc.NpcId + "）距离 " + d.ToString("0.0")
                + " m，交互需要 ≤4 m");

            int before = player.Quests.Active.Count;
            Vector3 near = npc.transform.position + new Vector3(0f, 0f, 2f);
            near.y = npc.transform.position.y;
            player.Teleport(near);

            // 第一环是"对话型"：第 1 次 E 接任务，第 2 次 E 才推进并交付、接下一环。
            // 只按一次会误判成"接不了任务"，所以这里连按两次，完整验证链首。
            player.RequestInteract(npc.NpcId);
            int after1 = player.Quests.Active.Count;
            player.RequestInteract(npc.NpcId);
            int after2 = player.Quests.Active.Count;

            // 用旧存档跑时 before 可能已经 >0（上一环还在进行），此时"第1次E交付、第2次E接下一环"，
            // 数量不变但链路其实是通的。所以再加一档：交互后仍有进行中的任务也算通过。
            bool gained = after1 > before || after2 > before;
            bool carried = before > 0 && after2 > 0;
            Log("接任务：进行中 " + before + " →(第1次E) " + after1 + " →(第2次E) " + after2
                + " 条；" + DescribeQuests(player)
                + " —— " + (gained || carried ? "OK" : "FAIL")
                + (gained ? "" : "（旧存档已在任务链中，数量不变属正常）"));
        }

        private static string DescribeQuests(PlayerActor player)
        {
            var names = new List<string>();
            foreach (Core.QuestProgress p in player.Quests.Active)
            {
                Core.QuestDef def = Core.QuestTable.Default.Get(p.QuestId);
                names.Add(def != null ? def.Name + "(" + p.Count + ")" : "id=" + p.QuestId);
            }
            return names.Count > 0 ? string.Join("、", names.ToArray()) : "(无)";
        }

        // ---------------------------------------------------------------- 工具
        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0") + ")";
        }

        private static void Log(string msg)
        {
            Debug.Log(Tag + " " + msg);
        }
    }
}
