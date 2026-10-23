// 《浮生渡》领域层 · 怪物定义、仇恨表与 AI 决策
// @trace 职业系统 GDD §2.6（坦克威胁值口径）、§5（边缘情况）
// 决策在领域层（纯 C#），移动与动画由表现层执行 —— 保证「同一份 AI 逻辑」可在服务端权威运行。
using System;
using System.Collections.Generic;

namespace Fushengdu.Core
{
    public sealed class MonsterDef
    {
        public int Id;
        public string Name = "怪物";
        public int Level = 1;
        public bool IsElite;
        public bool IsBoss;

        public float HpMax = 100f;
        public float Armor = 10f;
        public float WeaponDps = 8f;   // W
        public float WeaponSpeed = 2f; // S
        public float E_AP = 0f;        // 怪物攻强

        public float AggroRadius = 10f;   // 米
        public float LeashRadius = 22f;   // 脱离战斗并回位的距离（米）
        public float AttackRange = 3f;    // 米
        public float AttackInterval = 2f; // 秒
        public float AttackK = 0.60f;     // 普攻系数
        public float MoveSpeed = 2.5f;    // 米/秒

        public int RespawnSeconds = 20;
    }

    /// <summary>怪物 AI 意图（表现层据此执行移动 / 播放动画 / 发起攻击）。</summary>
    public enum MonsterIntent
    {
        Idle = 0,
        Chase = 1,
        Attack = 2,
        Return = 3,
        Dead = 4
    }

    /// <summary>
    /// 仇恨表。伤害产生威胁，治疗产生较低威胁（GDD §2.6.5：坦克技能威胁值 ×1.5）。
    /// </summary>
    public sealed class ThreatTable
    {
        private readonly Dictionary<int, float> _threat = new Dictionary<int, float>();

        public void Add(int actorId, float amount)
        {
            if (amount <= 0f) return;
            float cur;
            _threat.TryGetValue(actorId, out cur);
            _threat[actorId] = cur + amount;
        }

        public void Remove(int actorId)
        {
            _threat.Remove(actorId);
        }

        public void Clear()
        {
            _threat.Clear();
        }

        /// <summary>取仇恨最高的目标 Id；0 = 无。</summary>
        public int Top()
        {
            int best = 0;
            float bestValue = 0f;
            foreach (KeyValuePair<int, float> kv in _threat)
            {
                if (kv.Value <= bestValue) continue;
                bestValue = kv.Value;
                best = kv.Key;
            }
            return best;
        }

        public float ThreatOf(int actorId)
        {
            float v;
            return _threat.TryGetValue(actorId, out v) ? v : 0f;
        }
    }

    /// <summary>
    /// 怪物 AI 决策核心。输入为目标距离与自身状态，输出意图。
    /// 不持有任何 Unity 类型，可在服务端 headless 运行（ADR-001 §4.2）。
    /// </summary>
    public sealed class MonsterBrain
    {
        public int TargetId;
        public float AttackTimer;
        public float LeashTimer;

        /// <summary>
        /// 推进一帧。
        /// </summary>
        /// <param name="dt">帧间隔（秒）</param>
        /// <param name="self">怪物自身状态</param>
        /// <param name="def">怪物定义</param>
        /// <param name="distToTarget">与当前目标的距离（米）；负值表示无目标</param>
        /// <param name="distToSpawn">与出生点的距离（米）</param>
        /// <param name="hasTarget">是否有合法目标</param>
        /// <param name="attackReady">输出：本帧是否应发起一次普攻</param>
        public MonsterIntent Update(float dt, ActorState self, MonsterDef def,
            float distToTarget, float distToSpawn, bool hasTarget, out bool attackReady)
        {
            attackReady = false;
            if (self == null || def == null) return MonsterIntent.Idle;
            if (self.Dead) return MonsterIntent.Dead;

            // 硬控中无法行动
            if (self.Control != ControlKind.None && ActorState.IsHardControl(self.Control))
            {
                AttackTimer = def.AttackInterval;
                return MonsterIntent.Idle;
            }

            if (hasTarget)
            {
                // 脱离：超出脱离半径则回位并清空仇恨
                if (distToSpawn > def.LeashRadius)
                {
                    LeashTimer += dt;
                    if (LeashTimer >= 0.5f)
                    {
                        TargetId = 0;
                        return MonsterIntent.Return;
                    }
                }
                else
                {
                    LeashTimer = 0f;
                }

                if (distToTarget > def.AttackRange)
                {
                    AttackTimer = def.AttackInterval;
                    return MonsterIntent.Chase;
                }

                AttackTimer += dt;
                if (AttackTimer >= def.AttackInterval)
                {
                    AttackTimer = 0f;
                    attackReady = true;
                }
                return MonsterIntent.Attack;
            }

            LeashTimer = 0f;
            if (distToSpawn > 0.5f) return MonsterIntent.Return;
            return MonsterIntent.Idle;
        }

        /// <summary>寻找进入仇恨半径的目标（由表现层遍历后调用）。</summary>
        public void Acquire(int actorId)
        {
            TargetId = actorId;
            AttackTimer = 0f;
        }

        public void Release()
        {
            TargetId = 0;
            AttackTimer = 0f;
            LeashTimer = 0f;
        }
    }

    /// <summary>怪物定义表（本期：灰狼 / 獠牙野猪王（精英）/ 林间山贼）。</summary>
    public sealed class MonsterTable
    {
        private readonly Dictionary<int, MonsterDef> _defs = new Dictionary<int, MonsterDef>();

        private static MonsterTable _default;
        public static MonsterTable Default => _default ?? (_default = Build());

        private static MonsterTable Build()
        {
            var t = new MonsterTable();

            // —— 原有 3 只（东荒野外代表，保留以兼容现存场景刷怪点）——
            t.Add(new MonsterDef
            {
                Id = 1, Name = "林间灰狼", Level = 2, HpMax = 90f, Armor = 8f,
                WeaponDps = 7f, WeaponSpeed = 1.8f, E_AP = 4f,
                AggroRadius = 9f, AttackRange = 3f, AttackInterval = 2.0f, AttackK = 0.55f, MoveSpeed = 3.2f
            });
            t.Add(new MonsterDef
            {
                Id = 2, Name = "獠牙野猪王", Level = 7, IsElite = true, HpMax = 520f, Armor = 26f,
                WeaponDps = 16f, WeaponSpeed = 2.2f, E_AP = 18f,
                AggroRadius = 11f, AttackRange = 3f, AttackInterval = 2.4f, AttackK = 0.85f, MoveSpeed = 2.6f,
                RespawnSeconds = 45
            });
            t.Add(new MonsterDef
            {
                Id = 3, Name = "林间山贼", Level = 5, HpMax = 210f, Armor = 16f,
                WeaponDps = 11f, WeaponSpeed = 2.0f, E_AP = 10f,
                AggroRadius = 10f, AttackRange = 3f, AttackInterval = 2.2f, AttackK = 0.65f, MoveSpeed = 2.9f
            });

            // —— 东荒野外 14 族群（M-DH-01~14，即 14 个"物种"）+ 每物种 4 亚种 ——
            // 每物种 4 个可刷类型，id = 101 + 物种序号*4 + 亚种序号(0~3)：
            //   0 喽啰(标准) / 1 精英 / 2 稀有 / 3 头目(小BOSS)
            // 每个区域取 6 个不同物种，每物种 4 亚种（用户指定：6 物种 × 4 亚种）。
            var fam = new (int fi, string name, int lvl, float hp, float ar, float w, float ws, float ap, float ag, float iv, float ak, float mv)[]
            {
                (0,"山贼",5,210f,16f,11f,2.0f,10f,10f,2.2f,0.65f,2.9f),
                (1,"尸傀",8,330f,22f,15f,2.0f,14f,9f,2.2f,0.65f,2.6f),
                (2,"灵兽",3,120f,10f,8f,1.9f,6f,9f,2.0f,0.55f,3.4f),
                (3,"灰狼",2,90f,8f,7f,1.8f,4f,9f,2.0f,0.55f,3.2f),
                (4,"山猪",6,250f,18f,12f,2.0f,11f,10f,2.2f,0.65f,2.5f),
                (5,"纸人",10,420f,26f,18f,2.1f,17f,10f,2.2f,0.70f,2.4f),
                (6,"荒兵",12,520f,30f,21f,2.1f,20f,10f,2.3f,0.70f,2.6f),
                (7,"山魈",14,640f,34f,24f,2.2f,24f,11f,2.3f,0.72f,2.8f),
                (8,"蛛妖",16,780f,38f,27f,2.2f,27f,10f,2.4f,0.72f,2.5f),
                (9,"游方道人",18,940f,42f,30f,2.3f,31f,11f,2.4f,0.75f,2.7f),
                (10,"石敢当",20,1120f,46f,33f,2.3f,35f,10f,2.5f,0.75f,2.2f),
                (11,"水猴",22,1320f,50f,36f,2.4f,39f,11f,2.5f,0.78f,3.0f),
                (12,"白骨",25,1620f,56f,40f,2.4f,45f,11f,2.5f,0.80f,2.6f),
                (13,"狐火",28,1950f,62f,44f,2.5f,51f,12f,2.6f,0.82f,3.1f),
            };
            string[] vname = { "", "精英", "稀有", "头目" };
            foreach (var f in fam)
            {
                int b = 101 + f.fi * 4;
                t.Add(Mk(b + 0, f.name, f.lvl, f.hp, f.ar, f.w, f.ws, f.ap, f.ag, f.iv, f.ak, f.mv));                                  // 喽啰（标准）
                t.Add(MkElite(b + 1, f.name + vname[1], f.lvl + 1, f.hp * 2.0f, f.ar * 1.4f, f.w * 1.25f, f.ws, f.ap * 1.25f, f.ag, f.iv, f.ak, f.mv));
                t.Add(MkElite(b + 2, f.name + vname[2], f.lvl + 2, f.hp * 3.2f, f.ar * 1.6f, f.w * 1.4f, f.ws, f.ap * 1.4f, f.ag + 1f, f.iv, f.ak, f.mv));
                t.Add(Boss(b + 3, f.name + vname[3], f.lvl + 3, f.hp * 6.0f, f.ar * 2.2f, f.w * 1.7f, f.ws, f.ap * 1.7f, f.ag + 2f));  // 头目（小BOSS）
            }

            // —— 东荒野外独立稀有 X-DH-01/02（独立于 14 族群 ×4 亚种体系，归 老林·荒村）——
            // 数据取自《怪物种类全集》§5.3：山鬼 = 3.2× 藤蔓巨影 · SK-02×2.0；荒村傩面 = 人形傩面纸幡 · SK-03。
            // 品质「稀有」→ 数据层按 IsElite 处理（无独立 IsRare 标记）。等级对齐老林 22–30 带。
            t.Add(MkElite(161, "山鬼", 28, 6000f, 62f, 46f, 2.4f, 55f, 12f, 2.6f, 0.85f, 2.8f));
            t.Add(MkElite(162, "荒村傩面", 26, 5200f, 58f, 42f, 2.3f, 50f, 11f, 2.5f, 0.82f, 2.6f));

            // —— 东荒守城 BOSS：云津渡 CD-1~3（名称严格照抄《怪物全集-v1》）——
            t.Add(Boss(201, "镇岳·山君", 30, 8000f, 80f, 50f, 2.0f, 80f, 14f));
            t.Add(Boss(202, "镇岳·符伯", 32, 9000f, 85f, 55f, 2.0f, 90f, 14f));
            t.Add(Boss(203, "镇岳·灵狩", 34, 10000f, 90f, 60f, 2.0f, 100f, 14f));

            // —— 东荒世界 BOSS：雷泽·夔牛（WB-DH-01，世界 BOSS，供世界表《雷泽》区域引用）——
            t.Add(Boss(301, "雷泽·夔牛", 56, 60000f, 120f, 80f, 2.0f, 150f, 16f));

            // —— 副本/团本 BOSS（#53 · Phase A 数据层，全部 IsBoss = true）——
            // 数值由 RaidBoss(id, name, level, tier) 公式统一生成，名称逐字照抄《副本团本实现计划-v1》附录。

            // 工厂池 B01–B22 → id 401–422（22 条：Level = 15 + i*2，tier = 9）
            var factoryBoss = new[]
            {
                "石髓巨蝰", "蚀骨巫祝", "铁牙野猪王", "拾骸者·阿鸠", "云篆道人", "铜甲尸将",
                "陶纹祭司", "霜牙狼首", "双生石像·守与望", "归墟爬行者", "熔渣锻奴", "蜃楼妖妃",
                "银弓猎手·卡尔", "沉舰亡魂", "玄龟负碑", "卢恩石卫", "蚀心魔藤", "双子狼·哈提与斯库尔",
                "守渊将·无名", "星陨使徒", "碑灵·万骨", "归墟之喉"
            };
            for (int i = 0; i < factoryBoss.Length; i++)
                t.Add(RaidBoss(401 + i, factoryBoss[i], 15 + i * 2, 9f));

            // 固定 5 人本 D1–D29 → id 501–529（29 条：Level = 60，tier = 13）
            var fixedBoss = new[]
            {
                "裂隙颚兽", "初潮守门者", "归墟低语者",                 // D60-1
                "断剑残魂", "剑冢守灵", "冢主·无名剑魄",                // D60-2
                "竞技场冠军", "桂冠裁判官", "无冠者",                    // D60-3
                "摆渡人·卡戎", "溺亡者合唱", "冥河哀歌", "无渡之魂",      // D60-4
                "炉火守卫", "熔渣喷发口", "锻炉之主",                    // D60-5
                "腐根守卫", "尼德霍格幼嗣", "根心共鸣体", "腐根母株",    // D60-6
                "封印执行官", "地宫回响", "石卫队长", "封印物·未名",      // D60-7
                "残骸守卫·东荒", "残骸守卫·奥林匹斯", "残骸守卫·阿斯加德", "归墟低语合唱", "外缘守望者" // D60-8
            };
            for (int i = 0; i < fixedBoss.Length; i++)
                t.Add(RaidBoss(501 + i, fixedBoss[i], 60, 13f));

            // 10 人团本 R10-1(5) + R10-2(5) → id 601–610（10 条：Level = 60，tier = 26）
            var raid10Boss = new[]
            {
                "内环哨卫·三域残骸", "归墟回流者", "三域回响", "内环吞噬者", "【终】内环之瞳",
                "枢纽守卫长", "三域封印柱", "回响·地宫", "封印执行官·再临", "【终】封印核心·未名"
            };
            for (int i = 0; i < raid10Boss.Length; i++)
                t.Add(RaidBoss(601 + i, raid10Boss[i], 60, 26f));

            // 20 人团本 R20-1 → id 611–616（6 条：Level = 60，tier = 75）
            var raid20Boss = new[]
            {
                "终焉先驱（双体）", "归墟潮汐", "三域残骸·合体", "三使徒（星陨/卢恩/云篆）", "终焉守门者·无名", "【终】归墟之眼"
            };
            for (int i = 0; i < raid20Boss.Length; i++)
                t.Add(RaidBoss(611 + i, raid20Boss[i], 60, 75f));

            return t;
        }

        // 东荒野外小怪构造器（默认参数同原有 3 只口径；LeashRadius 沿用 22m 默认）
        private static MonsterDef Mk(int id, string name, int level,
            float hp, float armor, float w, float ws, float ap,
            float aggro, float interval, float atkK, float move)
        {
            return new MonsterDef
            {
                Id = id, Name = name, Level = level,
                HpMax = hp, Armor = armor, WeaponDps = w, WeaponSpeed = ws, E_AP = ap,
                AggroRadius = aggro, AttackRange = 3f, AttackInterval = interval, AttackK = atkK, MoveSpeed = move
            };
        }

        // 精英 / 稀有构造器（同 Mk 口径，但 IsElite = true）
        private static MonsterDef MkElite(int id, string name, int level,
            float hp, float armor, float w, float ws, float ap,
            float aggro, float interval, float atkK, float move)
        {
            var d = Mk(id, name, level, hp, armor, w, ws, ap, aggro, interval, atkK, move);
            d.IsElite = true;
            return d;
        }

        // 守城 BOSS 构造器（IsBoss = true）
        private static MonsterDef Boss(int id, string name, int level,
            float hp, float armor, float w, float ws, float ap, float aggro)
        {
            return new MonsterDef
            {
                Id = id, Name = name, Level = level, IsBoss = true,
                HpMax = hp, Armor = armor, WeaponDps = w, WeaponSpeed = ws, E_AP = ap,
                AggroRadius = aggro, AttackRange = 4f, AttackInterval = 2.6f, AttackK = 1.0f, MoveSpeed = 2.2f,
                RespawnSeconds = 60
            };
        }

        // 副本/团本 BOSS 构造器（#53 · 按等级 + 档位系数 tier 公式统一生成，IsBoss = true）
        private static MonsterDef RaidBoss(int id, string name, int level, float tier)
        {
            double baseHp = 90.0 * System.Math.Pow(level / 2.0, 1.2);
            float hp = (float)System.Math.Round(baseHp * tier);
            float ar = (float)System.Math.Round(hp / 40.0);
            float w  = (float)System.Math.Round(8.0 + level * 0.9);
            float ap = (float)System.Math.Round(4.0 + level * 0.9);
            return new MonsterDef
            {
                Id = id, Name = name, Level = level, IsBoss = true,
                HpMax = hp, Armor = ar, WeaponDps = w, WeaponSpeed = 2.2f, E_AP = ap,
                AggroRadius = 14f, AttackRange = 4f, AttackInterval = 2.6f, AttackK = 1.0f,
                MoveSpeed = 2.2f, RespawnSeconds = 60
            };
        }

        private void Add(MonsterDef def)
        {
            _defs[def.Id] = def;
        }

        public MonsterDef Get(int id)
        {
            MonsterDef d;
            return _defs.TryGetValue(id, out d) ? d : null;
        }

        public IEnumerable<MonsterDef> All()
        {
            return _defs.Values;
        }
    }
}
