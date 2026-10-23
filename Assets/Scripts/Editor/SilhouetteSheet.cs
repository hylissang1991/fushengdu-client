// 《浮生渡》编辑器工具 · 同骨架剪影比对（撞脸实测）
//
// 【为什么要有它】
//   lore-forms 在形态图鉴 §8.1 用四维矩阵（头 / 手持 / 悬浮物 / 材质）自检了 SK-05 家族的
//   区分度，但那是**纸面推演**。88 条真资产目前一条都还没有，等建模完才发现撞脸，
//   返工的是 22 只同骨架 BOSS。本工具在建模开工前用**程序化占位几何**把推演变实测。
//
// 【它测什么】
//   ① 彩色并排图 —— 肉眼看"像不像"
//   ② 纯剪影图（黑底白/白底黑二值）
//   ③ 灰度图 —— 图鉴 §9.4 的灰度验收法（去掉颜色后还认不认得出）
//   ④ **等高归一化剪影** —— 把所有体型拉到同框高度，强制只比"形状"，
//      屏蔽"体型"这一维。这是诊断关键：raw 版差异率会被体型主导，
//      看不出"如果体型一样还分不分得开"。
//
// 【度量口径】
//   每只渲一张固定视角小图 → 二值化成剪影掩码 → 两两算 IoU，差异率 = 1 - IoU。
//   差异率越小越像。阈值（工具默认，可调）：
//     <8%  高风险（撞脸）  /  8%~15% 需关注  /  >15% 通过
//   同时输出"每只的最近邻"，定位是哪一只在撞，而不是只给一个全局列表。
//
// 【配置表字段（按 team-lead 2026-10-22 裁定）】
//   MonsterId / SkeletonId / SizeMultiplier / 挂件列表（头·手持·悬浮）/ 材质参数组
//   挂点命名沿用 AT_Head / AT_ItemR / AT_ItemL / AT_Back / AT_Float / AT_Ground。
//   体型倍数写配置表、不烘进 prefab 缩放 —— 同一骨架不同 BOSS 体型差 1.3×~3.6×。
//
// 【诚实声明】
//   占位几何只保证"该有的维度差异存在"（头形/手持/悬浮物/体型/材质），
//   **不等于最终建模**。它的结论是"这个区分维度在剪影层面够不够用"，
//   不是"这只 BOSS 长得好不好"。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.SilhouetteSheet.Run
//             -fdSkeleton SK-05 -fdOut D:/AI_Workspace/浮生渡/shots/silhouette
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Fushengdu.Gameplay;
// System.Object 与 UnityEngine.Object 撞名（CS0104），显式指定
using Object = UnityEngine.Object;

namespace Fushengdu.Editor
{
    public static class SilhouetteSheet
    {
        // ---------------------------------------------------------------- 配置表

        internal enum HeadKind
        {
            CuMian,          // 铜面方盒（横隙）
            GreekHead,       // 希腊头
            WuTouShiTai,     // 无头（石台）
            DoorFace,        // 门上浮人面（身体是一扇门）
            ShiBanTou,       // 无面石板头
            TieJiangKui,     // 铁匠头盔
            WuTouDuanZhi,    // 无头（头顶 5 断枝）
            SanSeShiMian,    // 三色裂开石面
            FangYinTou,      // 方印头
            ShenXiangTou,    // 神像头（空眼窝）
            WuTouDuanBei,    // 无头（头顶断裂石碑）
            IceBlock,        // 一整块冰（无五官）
            LuEnFangBei,     // 卢恩方碑头
            TouKuiHengFeng,  // 无面头盔（横缝）
            YuanTong,        // 圆筒（横缝）
            BanJieDengLong,  // 半截灯笼
            SanTou           // 三个域特色头（合体）
        }

        internal enum HoldKind
        {
            None,
            QiJia,           // 旗架
            FangDunDuanJian, // 方盾 + 短剑
            JuShouZheE,      // 举手遮额
            ChuiXing,        // 锤形双手
            JuRen,           // 巨刃（洋红裂）
            MingBan,         // 铭板
            MingBanFengYinZhu, // 铭板 + 断裂封印柱（R9 撞脸修复专用，见下方注释）
            TaDunKuoJian,    // 塔盾（城垛缺口）+ 阔剑
            DuanJianDuanMao, // 断剑 + 断矛
            DuanZhu,         // 断柱
            DuanZhuX2,       // 断柱 ×2
            ShouShouMuLiang, // 兽首木梁（兽角）
            BingChui,        // 冰锤
            LuEnShiBanBi,    // 卢恩石板臂
            ZhiChuai,        // 枝杈臂
            ShuangQuan,      // 双拳
            ChangRen,        // 长刃
            SiDuanWuQi,      // 4 段变形武器
            SanTouWuQi       // 断剑 / 断矛 / 木梁（三头合体）
        }

        internal enum FloatKind
        {
            None,
            FuShi3, FuShi6, FuShi9,   // 符石 3 / 6 / 9 块
            TouDing3Jian,             // 头顶 3 柄小剑
            TouDingHuanHen,           // 头顶环形冠痕
            TouDingBanHuan,           // 头顶半个环（终焉先驱 ×2 各半）
            BeiHou3Pian,              // 背后 3 片符文屏
            BeiHou6Pian               // 背后 6 片符文屏
        }

        // ---------------------------------------------------------------- 挂点命名契约
        //
        // 【三层契约 · team-lead 2026-10-22 终裁，形态图鉴 §2.4.0】
        //   AT_    = 挂件资产（有 mesh）        —— 例 AT_TowerShield、AT_CrystalCluster_M
        //   BP_    = 骨架上的挂载点（空节点/骨骼）—— 各骨架自有名，跨骨架不统一
        //   SLOT_  = 配置表里的逻辑槽，运行时解析到 BP_
        //
        // **配置表的「挂件列表」字段一律写 SLOT_，不写 BP_**：
        //   一张配置表要套 8 套骨架，而 8 套骨架的物理挂点名各不相同。
        //   写 SLOT_ → 一张表通用，四足兽（无手）配到 SLOT_ItemR 静默留空即可；
        //   写 BP_   → 得为每套骨架各写一张表，8 张表互相漂移。
        //
        // 解析规则（照抄 §2.4.0）：**默认直通同后缀**（SLOT_X → BP_X），
        // 下表「例外/回退」列优先于直通；骨架没有对应 BP_ 时**静默留空，不报错**。
        //   SLOT_Head        → BP_Head    （SK-05 → BP_HeadAlt；SK-03/05/07 头顶装饰 → BP_Crown）
        //   SLOT_ItemR       → BP_HandR   （SK-01/02/04/06/08 无手 → 留空）
        //   SLOT_ItemL       → BP_HandL   （同上 → 留空）
        //   SLOT_Back        → BP_Back    （SK-04/06/08 无 BP_Back → 留空；**SK-07 预期留空**：
        //                                  漂浮法体无背饰语义，不补挂点，背饰需求改走 SLOT_Float）
        //   SLOT_Float       → BP_Aura    （SK-03/05/07 有；SK-05 的 BP_Aura=(0,2.05,0) 为本轮补挂点；
        //                                  SK-01/02/04/06/08 → 留空）
        //   SLOT_GroundDecal → BP_Root    （SK-04/SK-07 → BP_GroundDecal：SK-04 的 (0,0.02,-0.10)
        //                                  为本轮补挂点，体下中心，**不得落 BP_Root 尾端 (0,0,-1.40)**）
        // 覆盖矩阵见 §2.4.0：SK-03/SK-05 6/6，SK-07 5/6，SK-04 3/6，SK-01/02 3/6，SK-06/08 1/6。
        // 六个槽是终裁集合，**不得新增第七个**；其余挂点（BP_Orbit/BP_Crystal/BP_Slab 等）
        // 是骨架原生挂点，见 §2.4.1，配置表不得写。
        // 注：B16/D23 的「周身 6 块符石绕体一圈」是剪影主特征，走原生 BP_Orbit_1..9(y=1.15,r=1.10)，
        //     不进 SLOT_Float，配置表不写 —— 挪到头顶会毁掉剪影与 R3 读法（见 §2.4.0 台账 #4）。
        private static class Slot
        {
            public const string Head = "SLOT_Head";
            public const string ItemR = "SLOT_ItemR";
            public const string ItemL = "SLOT_ItemL";
            public const string Back = "SLOT_Back";
            public const string Float = "SLOT_Float";
            public const string GroundDecal = "SLOT_GroundDecal";
        }

        internal sealed class Recipe
        {
            public string Id;            // MonsterId
            public string Name;
            public string Skeleton;      // SkeletonId
            public float Size;           // SizeMultiplier

            // 以下三项在真资产阶段就是「挂件列表」字段：(挂件ID, 逻辑槽) 对。
            // 现在是程序化占位几何，所以先以形态维度枚举代替 —— 但**槽位归属按 SLOT_ 契约**：
            //   Head  → SLOT_Head / Hold → SLOT_ItemR（双手持时为 ItemR+ItemL）
            //   Float → SLOT_Float（背后符文屏属 SLOT_Back）
            public HeadKind Head;
            public HoldKind Hold;
            public FloatKind Float;
            public Color Body;           // 材质参数组：主色
            public Color Accent;         // 材质参数组：点缀色
            public bool FourLegs;        // 4 腿（锻炉之主）
            public bool ChestGlow;       // 胸口炉膛 / 树洞
            public bool RightShoulderUp; // 右肩飞檐翘角
            public bool BackColumn;      // 背后半截廊柱
            public bool TriBand;         // 躯干三材质分区
            public bool IceShell;        // 半透冰壳
        }

        /// <summary>SK-05 家族配置 —— 逐条抄自形态图鉴 §8.1 矩阵（该表 25 行）。internal 供侵蚀验证工具复用。</summary>
        internal static Recipe[] Sk05()
        {
            return new[]
            {
                new Recipe { Id="B06", Name="铜甲尸将", Size=1.6f, Head=HeadKind.CuMian, Hold=HoldKind.QiJia,
                    Body=Hex("#5E7A5A"), Accent=Hex("#C9A05C") },
                new Recipe { Id="B09a", Name="守（石像）", Size=2.0f, Head=HeadKind.GreekHead, Hold=HoldKind.FangDunDuanJian,
                    Body=Hex("#E9E5DB"), Accent=Hex("#A8A69F") },
                new Recipe { Id="B09b", Name="望（石像）", Size=2.0f, Head=HeadKind.GreekHead, Hold=HoldKind.JuShouZheE,
                    Body=Hex("#E9E5DB"), Accent=Hex("#A8A69F") },
                new Recipe { Id="B11", Name="熔渣锻奴", Size=1.8f, Head=HeadKind.YuanTong, Hold=HoldKind.ChuiXing,
                    Body=Hex("#55606B"), Accent=Hex("#FF7A33"), ChestGlow=true },
                new Recipe { Id="B16", Name="卢恩石卫", Size=2.1f, Head=HeadKind.WuTouShiTai, Hold=HoldKind.None, Float=FloatKind.FuShi6,
                    Body=Hex("#55606B"), Accent=Hex("#7FD6C8") },
                new Recipe { Id="B19", Name="守渊将·无名", Size=2.3f, Head=HeadKind.TouKuiHengFeng, Hold=HoldKind.JuRen,
                    Body=Hex("#1A1A20"), Accent=Hex("#C0407A") },
                new Recipe { Id="D2", Name="初潮守门者", Size=2.4f, Head=HeadKind.DoorFace, Hold=HoldKind.None,
                    Body=Hex("#6E403C"), Accent=Hex("#947F5A") },
                new Recipe { Id="D6", Name="冢主·无名剑魄", Size=2.0f, Head=HeadKind.GreekHead, Hold=HoldKind.None, Float=FloatKind.TouDing3Jian,
                    Body=Hex("#22222A"), Accent=Hex("#C9A05C") },
                new Recipe { Id="D9", Name="无冠者", Size=2.4f, Head=HeadKind.ShiBanTou, Hold=HoldKind.ShuangQuan, Float=FloatKind.TouDingHuanHen,
                    Body=Hex("#6B6560"), Accent=Hex("#4A4640") },
                new Recipe { Id="D16", Name="锻炉之主", Size=3.0f, Head=HeadKind.TieJiangKui, Hold=HoldKind.ChuiXing,
                    Body=Hex("#55606B"), Accent=Hex("#FF7A33"), FourLegs=true, ChestGlow=true },
                new Recipe { Id="D17", Name="腐根守卫", Size=2.3f, Head=HeadKind.WuTouDuanZhi, Hold=HoldKind.ZhiChuai,
                    Body=Hex("#574E44"), Accent=Hex("#3A322B"), ChestGlow=true },
                new Recipe { Id="D21", Name="封印执行官", Size=2.2f, Head=HeadKind.GreekHead, Hold=HoldKind.MingBan, Float=FloatKind.BeiHou3Pian,
                    Body=Hex("#6B4E7A"), Accent=Hex("#C9A05C") },
                new Recipe { Id="D23", Name="石卫队长", Size=2.5f, Head=HeadKind.FangYinTou, Hold=HoldKind.ChuiXing, Float=FloatKind.FuShi6,
                    Body=Hex("#6B4E7A"), Accent=Hex("#C9A05C") },
                new Recipe { Id="D25", Name="残骸守卫·东荒", Size=2.4f, Head=HeadKind.BanJieDengLong, Hold=HoldKind.DuanZhu,
                    Body=Hex("#8A6A55"), Accent=Hex("#C9A05C"), RightShoulderUp=true },
                new Recipe { Id="D26", Name="残骸守卫·奥林匹斯", Size=2.4f, Head=HeadKind.ShenXiangTou, Hold=HoldKind.DuanZhuX2,
                    Body=Hex("#E9E5DB"), Accent=Hex("#A8A69F"), BackColumn=true },
                new Recipe { Id="D27", Name="残骸守卫·阿斯加德", Size=2.4f, Head=HeadKind.WuTouDuanBei, Hold=HoldKind.ShouShouMuLiang,
                    Body=Hex("#6B5B4A"), Accent=Hex("#4A3F33") },
                new Recipe { Id="R1", Name="内环哨卫", Size=2.8f, Head=HeadKind.SanSeShiMian, Hold=HoldKind.DuanJianDuanMao,
                    Body=Hex("#55606B"), Accent=Hex("#C0407A"), TriBand=true },
                new Recipe { Id="R6", Name="枢纽守卫长", Size=2.6f, Head=HeadKind.GreekHead, Hold=HoldKind.TaDunKuoJian, Float=FloatKind.BeiHou3Pian,
                    Body=Hex("#6B4E7A"), Accent=Hex("#C9A05C") },
                // R9 撞脸修复（team-lead 终裁）：2.4× → 2.8×，手持追加断裂封印柱
                new Recipe { Id="R9", Name="封印执行官·再临", Size=2.8f, Head=HeadKind.GreekHead, Hold=HoldKind.MingBanFengYinZhu, Float=FloatKind.BeiHou6Pian,
                    Body=Hex("#6B4E7A"), Accent=Hex("#C0407A") },
                new Recipe { Id="R11", Name="终焉先驱（×2 镜像）", Size=2.6f, Head=HeadKind.GreekHead, Hold=HoldKind.ChangRen, Float=FloatKind.TouDingBanHuan,
                    Body=Hex("#1A1A20"), Accent=Hex("#C0407A") },
                new Recipe { Id="R13", Name="三域残骸·合体", Size=3.6f, Head=HeadKind.SanTou, Hold=HoldKind.SanTouWuQi,
                    Body=Hex("#55606B"), Accent=Hex("#C0407A") },
                new Recipe { Id="R15", Name="终焉守门者", Size=3.0f, Head=HeadKind.TouKuiHengFeng, Hold=HoldKind.SiDuanWuQi,
                    Body=Hex("#1A1A20"), Accent=Hex("#C0407A") },
                // 【建模约束 · team-lead 采纳】D2 初潮守门者 × CD-8 冰霜巨兵实测差异率 19.3%，
                // 全场第二低 —— 门形与冰壳都是"大块矩形"，目前只靠明度差分。
                // 这两只最终建模**禁止再往矩形大块方向收敛**，否则会变成第二对撞脸。
                new Recipe { Id="CD-8", Name="守门·冰霜巨兵", Size=2.8f, Head=HeadKind.IceBlock, Hold=HoldKind.BingChui,
                    Body=Hex("#7FB3D9"), Accent=Hex("#C9E4F2"), IceShell=true },
                new Recipe { Id="CD-9", Name="守门·卢恩石像", Size=2.6f, Head=HeadKind.LuEnFangBei, Hold=HoldKind.LuEnShiBanBi, Float=FloatKind.FuShi9,
                    Body=Hex("#55606B"), Accent=Hex("#7FD6C8") },
                // 【数据口径 · team-lead 终裁】图鉴 §8.1 该行"体型 1.3×"与括号"0.6× 尺寸"不一致，
                // 以 **1.3×** 为准，0.6× 那条作笔误删除。1.3× 是更保守的情形（与 B16 的 2.1× 更接近），
                // 实测仍通过（最近邻 D23，差异率 24.7%）。
                new Recipe { Id="M-12", Name="祭坛守卫", Size=1.3f, Head=HeadKind.WuTouShiTai, Hold=HoldKind.None, Float=FloatKind.FuShi3,
                    Body=Hex("#55606B"), Accent=Hex("#7FD6C8") },
            };
        }

        // ---------------------------------------------------------------- 主流程

        private const int CellW = 200, CellH = 280, Cols = 6;
        private const float RiskHigh = 0.08f;   // <8% 差异率 = 撞脸高风险
        private const float RiskWatch = 0.15f;  // 8%~15% 需关注

        // raw 版取景固定：覆盖家族里最高的那只（3.6× ≈ 6.5m），体型差异因此可见 ——
        // 体型本来就是区分维度之一，不该被"各自适配画面"抹掉。
        private const float RawSize = 3.6f;
        private static readonly Vector3 RawCenter = new Vector3(0f, 3.2f, 0f);

        private static string _out = "D:/AI_Workspace/浮生渡/shots/silhouette";
        private static string _skeleton = "SK-05";

        [MenuItem("浮生渡/23. 同骨架剪影比对", priority = 230)]
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-fdOut") _out = args[i + 1];
                if (args[i] == "-fdSkeleton") _skeleton = args[i + 1];
            }
            Directory.CreateDirectory(_out);

            // 空场景 + 一盏方向光（只影响彩色图；剪影/灰度用 unlit，不受光照影响）
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.Euler(32f, -28f, 0f);

            Recipe[] recipes = _skeleton == "SK-05" ? Sk05() : Sk05();
            Debug.Log("[Silhouette] 骨架=" + _skeleton + " 条目=" + recipes.Length);

            var colorTex = new List<Texture2D>();
            var silTex = new List<Texture2D>();
            var grayTex = new List<Texture2D>();
            var normTex = new List<Texture2D>();
            var masks = new List<bool[]>();
            var normMasks = new List<bool[]>();
            var report = new StringBuilder();

            // 正交相机：固定正面视角，保证"同角度同口径"才是公平比较
            for (int i = 0; i < recipes.Length; i++)
            {
                recipes[i].Skeleton = _skeleton;
                GameObject go = Build(recipes[i]);
                Bounds b = BoundsOf(go);
                float h = b.size.y;

                colorTex.Add(Render(go, Mode.Color, RawSize, new Color(0.16f, 0.18f, 0.22f)));
                silTex.Add(Render(go, Mode.Silhouette, RawSize, Color.white));
                grayTex.Add(Render(go, Mode.Gray, RawSize, new Color(0.5f, 0.5f, 0.5f)));
                // 等高归一化：每只单独适配画面 → 屏蔽体型维度，只比形状
                normTex.Add(Render(go, Mode.Silhouette, h * 0.58f, Color.white, b.center));

                masks.Add(Mask(silTex[i]));
                normMasks.Add(Mask(normTex[i]));

                report.AppendLine(string.Format("{0,-6} {1,-22} 体型×{2:F1} 高度={3:F2}m",
                    recipes[i].Id, recipes[i].Name, recipes[i].Size, h));
                Object.DestroyImmediate(go);
            }

            // ---------- 拼图 ----------
            Write(Compose(colorTex), "sheet_color.png");
            Write(Compose(silTex), "sheet_silhouette.png");
            Write(Compose(grayTex), "sheet_gray.png");
            Write(Compose(normTex), "sheet_silhouette_normalized.png");
            Debug.Log("[Silhouette] 四张并排图已输出到 " + _out);

            // ---------- 度量 ----------
            Pairs(recipes, masks, "含体型（raw）", report);
            Pairs(recipes, normMasks, "等高归一化（屏蔽体型）", report);
            Nearest(recipes, normMasks, report);

            Debug.Log("[Silhouette] 报告\n" + report);
            EditorApplication.Exit(0);
        }

        private enum Mode { Color, Silhouette, Gray }

        // ---------------------------------------------------------------- 渲染

        private static Texture2D Render(GameObject go, Mode mode, float orthoSize, Color bg, Vector3? center = null)
        {
            var camGo = new GameObject("SilCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bg;
            Vector3 c = center ?? RawCenter;
            // 角色正面朝 +Z（nose / visor 都在 +Z 侧），相机必须在 +Z 侧回头看，否则拍到背面
            camGo.transform.position = c + new Vector3(0f, 0f, 12f);
            camGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            // 换材质（渲染后必须还原，别污染 RuntimeArt 的材质缓存）
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var restore = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) restore[i] = renderers[i].sharedMaterial;

            if (mode != Mode.Color)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material src = restore[i];
                    Color c0 = src != null && src.HasProperty("_Color") ? src.color : Color.gray;
                    if (src != null && src.HasProperty("_BaseColor")) c0 = src.GetColor("_BaseColor");
                    Color use = mode == Mode.Silhouette ? Color.black : Luma(c0);
                    renderers[i].sharedMaterial = Unlit(use);
                }
            }

            var rt = new RenderTexture(CellW, CellH, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(CellW, CellH, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, CellW, CellH), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = restore[i];

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            return tex;
        }

        private static readonly Dictionary<int, Material> UnlitCache = new Dictionary<int, Material>();

        private static Material Unlit(Color c)
        {
            int key = ColorUtility.ToHtmlStringRGBA(c).GetHashCode();
            Material m;
            if (UnlitCache.TryGetValue(key, out m) && m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            m = new Material(sh) { name = "Sil_" + c };
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            UnlitCache[key] = m;
            return m;
        }

        private static Color Luma(Color c)
        {
            float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return new Color(y, y, y);
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.up, Vector3.one);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>二值化：背景白、前景黑 → 取非白像素为前景。</summary>
        private static bool[] Mask(Texture2D tex)
        {
            Color[] px = tex.GetPixels();
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].r < 0.5f;
            return m;
        }

        /// <summary>差异率 = 1 - IoU（越小越像）。</summary>
        private static float Diff(bool[] a, bool[] b)
        {
            int inter = 0, uni = 0;
            for (int i = 0; i < a.Length; i++)
            {
                bool x = a[i], y = b[i];
                if (x && y) inter++;
                if (x || y) uni++;
            }
            return uni == 0 ? 0f : 1f - inter / (float)uni;
        }

        private static void Pairs(Recipe[] r, List<bool[]> masks, string title, StringBuilder sb)
        {
            var list = new List<KeyValuePair<float, string>>();
            for (int i = 0; i < masks.Count; i++)
                for (int j = i + 1; j < masks.Count; j++)
                    list.Add(new KeyValuePair<float, string>(Diff(masks[i], masks[j]),
                        r[i].Id + " " + r[i].Name + "  ×  " + r[j].Id + " " + r[j].Name));
            list.Sort((x, y) => x.Key.CompareTo(y.Key));

            sb.AppendLine();
            sb.AppendLine("========== 最混淆的 10 对 · " + title + " ==========");
            sb.AppendLine("差异率 | 判定   | 组合");
            for (int k = 0; k < Mathf.Min(10, list.Count); k++)
                sb.AppendLine(string.Format("{0,6:P1} | {1} | {2}",
                    list[k].Key, Verdict(list[k].Key), list[k].Value));
        }

        private static void Nearest(Recipe[] r, List<bool[]> masks, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("========== 每只的最近邻（等高归一化，定位到底是谁在撞）==========");
            sb.AppendLine("编号 | 最近邻 | 差异率 | 判定");
            int high = 0, watch = 0;
            for (int i = 0; i < masks.Count; i++)
            {
                float best = 1f; int bi = -1;
                for (int j = 0; j < masks.Count; j++)
                {
                    if (j == i) continue;
                    float d = Diff(masks[i], masks[j]);
                    if (d < best) { best = d; bi = j; }
                }
                if (best < RiskHigh) high++;
                else if (best < RiskWatch) watch++;
                sb.AppendLine(string.Format("{0,-6} | {1,-22} | {2,6:P1} | {3}",
                    r[i].Id, bi >= 0 ? r[bi].Id + " " + r[bi].Name : "-", best, Verdict(best)));
            }
            sb.AppendLine();
            sb.AppendLine(string.Format("汇总：高风险（<{0:P0}）{1} 只 / 需关注（{2:P0}~{3:P0}）{4} 只 / 通过 {5} 只",
                RiskHigh, high, RiskHigh, RiskWatch, watch, masks.Count - high - watch));
        }

        private static string Verdict(float d)
        {
            if (d < RiskHigh) return "✗ 撞脸高风险";
            if (d < RiskWatch) return "△ 需关注";
            return "✓ 通过";
        }

        // ---------------------------------------------------------------- 拼图

        private static Texture2D Compose(List<Texture2D> cells)
        {
            int rows = Mathf.CeilToInt(cells.Count / (float)Cols);
            var big = new Texture2D(Cols * CellW, rows * CellH, TextureFormat.RGB24, false);
            Color blank = new Color(0.10f, 0.10f, 0.12f);
            for (int y = 0; y < big.height; y++)
                for (int x = 0; x < big.width; x++) big.SetPixel(x, y, blank);
            for (int i = 0; i < cells.Count; i++)
            {
                int cx = (i % Cols) * CellW;
                int cy = (rows - 1 - i / Cols) * CellH;   // 纹理 Y 自下而上
                big.SetPixels(cx, cy, CellW, CellH, cells[i].GetPixels());
            }
            big.Apply();
            return big;
        }

        private static void Write(Texture2D tex, string name)
        {
            File.WriteAllBytes(Path.Combine(_out, name), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ---------------------------------------------------------------- 占位拼装（SK-05 宽厚人形）

        internal static GameObject Build(Recipe r)
        {
            var root = new GameObject(r.Id + "_" + r.Name);
            Transform t = root.transform;
            float s = r.Size;

            Material body = RuntimeArt.Mat(r.Body);
            Material dark = RuntimeArt.Mat(r.Body * 0.72f);
            Material accent = RuntimeArt.Mat(r.Accent, 0.35f);

            // ---------- 腿（D16 四腿）
            float legY = 0.36f * s, legH = 0.72f * s;
            float[] legX = r.FourLegs ? new[] { -0.22f, 0.22f, -0.22f, 0.22f } : new[] { -0.17f, 0.17f };
            float[] legZ = r.FourLegs ? new[] { 0.16f, 0.16f, -0.16f, -0.16f } : new[] { 0f, 0f };
            for (int i = 0; i < legX.Length; i++)
                RuntimeArt.Box(t, "Leg" + i, new Vector3(legX[i] * s, legY, legZ[i] * s),
                    new Vector3(0.20f * s, legH, 0.20f * s), Vector3.zero, dark);

            // ---------- 躯干（宽厚；TriBand 拆三色分区）
            float torsoY = 0.72f * s, torsoH = 0.68f * s;
            if (r.TriBand)
            {
                RuntimeArt.Box(t, "TorsoA", new Vector3(0f, torsoY + torsoH * 0.72f, 0f),
                    new Vector3(0.64f * s, torsoH * 0.3f, 0.46f * s), Vector3.zero, body);
                RuntimeArt.Box(t, "TorsoB", new Vector3(0f, torsoY + torsoH * 0.5f, 0f),
                    new Vector3(0.64f * s, torsoH * 0.3f, 0.46f * s), Vector3.zero, dark);
                RuntimeArt.Box(t, "TorsoC", new Vector3(0f, torsoY + torsoH * 0.28f, 0f),
                    new Vector3(0.64f * s, torsoH * 0.3f, 0.46f * s), Vector3.zero, accent);
            }
            else
            {
                RuntimeArt.Box(t, "Torso", new Vector3(0f, torsoY + torsoH * 0.5f, 0f),
                    new Vector3(0.64f * s, torsoH, 0.46f * s), Vector3.zero, body);
            }

            if (r.ChestGlow)
                RuntimeArt.Box(t, "ChestCore", new Vector3(0f, (torsoY + torsoH * 0.52f), 0.24f * s),
                    new Vector3(0.22f * s, 0.20f * s, 0.06f * s), Vector3.zero, RuntimeArt.Mat(r.Accent, 2.0f));

            if (r.BackColumn)
                RuntimeArt.Tube(t, "BackColumn", new Vector3(0f, 1.05f * s, -0.34f * s),
                    new Vector3(0.16f * s, 1.30f * s, 0.16f * s), new Vector3(0f, 0f, 6f), dark);

            // ---------- 肩 / 臂
            float shY = 1.34f * s, shX = 0.34f * s;
            RuntimeArt.Ball(t, "ShoulderL", new Vector3(-shX, shY, 0f), new Vector3(0.26f * s, 0.24f * s, 0.26f * s), Vector3.zero, body);
            RuntimeArt.Ball(t, "ShoulderR", new Vector3(shX, shY, 0f), new Vector3(0.26f * s, 0.24f * s, 0.26f * s), Vector3.zero, body);

            if (r.RightShoulderUp)
                RuntimeArt.Box(t, "EaveTip", new Vector3(shX + 0.14f * s, shY + 0.18f * s, 0f),
                    new Vector3(0.34f * s, 0.05f * s, 0.10f * s), new Vector3(0f, 0f, 26f), accent);

            // 手臂：举起 / 持械 / 自然下垂 —— 剪影上差异最大的是这一维
            Vector3 armL = new Vector3(-shX - 0.06f * s, shY - 0.06f * s, 0f);
            Vector3 armR = new Vector3(shX + 0.06f * s, shY - 0.06f * s, 0f);
            float armLen = 0.58f * s;
            bool armUp = r.Hold == HoldKind.JuShouZheE;

            RuntimeArt.Tube(t, "ArmL", armL + new Vector3(0f, -armLen * 0.5f, 0f),
                new Vector3(0.17f * s, armLen, 0.17f * s), new Vector3(0f, 0f, armUp ? -42f : 6f), body);
            RuntimeArt.Tube(t, "ArmR", armR + new Vector3(0f, -armLen * 0.5f, 0f),
                new Vector3(0.17f * s, armLen, 0.17f * s), new Vector3(0f, 0f, armUp ? 62f : -6f), body);

            // ---------- 手持（挂点 AT_ItemR / AT_ItemL）
            Vector3 handR = armR + new Vector3(0f, -armLen, 0.04f * s);
            Vector3 handL = armL + new Vector3(0f, -armLen, 0.04f * s);
            switch (r.Hold)
            {
                case HoldKind.QiJia:
                    RuntimeArt.Box(t, "FlagPole", handR + new Vector3(0.05f * s, 0.55f * s, 0f),
                        new Vector3(0.045f * s, 1.5f * s, 0.045f * s), Vector3.zero, dark);
                    RuntimeArt.Box(t, "Flag", handR + new Vector3(0.22f * s, 1.15f * s, 0f),
                        new Vector3(0.34f * s, 0.30f * s, 0.02f * s), Vector3.zero, accent);
                    break;
                case HoldKind.FangDunDuanJian:
                    RuntimeArt.Box(t, "Shield", handL + new Vector3(-0.12f * s, -0.05f * s, 0.16f * s),
                        new Vector3(0.08f * s, 0.62f * s, 0.48f * s), Vector3.zero, accent);
                    RuntimeArt.Box(t, "ShortSword", handR + new Vector3(0.10f * s, -0.18f * s, 0.06f * s),
                        new Vector3(0.06f * s, 0.44f * s, 0.06f * s), new Vector3(0f, 0f, 18f), RuntimeArt.Mat(new Color(0.72f, 0.74f, 0.78f)));
                    break;
                case HoldKind.ChuiXing:
                    RuntimeArt.Box(t, "HammerHaft", handR + new Vector3(0.10f * s, 0.30f * s, 0f),
                        new Vector3(0.06f * s, 1.0f * s, 0.06f * s), Vector3.zero, dark);
                    RuntimeArt.Box(t, "HammerHead", handR + new Vector3(0.10f * s, 0.86f * s, 0f),
                        new Vector3(0.34f * s, 0.26f * s, 0.26f * s), Vector3.zero, accent);
                    break;
                case HoldKind.JuRen:
                    RuntimeArt.Box(t, "GreatBlade", handR + new Vector3(0.24f * s, 0.90f * s, 0f),
                        new Vector3(0.10f * s, 1.90f * s, 0.04f * s), new Vector3(0f, 0f, -14f), accent);
                    break;
                case HoldKind.MingBan:
                    RuntimeArt.Box(t, "Tablet", handR + new Vector3(0.14f * s, -0.10f * s, 0.16f * s),
                        new Vector3(0.40f * s, 0.52f * s, 0.04f * s), new Vector3(0f, 0f, -6f), accent);
                    break;
                case HoldKind.MingBanFengYinZhu:
                    // 【R9 撞脸修复 · team-lead 2026-10-22 终裁】
                    // D21×R9 等高差异率只有 7.0%，根因是两者区分几乎全靠体型。终裁两条一起上：
                    //   ① 体型 2.4× → 2.8×（拉开体量）
                    //   ② 追加「断裂封印柱」—— 斜持、长度显著超出躯干轮廓，
                    //      给 R9 一个 D21 没有的、能突破剪影轮廓的标志部件
                    RuntimeArt.Box(t, "Tablet", handR + new Vector3(0.14f * s, -0.10f * s, 0.16f * s),
                        new Vector3(0.40f * s, 0.52f * s, 0.04f * s), new Vector3(0f, 0f, -6f), accent);
                    // 断裂封印柱：长 2.0×体型、斜 52°，柱顶明显越过头顶线
                    RuntimeArt.Tube(t, "SealPillar", handR + new Vector3(0.30f * s, 0.72f * s, 0.05f * s),
                        new Vector3(0.15f * s, 2.00f * s, 0.15f * s), new Vector3(0f, 0f, -52f),
                        RuntimeArt.Mat(new Color(0.80f, 0.77f, 0.70f)));
                    // 断口（顶端斜切的一小块，读得出"断"）
                    RuntimeArt.Box(t, "SealPillarTip", handR + new Vector3(-0.26f * s, 1.62f * s, 0.05f * s),
                        new Vector3(0.22f * s, 0.20f * s, 0.16f * s), new Vector3(0f, 0f, 34f),
                        RuntimeArt.Mat(new Color(0.62f, 0.60f, 0.56f)));
                    break;
                case HoldKind.TaDunKuoJian:
                    RuntimeArt.Box(t, "TowerShield", handL + new Vector3(-0.14f * s, 0.02f * s, 0.18f * s),
                        new Vector3(0.09f * s, 1.05f * s, 0.44f * s), Vector3.zero, accent);
                    RuntimeArt.Box(t, "BroadSword", handR + new Vector3(0.16f * s, -0.12f * s, 0.06f * s),
                        new Vector3(0.07f * s, 0.78f * s, 0.07f * s), new Vector3(0f, 0f, 20f), RuntimeArt.Mat(new Color(0.72f, 0.74f, 0.78f)));
                    break;
                case HoldKind.DuanJianDuanMao:
                    RuntimeArt.Box(t, "BrokenSword", handR + new Vector3(0.12f * s, -0.16f * s, 0.06f * s),
                        new Vector3(0.06f * s, 0.36f * s, 0.06f * s), new Vector3(0f, 0f, 22f), RuntimeArt.Mat(new Color(0.62f, 0.64f, 0.68f)));
                    RuntimeArt.Box(t, "BrokenSpear", handL + new Vector3(-0.16f * s, 0.42f * s, 0f),
                        new Vector3(0.05f * s, 1.30f * s, 0.05f * s), new Vector3(0f, 0f, 10f), dark);
                    break;
                case HoldKind.DuanZhu:
                    RuntimeArt.Tube(t, "BrokenPillar", handR + new Vector3(0.16f * s, 0.20f * s, 0f),
                        new Vector3(0.20f * s, 0.95f * s, 0.20f * s), Vector3.zero, RuntimeArt.Mat(new Color(0.78f, 0.75f, 0.70f)));
                    break;
                case HoldKind.DuanZhuX2:
                    RuntimeArt.Tube(t, "PillarR", handR + new Vector3(0.18f * s, 0.26f * s, 0f),
                        new Vector3(0.18f * s, 1.05f * s, 0.18f * s), Vector3.zero, RuntimeArt.Mat(new Color(0.78f, 0.75f, 0.70f)));
                    RuntimeArt.Tube(t, "PillarL", handL + new Vector3(-0.18f * s, 0.26f * s, 0f),
                        new Vector3(0.18f * s, 1.05f * s, 0.18f * s), Vector3.zero, RuntimeArt.Mat(new Color(0.78f, 0.75f, 0.70f)));
                    break;
                case HoldKind.ShouShouMuLiang:
                    RuntimeArt.Box(t, "Beam", handR + new Vector3(0.18f * s, 0.30f * s, 0f),
                        new Vector3(0.16f * s, 1.25f * s, 0.16f * s), new Vector3(0f, 0f, -12f), RuntimeArt.Mat(new Color(0.42f, 0.31f, 0.22f)));
                    RuntimeArt.Cone(t, "HornL", handR + new Vector3(0.02f * s, 0.92f * s, 0f),
                        new Vector3(0.10f * s, 0.34f * s, 0.10f * s), new Vector3(0f, 0f, -38f), RuntimeArt.Mat(new Color(0.86f, 0.84f, 0.78f)));
                    RuntimeArt.Cone(t, "HornR", handR + new Vector3(0.34f * s, 0.92f * s, 0f),
                        new Vector3(0.10f * s, 0.34f * s, 0.10f * s), new Vector3(0f, 0f, -24f), RuntimeArt.Mat(new Color(0.86f, 0.84f, 0.78f)));
                    break;
                case HoldKind.BingChui:
                    RuntimeArt.Box(t, "IceHaft", handR + new Vector3(0.10f * s, 0.24f * s, 0f),
                        new Vector3(0.07f * s, 0.92f * s, 0.07f * s), Vector3.zero, dark);
                    RuntimeArt.Box(t, "IceHead", handR + new Vector3(0.14f * s, 0.74f * s, 0f),
                        new Vector3(0.42f * s, 0.34f * s, 0.34f * s), Vector3.zero, accent);
                    break;
                case HoldKind.LuEnShiBanBi:
                    RuntimeArt.Box(t, "RuneSlabR", handR + new Vector3(0.10f * s, -0.06f * s, 0.10f * s),
                        new Vector3(0.30f * s, 0.66f * s, 0.06f * s), Vector3.zero, accent);
                    break;
                case HoldKind.ZhiChuai:
                    RuntimeArt.Cone(t, "BranchL", handL + new Vector3(-0.10f * s, -0.10f * s, 0f),
                        new Vector3(0.14f * s, 0.72f * s, 0.14f * s), new Vector3(0f, 0f, 34f), RuntimeArt.Mat(new Color(0.34f, 0.28f, 0.22f)));
                    RuntimeArt.Cone(t, "BranchR", handR + new Vector3(0.10f * s, -0.10f * s, 0f),
                        new Vector3(0.14f * s, 0.72f * s, 0.14f * s), new Vector3(0f, 0f, -34f), RuntimeArt.Mat(new Color(0.34f, 0.28f, 0.22f)));
                    break;
                case HoldKind.ShuangQuan:
                    RuntimeArt.Ball(t, "FistL", handL, new Vector3(0.24f * s, 0.24f * s, 0.24f * s), Vector3.zero, body);
                    RuntimeArt.Ball(t, "FistR", handR, new Vector3(0.24f * s, 0.24f * s, 0.24f * s), Vector3.zero, body);
                    break;
                case HoldKind.ChangRen:
                    RuntimeArt.Box(t, "LongBlade", handR + new Vector3(0.20f * s, 0.62f * s, 0f),
                        new Vector3(0.06f * s, 1.55f * s, 0.06f * s), new Vector3(0f, 0f, -16f), accent);
                    break;
                case HoldKind.SiDuanWuQi:
                    for (int k = 0; k < 4; k++)
                        RuntimeArt.Box(t, "Seg" + k, handR + new Vector3(0.16f * s + k * 0.02f * s, 0.30f * s + k * 0.36f * s, 0f),
                            new Vector3((0.10f - k * 0.015f) * s, 0.34f * s, 0.06f * s), new Vector3(0f, 0f, -10f + k * 8f), accent);
                    break;
                case HoldKind.SanTouWuQi:
                    RuntimeArt.Box(t, "W1", handR + new Vector3(0.14f * s, 0.16f * s, 0f),
                        new Vector3(0.06f * s, 0.80f * s, 0.06f * s), new Vector3(0f, 0f, 16f), RuntimeArt.Mat(new Color(0.62f, 0.64f, 0.68f)));
                    RuntimeArt.Box(t, "W2", handL + new Vector3(-0.14f * s, 0.44f * s, 0f),
                        new Vector3(0.05f * s, 1.28f * s, 0.05f * s), new Vector3(0f, 0f, -12f), dark);
                    RuntimeArt.Box(t, "W3", new Vector3(0f, 0.30f * s, -0.30f * s),
                        new Vector3(0.16f * s, 1.10f * s, 0.16f * s), new Vector3(0f, 0f, 24f), RuntimeArt.Mat(new Color(0.42f, 0.31f, 0.22f)));
                    break;
            }

            // ---------- 头（挂点 AT_Head）
            float headY = 1.40f * s;
            switch (r.Head)
            {
                case HeadKind.CuMian:
                    RuntimeArt.Box(t, "Head", new Vector3(0f, headY + 0.18f * s, 0f),
                        new Vector3(0.34f * s, 0.36f * s, 0.32f * s), Vector3.zero, accent);
                    RuntimeArt.Box(t, "Slit", new Vector3(0f, headY + 0.18f * s, 0.16f * s),
                        new Vector3(0.30f * s, 0.05f * s, 0.02f * s), Vector3.zero, RuntimeArt.Mat(Color.black));
                    break;
                case HeadKind.GreekHead:
                    RuntimeArt.Ball(t, "Head", new Vector3(0f, headY + 0.18f * s, 0f),
                        new Vector3(0.30f * s, 0.34f * s, 0.30f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "Nose", new Vector3(0f, headY + 0.14f * s, 0.16f * s),
                        new Vector3(0.07f * s, 0.16f * s, 0.08f * s), Vector3.zero, body);
                    break;
                case HeadKind.WuTouShiTai:
                    RuntimeArt.Box(t, "StoneTable", new Vector3(0f, headY + 0.08f * s, 0f),
                        new Vector3(0.46f * s, 0.16f * s, 0.40f * s), Vector3.zero, body);
                    break;
                case HeadKind.DoorFace:
                    // 身体本身是一扇门 —— 这一维的差异在剪影上极强
                    RuntimeArt.Box(t, "DoorBody", new Vector3(0f, 1.15f * s, 0f),
                        new Vector3(1.05f * s, 2.30f * s, 0.26f * s), Vector3.zero, body);
                    RuntimeArt.Ball(t, "Face", new Vector3(0f, 1.72f * s, 0.16f * s),
                        new Vector3(0.30f * s, 0.34f * s, 0.10f * s), Vector3.zero, accent);
                    RuntimeArt.Box(t, "DoorLintel", new Vector3(0f, 2.34f * s, 0f),
                        new Vector3(1.25f * s, 0.14f * s, 0.34f * s), Vector3.zero, accent);
                    break;
                case HeadKind.ShiBanTou:
                    RuntimeArt.Box(t, "SlabHead", new Vector3(0f, headY + 0.26f * s, 0f),
                        new Vector3(0.30f * s, 0.52f * s, 0.16f * s), Vector3.zero, body);
                    break;
                case HeadKind.TieJiangKui:
                    RuntimeArt.Ball(t, "Head", new Vector3(0f, headY + 0.18f * s, 0f),
                        new Vector3(0.28f * s, 0.30f * s, 0.28f * s), Vector3.zero, body);
                    RuntimeArt.Tube(t, "HelmBrim", new Vector3(0f, headY + 0.30f * s, 0f),
                        new Vector3(0.44f * s, 0.06f * s, 0.44f * s), Vector3.zero, accent);
                    break;
                case HeadKind.WuTouDuanZhi:
                    RuntimeArt.Box(t, "Neck", new Vector3(0f, headY + 0.06f * s, 0f),
                        new Vector3(0.34f * s, 0.14f * s, 0.32f * s), Vector3.zero, body);
                    for (int k = 0; k < 5; k++)
                    {
                        float a = -0.9f + k * 0.45f;
                        RuntimeArt.Cone(t, "Branch" + k,
                            new Vector3(Mathf.Sin(a) * 0.16f * s, headY + 0.34f * s, Mathf.Cos(a) * 0.06f * s),
                            new Vector3(0.10f * s, (0.30f + (k % 2) * 0.16f) * s, 0.10f * s),
                            new Vector3(a * 22f, 0f, -a * 30f), RuntimeArt.Mat(new Color(0.34f, 0.28f, 0.22f)));
                    }
                    break;
                case HeadKind.SanSeShiMian:
                    RuntimeArt.Box(t, "HeadA", new Vector3(-0.11f * s, headY + 0.20f * s, 0f),
                        new Vector3(0.12f * s, 0.38f * s, 0.30f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "HeadB", new Vector3(0f, headY + 0.20f * s, 0f),
                        new Vector3(0.12f * s, 0.38f * s, 0.30f * s), Vector3.zero, dark);
                    RuntimeArt.Box(t, "HeadC", new Vector3(0.11f * s, headY + 0.20f * s, 0f),
                        new Vector3(0.12f * s, 0.38f * s, 0.30f * s), Vector3.zero, accent);
                    break;
                case HeadKind.FangYinTou:
                    RuntimeArt.Box(t, "Head", new Vector3(0f, headY + 0.18f * s, 0f),
                        new Vector3(0.32f * s, 0.34f * s, 0.30f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "SealTop", new Vector3(0f, headY + 0.40f * s, 0f),
                        new Vector3(0.42f * s, 0.10f * s, 0.38f * s), Vector3.zero, accent);
                    break;
                case HeadKind.ShenXiangTou:
                    RuntimeArt.Ball(t, "Head", new Vector3(0f, headY + 0.18f * s, 0f),
                        new Vector3(0.30f * s, 0.34f * s, 0.30f * s), Vector3.zero, body);
                    RuntimeArt.Ball(t, "SocketL", new Vector3(-0.09f * s, headY + 0.20f * s, 0.14f * s),
                        new Vector3(0.09f * s, 0.09f * s, 0.06f * s), Vector3.zero, RuntimeArt.Mat(Color.black));
                    RuntimeArt.Ball(t, "SocketR", new Vector3(0.09f * s, headY + 0.20f * s, 0.14f * s),
                        new Vector3(0.09f * s, 0.09f * s, 0.06f * s), Vector3.zero, RuntimeArt.Mat(Color.black));
                    break;
                case HeadKind.WuTouDuanBei:
                    RuntimeArt.Box(t, "Neck", new Vector3(0f, headY + 0.06f * s, 0f),
                        new Vector3(0.34f * s, 0.14f * s, 0.32f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "BrokenStele", new Vector3(0f, headY + 0.40f * s, 0f),
                        new Vector3(0.28f * s, 0.56f * s, 0.14f * s), new Vector3(0f, 0f, 8f), RuntimeArt.Mat(new Color(0.80f, 0.78f, 0.72f)));
                    break;
                case HeadKind.IceBlock:
                    RuntimeArt.Box(t, "IceHead", new Vector3(0f, headY + 0.20f * s, 0f),
                        new Vector3(0.42f * s, 0.44f * s, 0.40f * s), Vector3.zero, accent);
                    break;
                case HeadKind.LuEnFangBei:
                    RuntimeArt.Box(t, "SteleHead", new Vector3(0f, headY + 0.30f * s, 0f),
                        new Vector3(0.28f * s, 0.66f * s, 0.18f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "RuneBand", new Vector3(0f, headY + 0.30f * s, 0.10f * s),
                        new Vector3(0.20f * s, 0.44f * s, 0.02f * s), Vector3.zero, accent);
                    break;
                case HeadKind.TouKuiHengFeng:
                    RuntimeArt.Ball(t, "Head", new Vector3(0f, headY + 0.18f * s, 0f),
                        new Vector3(0.28f * s, 0.32f * s, 0.28f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "VisorSlit", new Vector3(0f, headY + 0.16f * s, 0.15f * s),
                        new Vector3(0.26f * s, 0.05f * s, 0.06f * s), Vector3.zero, accent);
                    break;
                case HeadKind.YuanTong:
                    RuntimeArt.Tube(t, "CylinderHead", new Vector3(0f, headY + 0.20f * s, 0f),
                        new Vector3(0.30f * s, 0.40f * s, 0.30f * s), Vector3.zero, body);
                    RuntimeArt.Box(t, "Seam", new Vector3(0f, headY + 0.20f * s, 0.16f * s),
                        new Vector3(0.32f * s, 0.04f * s, 0.04f * s), Vector3.zero, RuntimeArt.Mat(Color.black));
                    break;
                case HeadKind.BanJieDengLong:
                    RuntimeArt.Ball(t, "Lantern", new Vector3(0f, headY + 0.16f * s, 0f),
                        new Vector3(0.34f * s, 0.30f * s, 0.34f * s), Vector3.zero, accent);
                    RuntimeArt.Box(t, "LanternCap", new Vector3(0f, headY + 0.34f * s, 0f),
                        new Vector3(0.40f * s, 0.06f * s, 0.40f * s), Vector3.zero, dark);
                    break;
                case HeadKind.SanTou:
                    RuntimeArt.Ball(t, "HeadL", new Vector3(-0.20f * s, headY + 0.22f * s, 0f),
                        new Vector3(0.24f * s, 0.26f * s, 0.24f * s), Vector3.zero, body);
                    RuntimeArt.Ball(t, "HeadM", new Vector3(0f, headY + 0.30f * s, 0f),
                        new Vector3(0.24f * s, 0.26f * s, 0.24f * s), Vector3.zero, dark);
                    RuntimeArt.Ball(t, "HeadR", new Vector3(0.20f * s, headY + 0.22f * s, 0f),
                        new Vector3(0.24f * s, 0.26f * s, 0.24f * s), Vector3.zero, accent);
                    break;
            }

            // ---------- 悬浮物（挂点 AT_Float / AT_Back）
            switch (r.Float)
            {
                case FloatKind.FuShi3: FloatStones(t, 3, s, accent); break;
                case FloatKind.FuShi6: FloatStones(t, 6, s, accent); break;
                case FloatKind.FuShi9: FloatStones(t, 9, s, accent); break;
                case FloatKind.TouDing3Jian:
                    for (int k = 0; k < 3; k++)
                        RuntimeArt.Box(t, "SkySword" + k,
                            new Vector3((k - 1) * 0.16f * s, headY + 0.78f * s, 0f),
                            new Vector3(0.05f * s, 0.46f * s, 0.05f * s),
                            new Vector3(0f, 0f, (k - 1) * 14f), RuntimeArt.Mat(new Color(0.76f, 0.78f, 0.82f)));
                    break;
                case FloatKind.TouDingHuanHen:
                    RuntimeArt.Ring(t, "CrownRing", new Vector3(0f, headY + 0.62f * s, 0f), 0.58f * s, accent);
                    break;
                case FloatKind.TouDingBanHuan:
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k / 4f * Mathf.PI;
                        RuntimeArt.Box(t, "HalfRing" + k,
                            new Vector3(Mathf.Cos(a) * 0.28f * s, headY + 0.62f * s + Mathf.Sin(a) * 0.18f * s, 0f),
                            new Vector3(0.10f * s, 0.10f * s, 0.06f * s), Vector3.zero, accent);
                    }
                    break;
                case FloatKind.BeiHou3Pian: BackScreens(t, 3, s, accent); break;
                case FloatKind.BeiHou6Pian: BackScreens(t, 6, s, accent); break;
            }

            // ---------- 冰壳
            // 【诚实说明】当前 stylized 材质是不透明的，所以这层壳会把内部骨架完全包住 ——
            // 这是冰霜巨兵在剪影层的**最不利情形**（剪影退化成一个方块）。
            // 若最终做成真半透（能看见内部骨架），只比这个结果更好，不会更差。
            if (r.IceShell)
                RuntimeArt.Box(t, "IceShell", new Vector3(0f, 1.05f * s, 0f),
                    new Vector3(1.02f * s, 2.10f * s, 0.86f * s), Vector3.zero,
                    RuntimeArt.Mat(new Color(0.72f, 0.86f, 0.95f, 0.55f)));

            return root;
        }

        private static void FloatStones(Transform t, int n, float s, Material mat)
        {
            float r = 0.52f * s, y = 1.92f * s;
            for (int k = 0; k < n; k++)
            {
                float a = k / (float)n * Mathf.PI * 2f;
                RuntimeArt.Box(t, "Rune" + k,
                    new Vector3(Mathf.Cos(a) * r, y + Mathf.Sin(a * 2f) * 0.10f * s, Mathf.Sin(a) * r),
                    new Vector3(0.16f * s, 0.22f * s, 0.16f * s), new Vector3(0f, a * 40f, 0f), mat);
            }
        }

        private static void BackScreens(Transform t, int n, float s, Material mat)
        {
            for (int k = 0; k < n; k++)
            {
                float a = (k - (n - 1) / 2f) * (0.42f - n * 0.02f);
                RuntimeArt.Box(t, "Screen" + k,
                    new Vector3(Mathf.Sin(a) * 0.46f * s, 1.50f * s, -0.36f * s - Mathf.Cos(a) * 0.10f * s),
                    new Vector3(0.30f * s, 0.92f * s, 0.04f * s), new Vector3(0f, -a * 40f, 0f), mat);
            }
        }

        private static Color Hex(string h)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(h, out c) ? c : Color.gray;
        }
    }
}
