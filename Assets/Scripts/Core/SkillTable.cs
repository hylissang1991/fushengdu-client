// 《浮生渡》领域层 · 技能表（剑修 / 狂战士 各 7 技能）
// 逐条回指《浮生渡-职业系统GDD-v1》§2.6.1 / §2.6.2 与统一常量 §2.6。
// 位移距离、控制时长一律为常量（GDD §2.4 红线）。
namespace Fushengdu.Core
{
    public static class SkillTable
    {
        /// <summary>公共 CD（秒）—— 概念文档 §3.1。</summary>
        public const float GlobalCooldown = 1.0f;

        /// <summary>近战距离（米）—— GDD §2.6 统一常量。</summary>
        public const float MeleeRange = 3.0f;

        /// <summary>变身时长（秒）—— 全职业统一。</summary>
        public const float TransformationDuration = 15.0f;

        private static readonly System.Collections.Generic.Dictionary<SkillId, SkillDef> _defs =
            new System.Collections.Generic.Dictionary<SkillId, SkillDef>();

        static SkillTable()
        {
            RegisterSwordCultivator();
            RegisterBerserker();
        }

        public static SkillDef Get(SkillId id)
        {
            SkillDef def;
            return _defs.TryGetValue(id, out def) ? def : null;
        }

        public static bool TryGet(SkillId id, out SkillDef def)
        {
            return _defs.TryGetValue(id, out def);
        }

        // ---------------------------------------------------------------- 剑修
        private static void RegisterSwordCultivator()
        {
            ClassId c = ClassId.SwordCultivator;

            // 槽 1 青锋三叠：近战三段连击；产 Qi 12/段（共 36）；整体占 1 次 GCD
            Add(new SkillDef
            {
                Id = SkillId.Sword_QingFengSanDie, Class = c, Slot = SkillSlot.Fill1,
                Name = "青锋三叠", Cooldown = 0f, Range = MeleeRange, CastTime = 0.6f,
                Shape = SkillShape.SingleTarget, ResourceDelta = 12f,
                K = new[] { 0.75f, 0.85f, 1.10f }, MaxTargets = 1
            });

            // 槽 2 剑气纵横：需御剑姿态；耗 Qi 15；20 m（姿态 30 m）；穿透 2 目标，第 2 个起 ×0.70
            Add(new SkillDef
            {
                Id = SkillId.Sword_JianQiZongHeng, Class = c, Slot = SkillSlot.Fill2,
                Name = "剑气纵横", Cooldown = 0f, Range = 20f, CastTime = 0.5f,
                Shape = SkillShape.SingleTarget, ResourceDelta = -15f,
                K = new[] { 0.90f, 0.63f }, MaxTargets = 2,
                RequiredStance = StanceId.YuJian,
                Control = ControlKind.Slow, ControlDurationVsMonster = 4f, ControlDurationVsPlayer = 3f
            });

            // 槽 3 流云步：位移 8 m；产 Qi 20；CD 12 s；落地 2 s 内下一次青锋三叠 K +0.20
            Add(new SkillDef
            {
                Id = SkillId.Sword_LiuYunBu, Class = c, Slot = SkillSlot.Utility1,
                Name = "流云步", Cooldown = 12f, Range = 0f, CastTime = 0f,
                Shape = SkillShape.Displacement, ResourceDelta = 20f,
                Displacement = 8f, EffectDuration = 2f, K = new float[0]
            });

            // 槽 4 断岳式：硬控定身 1.5 s（对玩家 1.0 s）；护甲 −15% / 8 s；产 Qi 25；CD 15 s
            Add(new SkillDef
            {
                Id = SkillId.Sword_DuanYueShi, Class = c, Slot = SkillSlot.Utility2,
                Name = "断岳式", Cooldown = 15f, Range = MeleeRange, CastTime = 0.4f,
                Shape = SkillShape.SingleTarget, ResourceDelta = 25f,
                K = new[] { 1.30f }, MaxTargets = 1,
                Control = ControlKind.Stun, ControlDurationVsMonster = 1.5f, ControlDurationVsPlayer = 1.0f,
                EffectDuration = 8f
            });

            // 槽 5 万剑归宗：耗 Qi 50；CD 24 s；半径 6 m；主目标 K 2.10，其他 1.20；命中 ≥3 进御剑姿态 6 s
            Add(new SkillDef
            {
                Id = SkillId.Sword_WanJianGuiZong, Class = c, Slot = SkillSlot.Core,
                Name = "万剑归宗", Cooldown = 24f, Range = 0f, Radius = 6f, CastTime = 0.8f,
                Shape = SkillShape.SelfAoe, ResourceDelta = -50f,
                K = new[] { 2.10f, 1.20f }, MaxTargets = 8, EffectDuration = 6f
            });

            // 槽 6 天剑·斩虚：CD 90 s；25 m；K 3.60 并等效无视 30% 护甲
            Add(new SkillDef
            {
                Id = SkillId.Sword_TianJianZhanXu, Class = c, Slot = SkillSlot.Ultimate,
                Name = "天剑·斩虚", Cooldown = 90f, Range = 25f, CastTime = 1.0f,
                Shape = SkillShape.SingleTarget, ResourceDelta = 0f,
                K = new[] { 3.60f }, MaxTargets = 1
            });

            // 槽 7 御剑化形：CD 60 s；持续 15 s：御剑姿态 + P_AP ×1.12 + 移速 +20% + 免疫减速（≠ 免控）
            Add(new SkillDef
            {
                Id = SkillId.Sword_YuJianHuaXing, Class = c, Slot = SkillSlot.Transformation,
                Name = "御剑化形", Cooldown = 60f, Range = 0f, CastTime = 0f,
                Shape = SkillShape.SelfBuff, ResourceDelta = 0f, UsesGcd = false,
                K = new float[0], EffectDuration = TransformationDuration
            });
        }

        // -------------------------------------------------------------- 狂战士
        private static void RegisterBerserker()
        {
            ClassId c = ClassId.Berserker;

            // 槽 1 狂澜双斩：双持两段；产 Rage 10/段（共 20）
            Add(new SkillDef
            {
                Id = SkillId.Berserker_KuangLanShuangZhan, Class = c, Slot = SkillSlot.Fill1,
                Name = "狂澜双斩", Cooldown = 0f, Range = MeleeRange, CastTime = 0.5f,
                Shape = SkillShape.SingleTarget, ResourceDelta = 10f,
                K = new[] { 0.80f, 1.00f }, MaxTargets = 1
            });

            // 槽 2 裂骨重击：耗 Rage 25；K 1.60；破甲 −15% / 8 s，可叠加至 −30%
            Add(new SkillDef
            {
                Id = SkillId.Berserker_LieGuZhongJi, Class = c, Slot = SkillSlot.Fill2,
                Name = "裂骨重击", Cooldown = 0f, Range = MeleeRange, CastTime = 0.7f,
                Shape = SkillShape.SingleTarget, ResourceDelta = -25f,
                K = new[] { 1.60f }, MaxTargets = 1, EffectDuration = 8f
            });

            // 槽 3 冲锋陷阵：位移 12 m；产 Rage 15；CD 12 s；落地 K 0.60
            Add(new SkillDef
            {
                Id = SkillId.Berserker_ChongFengXianZhen, Class = c, Slot = SkillSlot.Utility1,
                Name = "冲锋陷阵", Cooldown = 12f, Range = 0f, CastTime = 0f,
                Shape = SkillShape.Displacement, ResourceDelta = 15f,
                Displacement = 12f, K = new[] { 0.60f }
            });

            // 槽 4 战吼·撼地：半径 5 m；产 Rage 20；CD 15 s；K 0.50；恐惧+击退 1.0 s（对玩家 0.8 s）
            Add(new SkillDef
            {
                Id = SkillId.Berserker_ZhanHouHanDi, Class = c, Slot = SkillSlot.Utility2,
                Name = "战吼·撼地", Cooldown = 15f, Range = 0f, Radius = 5f, CastTime = 0f,
                Shape = SkillShape.SelfAoe, ResourceDelta = 20f,
                K = new[] { 0.50f }, MaxTargets = 8,
                Control = ControlKind.Fear, ControlDurationVsMonster = 1.0f, ControlDurationVsPlayer = 0.8f
            });

            // 槽 5 血怒狂击：耗 Rage 40；CD 20 s；K 2.40 × (1 + 0.30 × 破甲层数)
            Add(new SkillDef
            {
                Id = SkillId.Berserker_XueNuKuangJi, Class = c, Slot = SkillSlot.Core,
                Name = "血怒狂击", Cooldown = 20f, Range = MeleeRange, CastTime = 0.8f,
                Shape = SkillShape.SingleTarget, ResourceDelta = -40f,
                K = new[] { 2.40f }, MaxTargets = 1
            });

            // 槽 6 诸神黄昏·终斩：CD 100 s；目标 HP ≥30% → K 4.00；<30% → K 5.00（处决）
            Add(new SkillDef
            {
                Id = SkillId.Berserker_ZhuShenHuangHun, Class = c, Slot = SkillSlot.Ultimate,
                Name = "诸神黄昏·终斩", Cooldown = 100f, Range = MeleeRange, CastTime = 1.0f,
                Shape = SkillShape.SingleTarget, ResourceDelta = 0f,
                K = new[] { 4.00f, 5.00f }, MaxTargets = 1
            });

            // 槽 7 英灵战躯：CD 60 s；持续 15 s：吸血 +25% / 受伤 −15% / 免疫恐惧 / 移速 +15%
            Add(new SkillDef
            {
                Id = SkillId.Berserker_YingLingZhanQu, Class = c, Slot = SkillSlot.Transformation,
                Name = "英灵战躯", Cooldown = 60f, Range = 0f, CastTime = 0f,
                Shape = SkillShape.SelfBuff, ResourceDelta = 0f, UsesGcd = false,
                K = new float[0], EffectDuration = TransformationDuration
            });
        }

        private static void Add(SkillDef def)
        {
            _defs[def.Id] = def;
        }

        /// <summary>取某职业某槽位的技能（用于技能栏与默认键位）。</summary>
        public static SkillId[] SlotOrder(ClassId c)
        {
            if (c == ClassId.SwordCultivator)
            {
                return new[]
                {
                    SkillId.Sword_QingFengSanDie, SkillId.Sword_JianQiZongHeng, SkillId.Sword_LiuYunBu,
                    SkillId.Sword_DuanYueShi, SkillId.Sword_WanJianGuiZong, SkillId.Sword_TianJianZhanXu,
                    SkillId.Sword_YuJianHuaXing
                };
            }
            if (c == ClassId.Berserker)
            {
                return new[]
                {
                    SkillId.Berserker_KuangLanShuangZhan, SkillId.Berserker_LieGuZhongJi,
                    SkillId.Berserker_ChongFengXianZhen, SkillId.Berserker_ZhanHouHanDi,
                    SkillId.Berserker_XueNuKuangJi, SkillId.Berserker_ZhuShenHuangHun,
                    SkillId.Berserker_YingLingZhanQu
                };
            }
            return new SkillId[0];
        }
    }
}
