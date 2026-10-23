// 《浮生渡》领域层 · 归墟侵蚀层词缀定义表（纯 C#，不引用 UnityEngine / UnityEditor）
// 14 个「神力侵蚀」词缀 id 701–714，名称 / 机制取自副本 GDD §2.2.4；
// 互斥组取自团本 GDD §2.5：AF-06×AF-11(组1) / AF-03×AF-09(组2) / AF-08×AF-02(组3) / AF-07×AF-12(组4)。
// 抽取与重掷逻辑在 Gameplay/AffixRuntime 实现；本文件只持有数据。
using System.Collections.Generic;

namespace Fushengdu.Core
{
    /// <summary>单个词缀定义。</summary>
    public sealed class AffixDef
    {
        public int Id;
        public string Name = "";
        public string Description = "";
        public int UnlockTier;   // 解锁档位（副本 GDD：T1:6 → T4:10 → T9:14）
        public int MutexGroup;   // 互斥组（0 = 无互斥）
    }

    /// <summary>词缀表（id 701–714）。</summary>
    public sealed class AffixTable
    {
        public static readonly AffixTable Default = new AffixTable();

        private readonly Dictionary<int, AffixDef> _byId = new Dictionary<int, AffixDef>();

        public AffixTable()
        {
            Build();
        }

        public AffixDef Get(int id)
        {
            AffixDef d;
            return _byId.TryGetValue(id, out d) ? d : null;
        }

        public IEnumerable<AffixDef> All()
        {
            return _byId.Values;
        }

        private void Add(int id, string name, string desc, int tier, int mutex)
        {
            _byId[id] = new AffixDef { Id = id, Name = name, Description = desc, UnlockTier = tier, MutexGroup = mutex };
        }

        private void Build()
        {
            // 互斥组编码：0=无，1=(706,711)，2=(703,709)，3=(708,702)，4=(707,712)
            Add(701, "卢恩狂暴", "怪物每 20s 周期性狂暴 8s（伤害 +40%）", 1, 0);
            Add(702, "东荒雷罚", "每 6s 随机 1 名玩家位置落雷（1.5s 预警圈）", 1, 3);  // 组3 × 708
            Add(703, "归墟侵蚀", "地面随机生成腐蚀区（站立每秒掉 1.5% 最大生命）", 1, 2);  // 组2 × 709
            Add(704, "石肤",     "怪物获得护盾（= 20% 最大生命）", 1, 0);
            Add(705, "血月",     "小怪死亡后，8m 内怪物狂暴 6s", 1, 0);
            Add(706, "静默",     "每 25s 全场沉默 2s（禁用技能，可用平砍与位移）", 1, 1);  // 组1 × 711
            Add(707, "镜像",     "终 BOSS 血量 50% 时分裂 1 个镜像（50% 属性，30s 后自毁）", 4, 4);  // 组4 × 712
            Add(708, "引力异常", "每 15s 全场向中心拉扯 2s（位移技能可抵抗）", 4, 3);  // 组3 × 702
            Add(709, "蚀魂",     "玩家死亡后原地残留腐蚀区 10s", 4, 2);  // 组2 × 703
            Add(710, "增殖",     "小怪死亡分裂为 2 个 50% 属性小怪（最多分裂 1 次）", 4, 0);
            Add(711, "时滞",     "BOSS 技能预警时间 -40%（反应窗口缩短）", 9, 1);  // 组1 × 706
            Add(712, "献祭",     "需先击杀 3 个祭坛守卫，否则终 BOSS 减伤 60%", 9, 4);  // 组4 × 707
            Add(713, "光暗交替", "每 20s 切换明暗；暗期视野 -40%、玩家伤害 +25%", 9, 0);
            Add(714, "星序紊乱", "玩家技能 CD 随机 ±20%（每次施放时掷骰）", 9, 0);
        }
    }
}
