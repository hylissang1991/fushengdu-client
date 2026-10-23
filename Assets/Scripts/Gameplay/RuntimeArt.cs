// 《浮生渡》表现层 · 运行时美术工具箱
// 背景：垂直切片早期为了打通流程，玩家/怪物/NPC 全是 Unity 默认胶囊体，
//       进入游戏后画面等于"白色空场 + 几个方块"，完全不具备可读性。
// 本文件用程序化方式拼装低模角色（不依赖任何外部美术资源），并把它们接到现有玩法实体上：
//   PlayerActor.Setup        → AttachPlayerModel
//   MonsterActor.Initialize  → AttachMonsterModel
//   NpcEntity.Awake          → AttachNpcModel
// 造型遵循美术圣经：剪影先行（宽肩/袍摆/顶髻/武器外露）、三域色板、L4 光阶留给稀有度演出。
using System.Collections.Generic;
using Fushengdu.Core;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    public static class RuntimeArt
    {
        private const string MatResourcePath = "Art/Mat_Stylized";

        private static Shader _shader;
        private static Material _baseMaterial;
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Mesh _cone;
        private static Mesh _ring;

        // ---------------------------------------------------------------- 材质

        /// <summary>风格化主着色器（Resources 里预置材质引用它，保证打包不被剥离）。</summary>
        public static Shader Stylized
        {
            get
            {
                if (_shader != null) return _shader;
                _baseMaterial = Resources.Load<Material>(MatResourcePath);
                if (_baseMaterial != null) _shader = _baseMaterial.shader;
                if (_shader == null) _shader = Shader.Find("Fushengdu/Stylized");
                if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit");
                return _shader;
            }
        }

        public static Material Mat(Color color, float emission = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color) + "|" + Mathf.RoundToInt(emission * 100f);
            Material cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            Material m = _baseMaterial != null ? new Material(_baseMaterial) : new Material(Stylized);
            if (m.HasProperty("_Color")) m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Emission")) m.SetFloat("_Emission", emission);
            m.enableInstancing = true;
            Cache[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- 基础零件

        public static GameObject Part(PrimitiveType type, Transform parent, string name,
            Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }

        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 euler, Material mat)
            => Part(PrimitiveType.Cube, parent, name, pos, size, euler, mat);

        public static GameObject Ball(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 euler, Material mat)
            => Part(PrimitiveType.Sphere, parent, name, pos, size, euler, mat);

        public static GameObject Tube(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 euler, Material mat)
            => Part(PrimitiveType.Cylinder, parent, name, pos, size, euler, mat);

        public static GameObject Cone(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 euler, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = ConeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// 关节枢轴：空节点放在关节（肩 / 髋）位置，肢体作为子节点向下偏移。
        /// 这样 CharacterMotion 旋转枢轴时是"摆动"，而不是绕肢体中心打转。
        /// </summary>
        private static Transform Pivot(Transform parent, string name, Vector3 pos, Vector3 euler = default(Vector3))
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            return go.transform;
        }

        /// <summary>圆锥网格（底面半径 0.5、高 1、顶点朝 +Y），单位尺寸便于缩放。</summary>
        public static Mesh ConeMesh
        {
            get
            {
                if (_cone != null) return _cone;
                const int sides = 14;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                var uvs = new List<Vector2>();

                verts.Add(new Vector3(0f, 1f, 0f));      // 0: 顶点
                uvs.Add(new Vector2(0.5f, 1f));
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
                    uvs.Add(new Vector2(i / (float)sides, 0f));
                }
                for (int i = 1; i <= sides; i++)
                {
                    tris.Add(0); tris.Add(i + 1); tris.Add(i);
                }
                // 底面
                int center = verts.Count;
                verts.Add(Vector3.zero); uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 1; i <= sides; i++)
                {
                    tris.Add(center); tris.Add(i); tris.Add(i + 1);
                }

                _cone = new Mesh { name = "FD_Cone" };
                _cone.SetVertices(verts);
                _cone.SetUVs(0, uvs);
                _cone.SetTriangles(tris, 0);
                _cone.RecalculateNormals();
                _cone.RecalculateBounds();
                return _cone;
            }
        }

        /// <summary>薄圆环（用于脚下选中环、光环、地面法阵）。</summary>
        public static Mesh RingMesh
        {
            get
            {
                if (_ring != null) return _ring;
                const int sides = 48;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
                    verts.Add(new Vector3(Mathf.Cos(a) * 0.40f, 0f, Mathf.Sin(a) * 0.40f));
                }
                for (int i = 0; i < sides; i++)
                {
                    int b = i * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
                _ring = new Mesh { name = "FD_Ring" };
                _ring.SetVertices(verts);
                _ring.SetTriangles(tris, 0);
                _ring.RecalculateNormals();
                _ring.RecalculateBounds();
                return _ring;
            }
        }

        public static GameObject Ring(Transform parent, string name, Vector3 pos, float diameter, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(diameter, 1f, diameter);
            go.AddComponent<MeshFilter>().sharedMesh = RingMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ---------------------------------------------------------------- 色板（美术圣经 v3 §3.2）

        public static readonly Color SkinEast = new Color(0.90f, 0.76f, 0.64f);
        public static readonly Color HairInk = new Color(0.12f, 0.12f, 0.14f);
        public static readonly Color RobeJade = new Color(0.30f, 0.48f, 0.55f);
        public static readonly Color RobeAzure = new Color(0.24f, 0.40f, 0.62f);
        public static readonly Color RobeCrimson = new Color(0.56f, 0.19f, 0.18f);
        public static readonly Color ClothBone = new Color(0.86f, 0.83f, 0.76f);
        public static readonly Color LeatherBrown = new Color(0.40f, 0.28f, 0.19f);
        public static readonly Color IronDark = new Color(0.34f, 0.36f, 0.40f);
        public static readonly Color GoldTrim = new Color(0.79f, 0.63f, 0.36f);
        public static readonly Color FurGray = new Color(0.45f, 0.44f, 0.42f);
        public static readonly Color FurDark = new Color(0.26f, 0.25f, 0.25f);
        public static readonly Color EyeGlow = new Color(1.00f, 0.42f, 0.24f);

        /// <summary>早期占位物是 Unity 默认胶囊体，拼装模型后要把它的渲染关掉，否则会套在模型外面。</summary>
        private static void HideLegacyRenderer(GameObject root)
        {
            Renderer legacy = root.GetComponent<Renderer>();
            if (legacy != null) legacy.enabled = false;
        }

        // ---------------------------------------------------------------- 玩家

        /// <summary>重建玩家模型（换职业会重建，先清掉旧的）。</summary>
        public static void AttachPlayerModel(GameObject root, ClassId classId, bool female)
        {
            if (root == null) return;
            HideLegacyRenderer(root);
            Transform old = root.transform.Find("Model");
            if (old != null) Object.Destroy(old.gameObject);

            GameObject model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            // 优先用带骨骼的真实建模（glb → Blender → FBX → prefab）；
            // 缺资产时回退到程序化胶囊人
            Animator rig = CharacterModels.Attach(model.transform, classId, female);
            bool real = rig != null;
            if (!real)
            {
                if (classId == ClassId.Berserker) BuildBerserker(model.transform, female);
                else BuildSwordCultivator(model.transform, female);
            }
            Debug.Log("[浮生渡] 玩家模型：" + (real ? "真实建模 " + CharacterModels.JobOf(classId) : "程序化胶囊人（缺建模资产）"));

            CharacterTint tint = model.AddComponent<CharacterTint>();
            tint.Collect();

            if (real)
            {
                // 带骨骼：动画交给 Controller，这里只喂 Speed（走/跑/待机由状态机判断）
                var motion = model.AddComponent<RiggedCharacterMotion>();
                motion.Anim = rig;
            }
            else
            {
                // 静态网格兜底：保留原来的整体颠簸假动作
                model.AddComponent<CharacterMotion>();
            }

            // 脚下选中环：玩家自身为青色（己方蓝，美术圣经 §3.5 敌我识别）
            Ring(model.transform, "SelectionRing", new Vector3(0f, 0.03f, 0f), 1.5f,
                Mat(new Color(0.35f, 0.75f, 1f), 0.9f));
        }

        private static void BuildSwordCultivator(Transform t, bool female)
        {
            Material skin = Mat(SkinEast);
            Material robe = Mat(RobeAzure);
            Material robeDark = Mat(new Color(0.16f, 0.26f, 0.42f));
            Material hair = Mat(HairInk);
            Material trim = Mat(GoldTrim, 0.25f);

            // 比例基准（总高 ≈1.85m）：髋 0.92 / 肩 1.36 / 头心 1.62
            // 两条铁律：① 躯干必须比头宽（否则是大头娃娃）；② 袍摆必须"底宽腰细"（锥底朝下），
            //          上一版锥底朝上，结果是"漏斗套腿 + 腰上断一截"，模型完全读不出人形。
            float s = female ? 0.95f : 1f;
            // 袍摆半径：上一版 0.42~0.46（直径近 1 米）在第三人称背视角下整个剪影是"一口钟"，
            // 肩和手臂全被摆子吃掉。收到 0.32~0.35（直径 0.65~0.70m），肩线才能压住下摆。
            float hemR = female ? 0.32f : 0.35f;
            float shX = female ? 0.175f : 0.19f;   // 肩关节 X
            float hipX = female ? 0.10f : 0.11f;

            // ---------- 袍摆：及膝（不落地）。
            // 上一版裙摆从地面直接罩到腰，整个人只剩"一个锥子 + 一个头"，读不出人形。
            // 现在收到膝盖，下面露出小腿和靴子，剪影才立得住。
            float hemY = 0.42f * s;
            Cone(t, "RobeSkirt", new Vector3(0f, hemY, 0f),
                new Vector3(hemR * 2f, 0.58f * s, hemR * 2f), Vector3.zero, robe);
            Tube(t, "Hem", new Vector3(0f, hemY + 0.02f, 0f), new Vector3(hemR * 1.9f, 0.022f, hemR * 1.9f), Vector3.zero, robeDark);
            // 髋部过渡块：圆锥顶端只有 1 个点，不补一块会在腰部掐出怪异的细腰
            Tube(t, "Hip", new Vector3(0f, 0.86f * s, 0f), new Vector3(hemR * 1.16f, 0.09f, hemR * 1.04f), Vector3.zero, robe);

            // ---------- 腿（露在小腿处，走路时有摆动）
            Transform legL = Pivot(t, "LegL", new Vector3(-hipX, 0.92f * s, 0f));
            Transform legR = Pivot(t, "LegR", new Vector3(hipX, 0.92f * s, 0f));
            Part(PrimitiveType.Capsule, legL, "LegLMesh", new Vector3(0f, -0.34f, 0f), new Vector3(0.145f, 0.34f, 0.145f), Vector3.zero, Mat(new Color(0.20f, 0.21f, 0.26f)));
            Part(PrimitiveType.Capsule, legR, "LegRMesh", new Vector3(0f, -0.34f, 0f), new Vector3(0.145f, 0.34f, 0.145f), Vector3.zero, Mat(new Color(0.20f, 0.21f, 0.26f)));
            // 布靴
            Box(t, "BootL", new Vector3(-hipX, 0.09f * s, 0.04f), new Vector3(0.17f, 0.18f, 0.28f), Vector3.zero, Mat(new Color(0.15f, 0.15f, 0.18f)));
            Box(t, "BootR", new Vector3(hipX, 0.09f * s, 0.04f), new Vector3(0.17f, 0.18f, 0.28f), Vector3.zero, Mat(new Color(0.15f, 0.15f, 0.18f)));

            // ---------- 躯干（比头宽 —— 这是"像个人"和"不像人"的分水岭）
            Tube(t, "Torso", new Vector3(0f, 1.16f * s, 0f), new Vector3(0.44f, 0.28f * s, 0.34f), Vector3.zero, robe);
            Box(t, "Lapel", new Vector3(0f, 1.22f * s, 0.155f), new Vector3(0.17f, 0.40f, 0.035f), Vector3.zero, robeDark);
            Tube(t, "Sash", new Vector3(0f, 0.95f * s, 0f), new Vector3(0.46f, 0.055f, 0.37f), Vector3.zero, trim);

            // ---------- 肩 / 臂（枢轴放在肩关节，摆臂才是"甩"不是"打转"）
            Ball(t, "ShoulderL", new Vector3(-shX, 1.36f * s, 0f), new Vector3(0.23f, 0.17f, 0.23f), Vector3.zero, robe);
            Ball(t, "ShoulderR", new Vector3(shX, 1.36f * s, 0f), new Vector3(0.23f, 0.17f, 0.23f), Vector3.zero, robe);
            Transform armL = Pivot(t, "ArmL", new Vector3(-shX - 0.02f, 1.36f * s, 0f), new Vector3(0f, 0f, 6f));
            Transform armR = Pivot(t, "ArmR", new Vector3(shX + 0.02f, 1.36f * s, 0f), new Vector3(0f, 0f, -6f));
            Part(PrimitiveType.Capsule, armL, "ArmLMesh", new Vector3(0f, -0.21f, 0f), new Vector3(0.11f, 0.21f, 0.11f), Vector3.zero, robe);
            Part(PrimitiveType.Capsule, armR, "ArmRMesh", new Vector3(0f, -0.21f, 0f), new Vector3(0.11f, 0.21f, 0.11f), Vector3.zero, robe);
            Ball(armL, "HandL", new Vector3(0f, -0.44f, 0.02f), new Vector3(0.11f, 0.12f, 0.11f), Vector3.zero, skin);
            Ball(armR, "HandR", new Vector3(0f, -0.44f, 0.02f), new Vector3(0.11f, 0.12f, 0.11f), Vector3.zero, skin);

            // ---------- 颈 / 头
            Tube(t, "Neck", new Vector3(0f, 1.45f * s, 0f), new Vector3(0.14f, 0.045f, 0.14f), Vector3.zero, skin);
            Ball(t, "Head", new Vector3(0f, 1.62f * s, 0f), new Vector3(0.30f, 0.32f, 0.29f), Vector3.zero, skin);
            Ball(t, "HairCap", new Vector3(0f, 1.655f * s, -0.02f), new Vector3(0.325f, 0.29f, 0.315f), Vector3.zero, hair);
            Ball(t, "Topknot", new Vector3(0f, 1.80f * s, -0.05f), new Vector3(0.13f, 0.15f, 0.13f), Vector3.zero, hair);
            Box(t, "HairBand", new Vector3(0f, 1.725f * s, -0.02f), new Vector3(0.295f, 0.035f, 0.295f), Vector3.zero, trim);
            // 眼（极简两笔，远看仍能读出朝向）
            Ball(t, "EyeL", new Vector3(-0.072f, 1.635f * s, 0.135f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, Mat(HairInk));
            Ball(t, "EyeR", new Vector3(0.072f, 1.635f * s, 0.135f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, Mat(HairInk));

            // ---------- 背剑（斜挎背后：剑鞘从左腰斜到右肩，剑柄探出右肩外）
            // 【踩坑】上一版剑柄中心 (0.28, 1.70) 正好落在头（半径 0.15，头心 1.62）旁边，
            // 背视角看就是"一根木棍穿过脑袋"。现在整组沿倾斜轴推到肩外 x≈0.30、
            // 并整体后移 z=-0.235，保证剑身完全在身体后面、剑柄在头轮廓之外。
            Material scabMat = Mat(new Color(0.17f, 0.16f, 0.19f));
            Box(t, "Scabbard", new Vector3(0.06f, 1.14f * s, -0.235f), new Vector3(0.075f, 0.86f, 0.045f), new Vector3(0f, 0f, -26f), scabMat);
            Box(t, "ScabTip", new Vector3(-0.13f, 0.79f * s, -0.235f), new Vector3(0.085f, 0.12f, 0.052f), new Vector3(0f, 0f, -26f), Mat(GoldTrim, 0.2f));
            Box(t, "Guard", new Vector3(0.25f, 1.53f * s, -0.235f), new Vector3(0.20f, 0.045f, 0.06f), new Vector3(0f, 0f, -26f), Mat(GoldTrim, 0.35f));
            Box(t, "Hilt", new Vector3(0.30f, 1.60f * s, -0.235f), new Vector3(0.055f, 0.22f, 0.055f), new Vector3(0f, 0f, -26f), Mat(new Color(0.33f, 0.22f, 0.16f)));
            Ball(t, "Pommel", new Vector3(0.35f, 1.71f * s, -0.235f), new Vector3(0.07f, 0.07f, 0.07f), Vector3.zero, Mat(GoldTrim, 0.45f));
            Box(t, "Tassel", new Vector3(0.22f, 1.50f * s, -0.27f), new Vector3(0.028f, 0.20f, 0.02f), Vector3.zero, Mat(RobeCrimson));
        }

        private static void BuildBerserker(Transform t, bool female)
        {
            Material skin = Mat(new Color(0.86f, 0.70f, 0.58f));
            Material leather = Mat(LeatherBrown);
            Material fur = Mat(FurGray);
            Material cloth = Mat(new Color(0.44f, 0.22f, 0.18f));
            Material iron = Mat(IronDark);
            Material hair = Mat(new Color(0.55f, 0.42f, 0.24f));

            float s = female ? 0.96f : 1.03f;      // 狂战比剑修更高更壮
            float shX = female ? 0.21f : 0.235f;
            float hipX = female ? 0.12f : 0.135f;

            // ---------- 铁靴
            Box(t, "BootL", new Vector3(-0.15f, 0.12f * s, 0.05f), new Vector3(0.20f, 0.24f, 0.32f), Vector3.zero, iron);
            Box(t, "BootR", new Vector3(0.15f, 0.12f * s, 0.05f), new Vector3(0.20f, 0.24f, 0.32f), Vector3.zero, iron);

            // ---------- 腿（露在外面，走路摆幅最明显）
            Transform legL = Pivot(t, "LegL", new Vector3(-hipX, 0.92f * s, 0f));
            Transform legR = Pivot(t, "LegR", new Vector3(hipX, 0.92f * s, 0f));
            Part(PrimitiveType.Capsule, legL, "LegLMesh", new Vector3(0f, -0.30f, 0f), new Vector3(0.18f, 0.32f, 0.18f), Vector3.zero, leather);
            Part(PrimitiveType.Capsule, legR, "LegRMesh", new Vector3(0f, -0.30f, 0f), new Vector3(0.18f, 0.32f, 0.18f), Vector3.zero, leather);

            // ---------- 战裙（只到膝上，腿是露出来的）
            Cone(t, "WarSkirt", new Vector3(0f, 0.58f * s, 0f), new Vector3(0.80f, 0.40f * s, 0.76f), Vector3.zero, cloth);

            // ---------- 赤裸上身（更厚，比头明显宽）
            Tube(t, "Torso", new Vector3(0f, 1.16f * s, 0f), new Vector3(0.54f, 0.28f * s, 0.42f), Vector3.zero, skin);
            Box(t, "Belt", new Vector3(0f, 0.94f * s, 0f), new Vector3(0.58f, 0.11f, 0.46f), Vector3.zero, leather);
            Box(t, "Harness", new Vector3(0f, 1.26f * s, 0.20f), new Vector3(0.36f, 0.11f, 0.045f), Vector3.zero, leather);

            // ---------- 毛皮肩甲
            Ball(t, "FurL", new Vector3(-shX, 1.40f * s, 0f), new Vector3(0.36f, 0.27f, 0.34f), Vector3.zero, fur);
            Ball(t, "FurR", new Vector3(shX, 1.40f * s, 0f), new Vector3(0.36f, 0.27f, 0.34f), Vector3.zero, fur);

            // ---------- 粗壮手臂
            Transform armL = Pivot(t, "ArmL", new Vector3(-shX - 0.03f, 1.36f * s, 0f), new Vector3(0f, 0f, 9f));
            Transform armR = Pivot(t, "ArmR", new Vector3(shX + 0.03f, 1.36f * s, 0f), new Vector3(0f, 0f, -9f));
            Part(PrimitiveType.Capsule, armL, "ArmLMesh", new Vector3(0f, -0.22f, 0f), new Vector3(0.15f, 0.22f, 0.15f), Vector3.zero, skin);
            Part(PrimitiveType.Capsule, armR, "ArmRMesh", new Vector3(0f, -0.22f, 0f), new Vector3(0.15f, 0.22f, 0.15f), Vector3.zero, skin);
            Ball(armL, "HandL", new Vector3(0f, -0.46f, 0.02f), new Vector3(0.14f, 0.15f, 0.14f), Vector3.zero, skin);
            Ball(armR, "HandR", new Vector3(0f, -0.46f, 0.02f), new Vector3(0.14f, 0.15f, 0.14f), Vector3.zero, skin);

            // ---------- 颈 / 头 / 发辫
            Tube(t, "Neck", new Vector3(0f, 1.45f * s, 0f), new Vector3(0.16f, 0.05f, 0.16f), Vector3.zero, skin);
            Ball(t, "Head", new Vector3(0f, 1.63f * s, 0f), new Vector3(0.31f, 0.33f, 0.30f), Vector3.zero, skin);
            Ball(t, "HairCap", new Vector3(0f, 1.665f * s, -0.03f), new Vector3(0.335f, 0.28f, 0.325f), Vector3.zero, hair);
            Box(t, "Braid", new Vector3(0f, 1.50f * s, -0.17f), new Vector3(0.085f, 0.40f, 0.085f), new Vector3(12f, 0f, 0f), hair);
            Ball(t, "EyeL", new Vector3(-0.075f, 1.645f * s, 0.14f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, Mat(new Color(0.85f, 0.25f, 0.2f), 0.5f));
            Ball(t, "EyeR", new Vector3(0.075f, 1.645f * s, 0.14f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, Mat(new Color(0.85f, 0.25f, 0.2f), 0.5f));

            // ---------- 背斧（巨武器轮廓）
            Box(t, "AxeHandle", new Vector3(0.17f, 1.24f * s, -0.23f), new Vector3(0.06f, 1.02f, 0.06f), new Vector3(0f, 0f, -14f), leather);
            Box(t, "AxeHead", new Vector3(0.31f, 1.68f * s, -0.23f), new Vector3(0.34f, 0.28f, 0.055f), new Vector3(0f, 0f, -14f), iron);
            Box(t, "AxeTrim", new Vector3(0.31f, 1.68f * s, -0.205f), new Vector3(0.36f, 0.06f, 0.03f), new Vector3(0f, 0f, -14f), Mat(GoldTrim, 0.2f));
        }

        // ---------------------------------------------------------------- 怪物

        public static void AttachMonsterModel(GameObject root, MonsterDef def)
        {
            if (root == null || def == null) return;
            HideLegacyRenderer(root);
            Transform old = root.transform.Find("Model");
            if (old != null) Object.Destroy(old.gameObject);

            GameObject model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            switch (def.Id)
            {
                case 1: BuildWolf(model.transform, def.IsElite); break;
                case 2: BuildBoarKing(model.transform); break;
                case 3: BuildBandit(model.transform); break;
                default: BuildWolf(model.transform, def.IsElite); break;
            }

            CharacterTint tint = model.AddComponent<CharacterTint>();
            tint.Collect();
            model.AddComponent<CharacterMotion>();

            // 敌方红（美术圣经 §3.5）脚下环
            if (def.IsElite || def.IsBoss)
                Ring(model.transform, "EliteRing", new Vector3(0f, 0.04f, 0f), 2.2f,
                    Mat(new Color(0.83f, 0.24f, 0.16f), 1.2f));

            // 保留一个胶囊碰撞体，保证鼠标点选 / 射线命中仍可用
            CapsuleCollider col = root.GetComponent<CapsuleCollider>();
            if (col == null) col = root.AddComponent<CapsuleCollider>();
            col.height = def.IsElite ? 2.4f : 1.8f;
            col.radius = def.IsElite ? 0.8f : 0.5f;
            col.center = new Vector3(0f, col.height * 0.5f, 0f);
        }

        private static void BuildWolf(Transform t, bool elite)
        {
            Material fur = Mat(new Color(0.38f, 0.37f, 0.39f));
            Material belly = Mat(new Color(0.55f, 0.53f, 0.52f));
            Material dark = Mat(FurDark);
            Material eye = Mat(EyeGlow, 1.6f);
            float s = elite ? 1.25f : 1f;

            // 躯干（沿 +Z 卧姿）
            Part(PrimitiveType.Capsule, t, "Body", new Vector3(0f, 0.62f * s, 0f),
                new Vector3(0.40f * s, 0.52f * s, 0.40f * s), new Vector3(90f, 0f, 0f), fur);
            Ball(t, "Belly", new Vector3(0f, 0.50f * s, 0.02f), new Vector3(0.40f * s, 0.30f * s, 0.62f * s), Vector3.zero, belly);
            // 头 + 吻部
            Ball(t, "Head", new Vector3(0f, 0.78f * s, 0.56f * s), new Vector3(0.34f * s, 0.32f * s, 0.34f * s), Vector3.zero, fur);
            Cone(t, "Snout", new Vector3(0f, 0.72f * s, 0.78f * s), new Vector3(0.24f * s, 0.34f * s, 0.24f * s), new Vector3(90f, 0f, 0f), belly);
            Ball(t, "Nose", new Vector3(0f, 0.72f * s, 0.90f * s), new Vector3(0.09f, 0.08f, 0.08f), Vector3.zero, dark);
            // 耳
            Cone(t, "EarL", new Vector3(-0.13f * s, 0.95f * s, 0.50f * s), new Vector3(0.11f * s, 0.20f * s, 0.11f * s), new Vector3(-14f, 0f, 12f), dark);
            Cone(t, "EarR", new Vector3(0.13f * s, 0.95f * s, 0.50f * s), new Vector3(0.11f * s, 0.20f * s, 0.11f * s), new Vector3(-14f, 0f, -12f), dark);
            // 眼
            Ball(t, "EyeL", new Vector3(-0.11f * s, 0.83f * s, 0.70f * s), new Vector3(0.07f, 0.06f, 0.05f), Vector3.zero, eye);
            Ball(t, "EyeR", new Vector3(0.11f * s, 0.83f * s, 0.70f * s), new Vector3(0.07f, 0.06f, 0.05f), Vector3.zero, eye);
            // 四腿
            Part(PrimitiveType.Capsule, t, "LegFL", new Vector3(-0.17f * s, 0.32f * s, 0.34f * s), new Vector3(0.11f * s, 0.20f * s, 0.11f * s), Vector3.zero, fur);
            Part(PrimitiveType.Capsule, t, "LegFR", new Vector3(0.17f * s, 0.32f * s, 0.34f * s), new Vector3(0.11f * s, 0.20f * s, 0.11f * s), Vector3.zero, fur);
            Part(PrimitiveType.Capsule, t, "LegBL", new Vector3(-0.17f * s, 0.32f * s, -0.34f * s), new Vector3(0.11f * s, 0.20f * s, 0.11f * s), Vector3.zero, fur);
            Part(PrimitiveType.Capsule, t, "LegBR", new Vector3(0.17f * s, 0.32f * s, -0.34f * s), new Vector3(0.11f * s, 0.20f * s, 0.11f * s), Vector3.zero, fur);
            // 尾
            Cone(t, "Tail", new Vector3(0f, 0.70f * s, -0.66f * s), new Vector3(0.13f * s, 0.46f * s, 0.13f * s), new Vector3(-105f, 0f, 0f), fur);
        }

        private static void BuildBandit(Transform t)
        {
            Material skin = Mat(new Color(0.82f, 0.66f, 0.52f));
            Material coat = Mat(new Color(0.33f, 0.26f, 0.22f));
            Material hood = Mat(new Color(0.22f, 0.20f, 0.20f));
            Material dark = Mat(new Color(0.16f, 0.15f, 0.16f));
            Material steel = Mat(new Color(0.62f, 0.64f, 0.68f));

            Cone(t, "Coat", new Vector3(0f, 0.66f, 0f), new Vector3(0.78f, 0.80f, 0.78f), new Vector3(180f, 0f, 0f), coat);
            Tube(t, "Torso", new Vector3(0f, 1.14f, 0f), new Vector3(0.25f, 0.22f, 0.22f), Vector3.zero, coat);
            Ball(t, "ShoulderL", new Vector3(-0.30f, 1.36f, 0f), new Vector3(0.24f, 0.18f, 0.24f), Vector3.zero, coat);
            Ball(t, "ShoulderR", new Vector3(0.30f, 1.36f, 0f), new Vector3(0.24f, 0.18f, 0.24f), Vector3.zero, coat);
            Part(PrimitiveType.Capsule, t, "ArmL", new Vector3(-0.32f, 1.10f, 0.04f), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(10f, 0f, 10f), coat);
            Part(PrimitiveType.Capsule, t, "ArmR", new Vector3(0.32f, 1.10f, 0.04f), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(10f, 0f, -10f), coat);
            Ball(t, "HandL", new Vector3(-0.36f, 0.88f, 0.06f), new Vector3(0.12f, 0.13f, 0.12f), Vector3.zero, skin);
            Ball(t, "HandR", new Vector3(0.36f, 0.88f, 0.06f), new Vector3(0.12f, 0.13f, 0.12f), Vector3.zero, skin);
            Part(PrimitiveType.Capsule, t, "LegL", new Vector3(-0.12f, 0.42f, 0f), new Vector3(0.14f, 0.22f, 0.14f), Vector3.zero, dark);
            Part(PrimitiveType.Capsule, t, "LegR", new Vector3(0.12f, 0.42f, 0f), new Vector3(0.14f, 0.22f, 0.14f), Vector3.zero, dark);
            Ball(t, "Head", new Vector3(0f, 1.58f, 0f), new Vector3(0.30f, 0.31f, 0.29f), Vector3.zero, skin);
            Ball(t, "Hood", new Vector3(0f, 1.62f, -0.02f), new Vector3(0.36f, 0.36f, 0.36f), Vector3.zero, hood);
            Box(t, "Mask", new Vector3(0f, 1.56f, 0.13f), new Vector3(0.28f, 0.14f, 0.06f), Vector3.zero, dark);
            Ball(t, "EyeL", new Vector3(-0.07f, 1.63f, 0.13f), new Vector3(0.05f, 0.05f, 0.03f), Vector3.zero, Mat(EyeGlow, 0.8f));
            Ball(t, "EyeR", new Vector3(0.07f, 1.63f, 0.13f), new Vector3(0.05f, 0.05f, 0.03f), Vector3.zero, Mat(EyeGlow, 0.8f));
            // 匕首
            Box(t, "Dagger", new Vector3(0.46f, 0.92f, 0.10f), new Vector3(0.05f, 0.42f, 0.02f), new Vector3(0f, 0f, -18f), steel);
        }

        private static void BuildBoarKing(Transform t)
        {
            Material hide = Mat(new Color(0.34f, 0.26f, 0.22f));
            Material bristle = Mat(new Color(0.20f, 0.17f, 0.16f));
            Material tusk = Mat(new Color(0.92f, 0.89f, 0.80f));
            Material eye = Mat(EyeGlow, 1.8f);

            Ball(t, "Body", new Vector3(0f, 0.95f, 0f), new Vector3(1.05f, 0.95f, 1.35f), Vector3.zero, hide);
            Ball(t, "Hump", new Vector3(0f, 1.35f, -0.15f), new Vector3(0.80f, 0.55f, 0.90f), Vector3.zero, bristle);
            Ball(t, "Head", new Vector3(0f, 0.85f, 0.78f), new Vector3(0.62f, 0.58f, 0.55f), Vector3.zero, hide);
            Cone(t, "Snout", new Vector3(0f, 0.76f, 1.08f), new Vector3(0.42f, 0.42f, 0.42f), new Vector3(90f, 0f, 0f), hide);
            Ball(t, "Nose", new Vector3(0f, 0.76f, 1.26f), new Vector3(0.18f, 0.14f, 0.10f), Vector3.zero, bristle);
            // 獠牙
            Cone(t, "TuskL", new Vector3(-0.20f, 0.68f, 1.06f), new Vector3(0.11f, 0.36f, 0.11f), new Vector3(-40f, 0f, -20f), tusk);
            Cone(t, "TuskR", new Vector3(0.20f, 0.68f, 1.06f), new Vector3(0.11f, 0.36f, 0.11f), new Vector3(-40f, 0f, 20f), tusk);
            // 背脊尖刺
            for (int i = 0; i < 5; i++)
            {
                float z = -0.55f + i * 0.30f;
                Cone(t, "Spike" + i, new Vector3(0f, 1.55f - Mathf.Abs(i - 2) * 0.06f, z),
                    new Vector3(0.16f, 0.42f - Mathf.Abs(i - 2) * 0.05f, 0.16f), new Vector3(-12f, 0f, 0f), bristle);
            }
            Ball(t, "EyeL", new Vector3(-0.22f, 0.95f, 1.00f), new Vector3(0.09f, 0.08f, 0.06f), Vector3.zero, eye);
            Ball(t, "EyeR", new Vector3(0.22f, 0.95f, 1.00f), new Vector3(0.09f, 0.08f, 0.06f), Vector3.zero, eye);
            // 四腿
            Part(PrimitiveType.Capsule, t, "LegFL", new Vector3(-0.40f, 0.36f, 0.55f), new Vector3(0.20f, 0.26f, 0.20f), Vector3.zero, hide);
            Part(PrimitiveType.Capsule, t, "LegFR", new Vector3(0.40f, 0.36f, 0.55f), new Vector3(0.20f, 0.26f, 0.20f), Vector3.zero, hide);
            Part(PrimitiveType.Capsule, t, "LegBL", new Vector3(-0.40f, 0.36f, -0.55f), new Vector3(0.20f, 0.26f, 0.20f), Vector3.zero, hide);
            Part(PrimitiveType.Capsule, t, "LegBR", new Vector3(0.40f, 0.36f, -0.55f), new Vector3(0.20f, 0.26f, 0.20f), Vector3.zero, hide);
        }

        // ---------------------------------------------------------------- NPC

        public static void AttachNpcModel(GameObject root, bool merchant, string npcName)
        {
            if (root == null) return;
            HideLegacyRenderer(root);
            Transform old = root.transform.Find("Model");
            if (old != null) Object.Destroy(old.gameObject);

            GameObject model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            Material skin = Mat(new Color(0.88f, 0.74f, 0.62f));
            Material robe = merchant ? Mat(new Color(0.46f, 0.33f, 0.20f)) : Mat(new Color(0.40f, 0.42f, 0.46f));
            Material inner = Mat(merchant ? new Color(0.60f, 0.46f, 0.26f) : new Color(0.72f, 0.70f, 0.66f));
            Material hair = Mat(merchant ? new Color(0.20f, 0.16f, 0.14f) : new Color(0.86f, 0.86f, 0.84f));
            Material trim = Mat(GoldTrim, 0.15f);

            // 比例与玩家同一套（髋 0.92 / 肩 1.34 / 头心 1.60）；商人袍短露腿，老者袍长及地
            float hemR = merchant ? 0.40f : 0.46f;
            float hemY = merchant ? 0.34f : 0.02f;

            Cone(model.transform, "Robe", new Vector3(0f, hemY, 0f),
                new Vector3(hemR * 2f, (0.96f - hemY), hemR * 2f), Vector3.zero, robe);
            Tube(model.transform, "Torso", new Vector3(0f, 1.14f, 0f), new Vector3(0.42f, 0.28f, 0.36f), Vector3.zero, inner);
            Tube(model.transform, "Sash", new Vector3(0f, 0.93f, 0f), new Vector3(0.44f, 0.05f, 0.38f), Vector3.zero, trim);
            Ball(model.transform, "ShoulderL", new Vector3(-0.185f, 1.34f, 0f), new Vector3(0.22f, 0.16f, 0.22f), Vector3.zero, robe);
            Ball(model.transform, "ShoulderR", new Vector3(0.185f, 1.34f, 0f), new Vector3(0.22f, 0.16f, 0.22f), Vector3.zero, robe);

            // 手臂：老者前伸虚握（枢轴在肩，稍微前倾）
            float armTilt = merchant ? 6f : 34f;
            Transform armL = Pivot(model.transform, "ArmL", new Vector3(-0.205f, 1.34f, 0f), new Vector3(armTilt, 0f, 10f));
            Transform armR = Pivot(model.transform, "ArmR", new Vector3(0.205f, 1.34f, 0f), new Vector3(armTilt, 0f, -10f));
            Part(PrimitiveType.Capsule, armL, "ArmLMesh", new Vector3(0f, -0.20f, 0f), new Vector3(0.115f, 0.20f, 0.115f), Vector3.zero, robe);
            Part(PrimitiveType.Capsule, armR, "ArmRMesh", new Vector3(0f, -0.20f, 0f), new Vector3(0.115f, 0.20f, 0.115f), Vector3.zero, robe);
            Ball(armL, "HandL", new Vector3(0f, -0.42f, 0.02f), new Vector3(0.11f, 0.12f, 0.11f), Vector3.zero, skin);
            Ball(armR, "HandR", new Vector3(0f, -0.42f, 0.02f), new Vector3(0.11f, 0.12f, 0.11f), Vector3.zero, skin);

            if (merchant)
            {
                // 商人：露出双腿，走路时能看出在走
                Transform legL = Pivot(model.transform, "LegL", new Vector3(-0.105f, 0.88f, 0f));
                Transform legR = Pivot(model.transform, "LegR", new Vector3(0.105f, 0.88f, 0f));
                Part(PrimitiveType.Capsule, legL, "LegLMesh", new Vector3(0f, -0.30f, 0f), new Vector3(0.15f, 0.31f, 0.15f), Vector3.zero, Mat(new Color(0.26f, 0.22f, 0.20f)));
                Part(PrimitiveType.Capsule, legR, "LegRMesh", new Vector3(0f, -0.30f, 0f), new Vector3(0.15f, 0.31f, 0.15f), Vector3.zero, Mat(new Color(0.26f, 0.22f, 0.20f)));
                Box(model.transform, "BootL", new Vector3(-0.105f, 0.075f, 0.05f), new Vector3(0.16f, 0.15f, 0.26f), Vector3.zero, Mat(new Color(0.20f, 0.17f, 0.15f)));
                Box(model.transform, "BootR", new Vector3(0.105f, 0.075f, 0.05f), new Vector3(0.16f, 0.15f, 0.26f), Vector3.zero, Mat(new Color(0.20f, 0.17f, 0.15f)));
            }

            Tube(model.transform, "Neck", new Vector3(0f, 1.43f, 0f), new Vector3(0.13f, 0.04f, 0.13f), Vector3.zero, skin);
            Ball(model.transform, "Head", new Vector3(0f, 1.60f, 0f), new Vector3(0.30f, 0.32f, 0.29f), Vector3.zero, skin);
            Ball(model.transform, "Hair", new Vector3(0f, 1.635f, -0.03f), new Vector3(0.315f, 0.28f, 0.305f), Vector3.zero, hair);
            Box(model.transform, "Cap", new Vector3(0f, 1.725f, -0.02f), new Vector3(merchant ? 0.32f : 0.29f, merchant ? 0.09f : 0.055f, merchant ? 0.30f : 0.28f), Vector3.zero, merchant ? Mat(new Color(0.35f, 0.24f, 0.16f)) : hair);
            Ball(model.transform, "EyeL", new Vector3(-0.07f, 1.615f, 0.135f), new Vector3(0.05f, 0.055f, 0.03f), Vector3.zero, Mat(HairInk));
            Ball(model.transform, "EyeR", new Vector3(0.07f, 1.615f, 0.135f), new Vector3(0.05f, 0.055f, 0.03f), Vector3.zero, Mat(HairInk));

            if (merchant)
            {
                // 货担（背在身后，从正面也能看出轮廓）
                Box(model.transform, "Pack", new Vector3(0f, 1.26f, -0.30f), new Vector3(0.50f, 0.42f, 0.22f), Vector3.zero, Mat(new Color(0.44f, 0.30f, 0.18f)));
                Ball(model.transform, "PackTie", new Vector3(0f, 1.48f, -0.30f), new Vector3(0.23f, 0.06f, 0.25f), Vector3.zero, trim);
            }
            else
            {
                // 长须 + 手杖：一眼读出"长者 / 任务发布者"
                // 长须要比头发暗一档、且更窄，否则头 + 须 + 冠会糊成一整块白柱
                Cone(model.transform, "Beard", new Vector3(0f, 1.40f, 0.11f), new Vector3(0.15f, 0.36f, 0.14f),
                    new Vector3(180f, 0f, 0f), Mat(new Color(0.74f, 0.74f, 0.72f)));
                Box(model.transform, "Staff", new Vector3(0.30f, 0.78f, 0.26f), new Vector3(0.05f, 1.50f, 0.05f), new Vector3(3f, 0f, -3f), Mat(new Color(0.38f, 0.27f, 0.18f)));
                Ball(model.transform, "StaffOrb", new Vector3(0.33f, 1.54f, 0.26f), new Vector3(0.15f, 0.15f, 0.15f), Vector3.zero, Mat(new Color(1f, 0.82f, 0.42f), 2.2f));
            }

            CharacterTint tint = model.AddComponent<CharacterTint>();
            tint.Collect();
            model.AddComponent<CharacterMotion>();

            // 中立金：NPC 识别环（美术圣经 §3.5）
            Ring(model.transform, "NpcRing", new Vector3(0f, 0.04f, 0f), 1.6f, Mat(new Color(0.91f, 0.72f, 0.30f), 0.7f));

            CapsuleCollider col = root.GetComponent<CapsuleCollider>();
            if (col == null) col = root.AddComponent<CapsuleCollider>();
            col.height = 1.9f;
            col.radius = 0.45f;
            col.center = new Vector3(0f, 0.95f, 0f);
        }
    }
}
