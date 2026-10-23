// 《浮生渡》领域层 · 复活规则（纯 C#，可被单测直接覆盖）
// @trace 缺陷 N-1：玩家死亡后必须可复活，否则 1→15 成长循环在第一次死亡后断裂
// @trace ADR-001 N-2：复活由「权威端」触发；客户端只提交复活请求，不得自行改状态
// 数值一律集中在本文件，便于试玩用例直接引用期望值做比对。
namespace Fushengdu.Core
{
    public static class RespawnRules
    {
        /// <summary>死亡后到自动复活的等待时间（秒）。与 CombatWorld.RespawnDelaySeconds 一致。</summary>
        public const float DefaultDelaySeconds = 5.0f;

        /// <summary>复活后生命比例（1.0 = 满血）。用例 B-23 的期望值基准。</summary>
        public const float RespawnHealthRatio = 1.0f;

        /// <summary>复活后资源比例（0 = 资源归零；剑气/怒气均从 0 起步）。</summary>
        public const float RespawnResourceRatio = 0f;

        /// <summary>是否处于「等待复活」状态。</summary>
        public static bool IsAwaitingRespawn(ActorState actor)
        {
            return actor != null && actor.Dead && actor.RespawnTimer > 0f;
        }

        /// <summary>标记死亡并开始复活倒计时。</summary>
        public static void BeginRespawn(ActorState actor, float delaySeconds = DefaultDelaySeconds)
        {
            if (actor == null) return;
            actor.Dead = true;
            actor.Hp = 0f;
            actor.RespawnTimer = delaySeconds > 0f ? delaySeconds : 0f;
        }

        /// <summary>
        /// 推进复活倒计时。返回 true 表示「本帧应当执行复活」。
        /// 注意：倒计时由权威端单点推进，客户端不得调用（避免状态分叉）。
        /// </summary>
        public static bool Advance(ActorState actor, float dt)
        {
            if (!IsAwaitingRespawn(actor)) return false;
            actor.RespawnTimer -= dt;
            if (actor.RespawnTimer > 0f) return false;
            actor.RespawnTimer = 0f;
            return true;
        }

        /// <summary>
        /// 执行复活：满血、资源归零、清除持续效果与控制。
        /// 技能 CD **不**重置（复活不是重置战斗资源的手段，避免用于刷 CD）。
        /// </summary>
        public static void ApplyRespawn(ActorState actor)
        {
            if (actor == null) return;
            actor.Dead = false;
            actor.RespawnTimer = 0f;
            actor.Hp = actor.HpMax * RespawnHealthRatio;
            actor.Resource = actor.ResourceMax * RespawnResourceRatio;
            actor.Buffs.Clear();
            actor.Control = ControlKind.None;
            actor.ControlRemaining = 0f;
            actor.CastTimeRemaining = 0f;
            actor.PendingSkill = SkillId.None;
        }

        /// <summary>死亡状态下是否禁止行动（移动 / 施法 / 交互）。</summary>
        public static bool IsActionBlocked(ActorState actor)
        {
            return actor != null && actor.Dead;
        }
    }
}
