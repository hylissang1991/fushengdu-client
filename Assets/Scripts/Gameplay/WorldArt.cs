// 《浮生渡》表现层 · 新手村世界生成（运行时）
// ---------------------------------------------------------------------------
// 为什么在"运行时"生成，而不是编辑器里烤进场景：
//   把上百棵树、六座房子、上千个草丛序列化进 .unity，会让场景文件涨到 10MB，
//   并且程序化网格必须以 .asset 形式被 GUID 引用 —— 只要资产库状态抖一下，
//   打包出来的 level0 就会坏（表现为启动即 "level0 is corrupted" + 崩溃）。
//   改成运行时生成后：场景里只有一个空 GameObject + 本组件，level0 只有几百 KB 且恒定有效。
//   附带好处：改美术只改 C#，不用重建场景，迭代快得多。
// 约束：本组件用 [DefaultExecutionOrder(-1000)] 提前执行，保证地面碰撞体在
//       玩家 / 怪物 / NPC 落位之前就已经存在。
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using Fushengdu.Core;
using UnityEngine;

namespace Fushengdu.Gameplay
{
    [DefaultExecutionOrder(-1000)]
    public sealed class WorldArt : MonoBehaviour
    {
        private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();

        // 世界尺寸
        private const float GroundSize = 260f;
        private const int GroundCells = 128;
        private const float VillageFlatRadius = 30f;

        // ---------------------------------------------------------------- 材质缓存
        private static Material _grass, _dirt, _stone, _wood, _plaster, _roof, _leafDark, _leafLight,
            _bark, _water, _rock, _mountain, _foliage, _lantern, _banner, _goldTrim, _plaza, _flame;

        // ================================================================ 入口
        private void Awake()
        {
            Build(transform);
        }

        /// <summary>就地生成整个新手村（挂在空 GameObject 上即可）。</summary>
        public static void Build(Transform artRoot)
        {
            EnsureTextures();
            EnsureMaterials();

            BuildSky(artRoot);
            BuildLighting();
            BuildGround(artRoot);
            BuildPaths(artRoot);
            BuildMountains(artRoot);
            BuildPond(artRoot);
            BuildVillage(artRoot);
            BuildVegetation(artRoot);
            BuildGrassField(artRoot);

            // 植被从 120 棵加到 310 棵（为了补边缘林带），不合批就是 1500+ 次 DrawCall。
            // 村落与植被全是静态物件，运行时合并成静态批，DrawCall 直接掉一个数量级。
            CombineStatic(artRoot, "Vegetation");
            CombineStatic(artRoot, "Village_Newcomer");
            StripDecorColliders(artRoot);
            AddNearbyMonsterSpawns(artRoot);
            ApplySpawnPoint();

            Debug.Log("[浮生渡] 新手村美术构建完成");
        }

        /// <summary>
        /// 【血泪教训 · 本次"能打开但玩不了"的真正根因】
        /// 装饰物件绝不能留碰撞体：GameObject.CreatePrimitive 会自带 Collider，
        /// 而广场石台这种 32 × 0.14 × 32 的 Cylinder，它的 CapsuleCollider 在被非等比缩放后会
        /// **退化成一颗半径 16m 的巨球**——出生在石台上的玩家会被 CharacterController
        /// 沿最近方向一把弹出 16m 并卡在球面上（实测：出生点 (0,1.6,-7) 半秒内被弹到 (0,3.8,-15.9) 后不再动）。
        /// 表现就是：人飞到村外 → 看不到自己 / 走不动 / 打不到怪 / 技能全拒 / 走不到 NPC 接任务。
        /// 因此这里统一收尾：只保留地面 MeshCollider（玩家落地、相机贴地、怪物落位都靠它）。
        /// </summary>
        private static void StripDecorColliders(Transform root)
        {
            var all = root.GetComponentsInChildren<Collider>(true);
            int removed = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Collider c = all[i];
                if (c == null) continue;
                if (string.Equals(c.gameObject.name, "Ground")) continue; // 地面碰撞保留
                Object.Destroy(c);
                removed++;
            }
            if (removed > 0)
                Debug.Log("[浮生渡] 已剔除装饰物碰撞体 " + removed + " 个（仅保留地面 MeshCollider）");
        }

        /// <summary>
        /// 村口近处刷怪点。场景里原有的 4 组刷怪点全在村外 45~70m，玩家出生后要走半分钟
        /// 才见到第一只怪 —— 对于"这游戏能不能玩"的第一印象是致命的。
        /// 这里在 24m 左右补两组灰狼：看得见、走两步就能打，但不会一出生就贴脸。
        /// （同样不写进场景资产，避免重建场景触发打包 level0 截断的坑。）
        /// </summary>
        private static void AddNearbyMonsterSpawns(Transform parent)
        {
            var root = new GameObject("SpawnPoints_NearVillage");
            root.transform.SetParent(parent, false);
            CreateSpawnPoint(root.transform, 11f, 15f, 1, 3, 4f);
            CreateSpawnPoint(root.transform, -14f, 13f, 1, 2, 3f);
        }

        private static void CreateSpawnPoint(Transform parent, float x, float z, int defId, int count, float scatter)
        {
            var go = new GameObject("SpawnPoint_near_" + defId);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, GroundHeight(x, z), z);
            var sp = go.AddComponent<MonsterSpawnPoint>();
            sp.MonsterDefId = defId;
            sp.Count = count;
            sp.ScatterRadius = scatter;
        }

        /// <summary>出生点 Z（广场北侧路口）。</summary>
        private const float SpawnZ = -7f;

        /// <summary>
        /// 把出生点挪到广场北路口。
        /// 相机是"背后跟随、朝 -Z 看"，出生在广场中心 (0,0) 会和渡字石碑重叠，
        /// 而站在北路口时第一帧是「人物背影 → 石碑 → 广场 → 南侧村舍与远山」，才是正常的开场取景。
        /// 【为什么写在运行时】场景资产每改一次都要重建场景，而 batchmode 重建后的打包有一定概率
        /// 产出被截断 32 字节的 level0（播放器直接 "level0 is corrupted" 崩溃）。运行时改位移零风险。
        /// </summary>
        private static void ApplySpawnPoint()
        {
            RespawnPointMarker marker = Object.FindObjectOfType<RespawnPointMarker>();
            if (marker != null)
            {
                float y = GroundHeight(0f, SpawnZ);
                marker.transform.position = new Vector3(0f, y + 1.6f, SpawnZ);
            }

            // 跟随相机也跟着收紧一点：默认 7.2/2.9/14° 在 16:9 下人物只占屏高 ~1/8，读不出动作。
            // 同样放运行时，避免为了改个相机参数去重建场景。
            CameraFollow rig = Object.FindObjectOfType<CameraFollow>();
            if (rig != null)
            {
                rig.Distance = 7.4f;
                rig.Height = 3.3f;
                rig.Pitch = 16f;
            }
        }

        private static void CombineStatic(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            if (child == null) return;
            try
            {
                StaticBatchingUtility.Combine(child.gameObject);
            }
            catch (System.Exception e)
            {
                // 合批失败只是掉帧，绝不能让整个新手村构建中断
                Debug.LogWarning("[浮生渡] 静态合批跳过（" + childName + "）：" + e.Message);
            }
        }

        /// <summary>地面高度（与 Ground 网格同一函数，供道具/怪物落位使用）。</summary>
        public static float GroundHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float hills = 1.05f * Mathf.Sin(x * 0.033f) * Mathf.Cos(z * 0.029f)
                        + 0.55f * Mathf.Sin(x * 0.082f + 1.3f) * Mathf.Sin(z * 0.071f - 0.4f)
                        + 0.22f * Mathf.Sin(x * 0.19f + 0.7f) * Mathf.Cos(z * 0.17f);
            float flatBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, VillageFlatRadius + 16f, r));
            // 边缘轻微抬升（不能太高：上一版做成 30 米高的碗壁，把地平线和天空全挡了）
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(100f, 128f, r)) * 6f;

            // 池塘洼地（否则水面会被地形挡住）
            float pd = Mathf.Sqrt((x + 54f) * (x + 54f) + (z - 38f) * (z - 38f));
            float pit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17f, 5f, pd)) * 3.4f;

            return hills * flatBlend + rim - pit;
        }

        // ================================================================ 资源

        // ---------------------------------------------------------------- 网格落盘
        // 【关键工程约束 · 血泪教训】
        // 所有程序化 Mesh 必须        // 直接把运行时 Mesh 塞进场景，Unity 会把整块顶点数据以 YAML 写进 .unity（本例一度 10MB），
        // 打包后播放器读 level0 直接报 "corrupted" 崩溃（exit code 3）。
        private static Mesh SaveMesh(Mesh mesh, string assetName)
        {
            if (assetName != null) MeshCache[assetName] = mesh;
            return mesh;
        }

        private static Mesh _coneAsset;

        /// <summary>共享圆锥网格（远山 / 树冠用）。</summary>
        private static Mesh ConeAsset
        {
            get
            {
                if (_coneAsset == null) _coneAsset = MakeConeMesh();
                return _coneAsset;
            }
        }

        /// <summary>圆锥：底面半径 0.5、高 1、顶点朝 +Y、含底盖。</summary>
        private static Mesh MakeConeMesh()
        {
            const int sides = 14;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, 1f, 0f));
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
            int center = verts.Count;
            verts.Add(Vector3.zero);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 1; i <= sides; i++)
            {
                tris.Add(center); tris.Add(i); tris.Add(i + 1);
            }

            var mesh = new Mesh { name = "M_Cone" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- 贴图

        private static void EnsureTextures()
        {
            MakeTexture("T_Grass", 256, true, (u, v) =>
            {
                float n = TileNoise(u, v, 6f) * 0.55f + TileNoise(u, v, 18f) * 0.3f + TileNoise(u, v, 42f) * 0.15f;
                // 草色要"有绿"且"有起伏"：偏灰的橄榄绿在光照 + 色调映射后会洗成米黄，
                // 明暗差太小则远看是一片死板的纯色
                Color baseC = new Color(0.32f, 0.45f, 0.20f);
                Color darkC = new Color(0.16f, 0.25f, 0.09f);
                Color lightC = new Color(0.50f, 0.63f, 0.28f);
                Color c = n < 0.48f ? Color.Lerp(darkC, baseC, n / 0.48f) : Color.Lerp(baseC, lightC, (n - 0.48f) / 0.52f);
                // 干草斑点
                float sp = TileNoise(u + 0.3f, v + 0.7f, 60f);
                if (sp > 0.86f) c = Color.Lerp(c, new Color(0.66f, 0.62f, 0.36f), 0.4f);
                return c;
            });

            MakeTexture("T_Dirt", 256, true, (u, v) =>
            {
                float n = TileNoise(u, v, 7f) * 0.6f + TileNoise(u, v, 26f) * 0.4f;
                Color c = Color.Lerp(new Color(0.36f, 0.29f, 0.21f), new Color(0.55f, 0.45f, 0.33f), n);
                float pebble = TileNoise(u + 0.5f, v + 0.2f, 70f);
                if (pebble > 0.82f) c = Color.Lerp(c, new Color(0.62f, 0.59f, 0.54f), 0.55f);
                return c;
            });

            MakeTexture("T_Stone", 256, true, (u, v) =>
            {
                float n = TileNoise(u, v, 5f) * 0.7f + TileNoise(u, v, 22f) * 0.3f;
                Color c = Color.Lerp(new Color(0.44f, 0.44f, 0.43f), new Color(0.66f, 0.65f, 0.62f), n);
                // 砖缝（每 1/4 一条横缝 + 交错竖缝）
                float row = v * 4f;
                float rowFrac = row - Mathf.Floor(row);
                float col = u * 4f + (Mathf.Floor(row) % 2 == 0 ? 0f : 0.5f);
                float colFrac = col - Mathf.Floor(col);
                if (rowFrac < 0.06f || colFrac < 0.04f) c *= 0.72f;
                return c;
            });

            MakeTexture("T_Wood", 128, false, 512, (u, v) =>
            {
                float grain = Mathf.Sin(u * 42f + TileNoise(u, v, 4f) * 7f) * 0.5f + 0.5f;
                Color c = Color.Lerp(new Color(0.30f, 0.21f, 0.14f), new Color(0.48f, 0.35f, 0.23f), grain);
                float knot = TileNoise(u, v, 9f);
                if (knot > 0.88f) c *= 0.7f;
                return c;
            });

            MakeTexture("T_Plaster", 128, true, (u, v) =>
            {
                float n = TileNoise(u, v, 8f) * 0.6f + TileNoise(u, v, 30f) * 0.4f;
                return Color.Lerp(new Color(0.76f, 0.73f, 0.66f), new Color(0.90f, 0.87f, 0.80f), n);
            });

            MakeTexture("T_Roof", 128, true, 512, (u, v) =>
            {
                // 瓦垄：竖向波纹 + 横向压边
                float ridge = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 10f));
                Color c = Color.Lerp(new Color(0.17f, 0.20f, 0.23f), new Color(0.32f, 0.36f, 0.39f), ridge);
                float row = v * 6f;
                float f = row - Mathf.Floor(row);
                if (f < 0.12f) c *= 0.72f;
                c *= 0.9f + TileNoise(u, v, 16f) * 0.2f;
                return c;
            });

            MakeTexture("T_GrassBlade", 64, false, 64, (u, v) =>
            {
                // 透明底 + 若干草叶（alpha clip 用）
                float alpha = 0f;
                for (int i = 0; i < 9; i++)
                {
                    float baseX = (i + 0.5f) / 9f;
                    float bend = (TileNoise(baseX, 0.5f, 3f) - 0.5f) * 0.22f;
                    float halfW = Mathf.Lerp(0.045f, 0.008f, v);
                    float center = baseX + bend * v;
                    if (v < 0.92f && Mathf.Abs(u - center) < halfW) alpha = 1f;
                }
                Color green = Color.Lerp(new Color(0.22f, 0.33f, 0.13f), new Color(0.40f, 0.52f, 0.22f), v);
                return new Color(green.r, green.g, green.b, alpha);
            }, true, false);

            MakeTexture("T_Water", 256, true, (u, v) =>
            {
                float n = TileNoise(u, v, 5f) * 0.6f + TileNoise(u, v, 15f) * 0.4f;
                return Color.Lerp(new Color(0.15f, 0.32f, 0.36f), new Color(0.30f, 0.52f, 0.55f), n);
            });
        }

        private static float TileNoise(float u, float v, float freq)
        {
            float a = Mathf.PerlinNoise(u * freq, v * freq);
            float b = Mathf.PerlinNoise((u - 1f) * freq, v * freq);
            float c = Mathf.PerlinNoise(u * freq, (v - 1f) * freq);
            float d = Mathf.PerlinNoise((u - 1f) * freq, (v - 1f) * freq);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static void MakeTexture(string name, int width, bool tiling, System.Func<float, float, Color> pixel)
        {
            MakeTexture(name, width, tiling, width, pixel, false);
        }

        private static void MakeTexture(string name, int width, bool tiling, int height,
            System.Func<float, float, Color> pixel, bool alphaIsTransparency = false, bool mipmap = true)
        {
            if (TexCache.ContainsKey(name)) return;

            // 草叶贴图必须关 mipmap：mip 会把 alpha 平均成半透明，alpha clip 于是在远处失效，
            // 一片草变成一堆实心方块 —— 就是上一版"满地白纸屑"的原因。
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipmap);
            var cols = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    cols[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
                }
            }
            tex.SetPixels(cols);
            tex.wrapMode = tiling ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            tex.Apply(true, false);
            TexCache[name] = tex;
        }

        private static Texture2D Tex(string name)
        {
            Texture2D t;
            return TexCache.TryGetValue(name, out t) ? t : null;
        }

        // ---------------------------------------------------------------- 材质

        private static void EnsureMaterials()
        {
            _grass = MakeMat("M_Grass", Tex("T_Grass"), new Color(1f, 1f, 1f), Vector2.one, false, true);
            _dirt = MakeMat("M_Dirt", Tex("T_Dirt"), new Color(1f, 1f, 1f), new Vector2(4f, 4f), false, false);
            _stone = MakeMat("M_Stone", Tex("T_Stone"), new Color(1f, 1f, 1f), new Vector2(3f, 3f), false, false);
            _plaza = MakeMat("M_Plaza", Tex("T_Stone"), new Color(0.92f, 0.90f, 0.85f), new Vector2(9f, 9f), false, false);
            _wood = MakeMat("M_Wood", Tex("T_Wood"), new Color(1f, 1f, 1f), new Vector2(1f, 2f), false, false);
            _plaster = MakeMat("M_Plaster", Tex("T_Plaster"), new Color(1f, 0.98f, 0.93f), new Vector2(3f, 2f), false, false);
            _roof = MakeMat("M_Roof", Tex("T_Roof"), new Color(1f, 1f, 1f), new Vector2(3f, 3f), false, false);
            _bark = MakeMat("M_Bark", Tex("T_Wood"), new Color(0.62f, 0.54f, 0.46f), new Vector2(1f, 1f), false, false);
            _leafDark = MakeMat("M_LeafDark", null, new Color(0.20f, 0.33f, 0.20f), Vector2.one, false, false);
            _leafLight = MakeMat("M_LeafLight", null, new Color(0.32f, 0.45f, 0.24f), Vector2.one, false, false);
            _water = MakeMat("M_Water", Tex("T_Water"), new Color(1f, 1f, 1f), new Vector2(6f, 6f), false, false, 0.25f);
            _rock = MakeMat("M_Rock", Tex("T_Stone"), new Color(0.72f, 0.72f, 0.70f), new Vector2(2f, 2f), false, false);
            _mountain = MakeMat("M_Mountain", null, new Color(0.36f, 0.42f, 0.48f), Vector2.one, false, false);
            // _foliage 必须开顶点色：草地网格现在每丛带明暗/色相偏移，关掉就退回"一片纯绿"
            _foliage = MakeMat("M_Foliage", Tex("T_GrassBlade"), Color.white, Vector2.one, true, true, 0f, true);
            // 裁剪阈值 0.5 → 0.58：远处 mip 会把叶缝的 alpha 平均上去，0.5 时叶子会糊成一整块绿板
            if (_foliage != null) _foliage.SetFloat("_Cutoff", 0.58f);
            // 灯笼：暖琥珀色纸面，自发光压到 0.9——原来 2.6 会被色调映射直接烧成纯白，
            // 远看就是"挂在杆子上的白气球"，这是旧版最出戏的一处。
            _lantern = MakeMat("M_Lantern", null, new Color(1.00f, 0.66f, 0.32f), Vector2.one, false, false, 0.9f);
            // 火焰专用：更橙、更亮，但同样不烧白
            _flame = MakeMat("M_Flame", null, new Color(1.00f, 0.52f, 0.16f), Vector2.one, false, false, 1.7f);
            // 幡旗：暗朱红，原来 (0.62,0.16,0.15) 在日光下亮成"红胶带"
            _banner = MakeMat("M_Banner", null, new Color(0.52f, 0.12f, 0.10f), Vector2.one, false, false, 0f, true);
            _goldTrim = MakeMat("M_GoldTrim", null, new Color(0.79f, 0.63f, 0.36f), Vector2.one, false, false, 0.35f);
        }

        private static Material MakeMat(string name, Texture2D tex, Color color, Vector2 tiling,
            bool alphaClip, bool vertexColor, float emission = 0f, bool doubleSided = false)
        {
            Shader shader = RuntimeArt.Stylized;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[浮生渡] 找不到 Fushengdu/Stylized，地形材质无法创建");
                return null;
            }

            var m = new Material(shader) { name = name, enableInstancing = true };
            m.SetColor("_Color", color);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", tiling);
            }
            m.SetFloat("_Emission", emission);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            if (alphaClip) m.EnableKeyword("_ALPHATEST_ON");
            else m.DisableKeyword("_ALPHATEST_ON");
            if (vertexColor) m.EnableKeyword("_VERTEXCOLOR_ON");
            else m.DisableKeyword("_VERTEXCOLOR_ON");
            return m;
        }

        // ================================================================ 天空与光照

        private static void BuildSky(Transform parent)
        {
            var skyGo = new GameObject("SkyDome");
            skyGo.transform.SetParent(parent, false);
            var mf = skyGo.AddComponent<MeshFilter>();
            var mr = skyGo.AddComponent<MeshRenderer>();

            const int rings = 28, segs = 48;
            const float radius = 420f;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            Color zenith = new Color(0.16f, 0.28f, 0.45f);
            Color mid = new Color(0.42f, 0.60f, 0.74f);
            Color horizon = new Color(0.93f, 0.87f, 0.75f);
            Color below = new Color(0.55f, 0.52f, 0.45f);

            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;          // 0=天顶 1=下方
                float phi = t * Mathf.PI;            // 0..180°
                float y = Mathf.Cos(phi) * radius;
                float ringR = Mathf.Sin(phi) * radius;
                Color c = t < 0.55f ? Color.Lerp(zenith, mid, t / 0.55f)
                        : t < 0.82f ? Color.Lerp(mid, horizon, (t - 0.55f) / 0.27f)
                        : Color.Lerp(horizon, below, (t - 0.82f) / 0.18f);

                for (int s = 0; s <= segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * ringR, y, Mathf.Sin(a) * ringR));
                    colors.Add(c);
                }
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    int i0 = r * (segs + 1) + s;
                    int i1 = i0 + 1;
                    int i2 = i0 + segs + 1;
                    int i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }

            var mesh = new Mesh { name = "M_SkyDome", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mf.sharedMesh = SaveMesh(mesh, "M_SkyDome");

            // 优先用 Resources 里预置的天空材质：保证 FDSky 着色器被打进包（Shader.Find 在
            // 打包后只找得到"确实被引用"的着色器，纯代码 Find 很容易拿到 null 变成粉红天空）
            Material skyMat = Resources.Load<Material>("Art/Mat_Sky");
            if (skyMat == null)
            {
                Shader skyShader = Shader.Find("Fushengdu/SkyDome");
                if (skyShader != null)
                {
                    skyMat = new Material(skyShader);
                    skyMat.SetColor("_Color", Color.white);
                }
            }
            if (skyMat != null) mr.sharedMaterial = skyMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 太阳：高亮球体 + 目标方向对齐
            var sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "SunDisc";
            sun.transform.SetParent(parent, false);
            Object.DestroyImmediate(sun.GetComponent<Collider>());
            float len = 340f;
            Vector3 dir = new Vector3(-0.42f, 0.62f, 0.66f).normalized;
            sun.transform.position = dir * len;
            sun.transform.localScale = Vector3.one * 26f;
            var sunMat = new Material(skyMatShaderOrDefault());
            sunMat.SetColor("_Color", new Color(1f, 0.94f, 0.80f));
            sunMat.SetFloat("_Emission", 3.2f);
            sun.GetComponent<Renderer>().sharedMaterial = sunMat;
            sun.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Shader skyMatShaderOrDefault()
        {
            Shader s = RuntimeArt.Stylized;
            if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
            return s;
        }

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.58f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.21f, 0.17f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0032f;
            // 雾色要跟地平线同色系，否则远处会出现一条突兀的"灰墙"
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.82f);

            // 主光：午后斜阳，软阴影（D-13 GTX1060 预算下仍可开）
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 1.12f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.72f;
            light.shadowBias = 0.04f;
            go.transform.rotation = Quaternion.Euler(46f, -38f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.sun = light;
        }

        // ================================================================ 地形

        private static void BuildGround(Transform parent)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);

            int cells = GroundCells;
            float half = GroundSize * 0.5f;
            float step = GroundSize / cells;

            var verts = new Vector3[(cells + 1) * (cells + 1)];
            var uvs = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            var tris = new int[cells * cells * 6];

            for (int z = 0; z <= cells; z++)
            {
                for (int x = 0; x <= cells; x++)
                {
                    float wx = -half + x * step;
                    float wz = -half + z * step;
                    float h = GroundHeight(wx, wz);
                    int i = z * (cells + 1) + x;
                    verts[i] = new Vector3(wx, h, wz);
                    // 贴图密度：一个 tile ≈ 2.6m。上一版是 0.35m/tile（UV 除 9 再乘材质 tiling 26），
                    // 远处直接糊成一层纯色，整片地形看起来"没有质感"，也是画面发平的主因之一。
                    uvs[i] = new Vector2(wx / 2.6f, wz / 2.6f);

                    // 顶点色：低洼更暗、高处偏亮；再接一层高频噪声和"干草斑"，
                    // 否则整片山坡就是一块均匀的荧光绿（这是画面"廉价"的主要来源之一）。
                    float n = Mathf.PerlinNoise((wx + 300f) * 0.045f, (wz + 300f) * 0.045f);
                    float n2 = Mathf.PerlinNoise((wx + 900f) * 0.17f, (wz + 900f) * 0.17f);
                    float tint = Mathf.Lerp(0.72f, 1.20f, n) * Mathf.Lerp(0.94f, 1.06f, n2);
                    float r = Mathf.Sqrt(wx * wx + wz * wz);
                    if (r < VillageFlatRadius + 6f) tint *= 1.05f;
                    Color lush = new Color(tint * 0.95f, tint * 1.04f, tint * 0.84f, 1f);
                    float dryRaw = Mathf.PerlinNoise((wx + 1700f) * 0.031f, (wz + 1700f) * 0.031f);
                    float dry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.80f, dryRaw)) * 0.75f;
                    Color hay = new Color(tint * 1.10f, tint * 1.00f, tint * 0.60f, 1f);
                    colors[i] = Color.Lerp(lush, hay, dry);
                }
            }

            int t = 0;
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int i0 = z * (cells + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + cells + 1;
                    int i3 = i2 + 1;
                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "M_Ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Mesh ground = SaveMesh(mesh, "M_Ground");
            go.AddComponent<MeshFilter>().sharedMesh = ground;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _grass;
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = ground;
            go.isStatic = true;
        }

        /// <summary>贴地土路：沿折线生成飘带网格，比"额外贴图"更贴合起伏地形。</summary>
        private static void BuildPaths(Transform parent)
        {
            var root = new GameObject("Paths");
            root.transform.SetParent(parent, false);

            var paths = new List<Vector3[]>
            {
                new[] { new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 26f), new Vector3(2f, 0f, 46f), new Vector3(-2f, 0f, 66f) },
                new[] { new Vector3(10f, 0f, 2f), new Vector3(26f, 0f, 0f), new Vector3(44f, 0f, -4f), new Vector3(62f, 0f, -8f) },
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(-28f, 0f, -4f), new Vector3(-48f, 0f, -2f) },
                new[] { new Vector3(0f, 0f, -10f), new Vector3(-2f, 0f, -28f), new Vector3(0f, 0f, -46f) }
            };

            int idx = 0;
            foreach (Vector3[] p in paths)
            {
                BuildRibbon(root.transform, "Path_" + (idx++), p, 4.6f, _dirt);
            }
        }

        private static void BuildRibbon(Transform parent, string name, Vector3[] points, float width, Material mat)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int stepsPerSegment = 8;

            var samples = new List<Vector3>();
            for (int i = 0; i < points.Length - 1; i++)
            {
                for (int s = 0; s < stepsPerSegment; s++)
                {
                    samples.Add(Vector3.Lerp(points[i], points[i + 1], s / (float)stepsPerSegment));
                }
            }
            samples.Add(points[points.Length - 1]);

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 fwd = i < samples.Count - 1 ? (samples[i + 1] - samples[i]) : (samples[i] - samples[i - 1]);
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 c = samples[i];
                float w = width * 0.5f * (1f + 0.12f * Mathf.Sin(i * 0.7f));

                Vector3 l = c - side * w;
                Vector3 r = c + side * w;
                l.y = GroundHeight(l.x, l.z) + 0.05f;
                r.y = GroundHeight(r.x, r.z) + 0.05f;
                verts.Add(l);
                verts.Add(r);
                uvs.Add(new Vector2(0f, i * 0.35f));
                uvs.Add(new Vector2(1f, i * 0.35f));
            }

            for (int i = 0; i < samples.Count - 1; i++)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            var mesh = new Mesh { name = "M_" + name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, "M_" + name);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void BuildMountains(Transform parent)
        {
            var root = new GameObject("Mountains");
            root.transform.SetParent(parent, false);

            var rng = new System.Random(20261021);
            for (int i = 0; i < 16; i++)
            {
                float ang = i / 16f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.12f;
                float dist = 128f + (float)rng.NextDouble() * 34f;
                float h = 34f + (float)rng.NextDouble() * 40f;
                float rad = 42f + (float)rng.NextDouble() * 30f;

                var go = new GameObject("Mountain_" + i);
                go.transform.SetParent(root.transform, false);
                float x = Mathf.Cos(ang) * dist;
                float z = Mathf.Sin(ang) * dist;
                go.transform.position = new Vector3(x, GroundHeight(x, z) - 6f, z);
                go.transform.localScale = new Vector3(rad, h, rad);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = ConeAsset;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mountain;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static void BuildPond(Transform parent)
        {
            var root = new GameObject("Pond");
            root.transform.SetParent(parent, false);
            Vector3 center = new Vector3(-54f, 0f, 38f);
            float y = GroundHeight(center.x, center.z) + 2.3f;

            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "WaterSurface";
            water.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.position = new Vector3(center.x, y, center.z);
            water.transform.localScale = new Vector3(26f, 0.12f, 22f);
            water.GetComponent<Renderer>().sharedMaterial = _water;

            // 岸边石块 + 芦苇
            var rng = new System.Random(77);
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                float rr = 1f + (float)rng.NextDouble() * 0.1f;
                Vector3 p = new Vector3(center.x + Mathf.Cos(a) * 13.6f * rr, 0f, center.z + Mathf.Sin(a) * 11.6f * rr);
                p.y = GroundHeight(p.x, p.z);
                RockCluster(root.transform, p, 0.9f + (float)rng.NextDouble() * 0.8f, rng);
            }
            for (int i = 0; i < 22; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 p = new Vector3(center.x + Mathf.Cos(a) * 13.2f, 0f, center.z + Mathf.Sin(a) * 11.2f);
                p.y = GroundHeight(p.x, p.z);
                for (int k = 0; k < 4; k++)
                {
                    var reed = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    reed.name = "Reed";
                    reed.transform.SetParent(root.transform, false);
                    Object.DestroyImmediate(reed.GetComponent<Collider>());
                    reed.transform.position = p + new Vector3((float)rng.NextDouble() * 1.2f - 0.6f, 0.7f, (float)rng.NextDouble() * 1.2f - 0.6f);
                    reed.transform.localScale = new Vector3(0.06f, 0.75f, 0.06f);
                    reed.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, 0f, (float)rng.NextDouble() * 10f - 5f);
                    reed.GetComponent<Renderer>().sharedMaterial = _leafLight;
                }
            }
        }

        // ================================================================ 村落

        private static void BuildVillage(Transform parent)
        {
            var root = new GameObject("Village_Newcomer");
            root.transform.SetParent(parent, false);
            float gy = GroundHeight(0f, 0f);

            // ---- 广场石台
            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plaza.name = "VillagePlaza";
            plaza.transform.SetParent(root.transform, false);
            plaza.transform.position = new Vector3(0f, gy + 0.06f, 0f);
            plaza.transform.localScale = new Vector3(32f, 0.14f, 32f);
            plaza.GetComponent<Renderer>().sharedMaterial = _plaza;

            // 广场内环（深色石圈，强化构图）
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "PlazaRing";
            ring.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.position = new Vector3(0f, gy + 0.16f, 0f);
            ring.transform.localScale = new Vector3(17f, 0.05f, 17f);
            ring.GetComponent<Renderer>().sharedMaterial = MakeMat("M_PlazaInner", Tex("T_Stone"),
                new Color(0.62f, 0.60f, 0.56f), new Vector2(6f, 6f), false, false);

            // ---- 中央石碑（渡）
            var steleRoot = new GameObject("Stele");
            steleRoot.transform.SetParent(root.transform, false);
            steleRoot.transform.position = new Vector3(0f, gy + 0.2f, 0f);
            Box(steleRoot.transform, "Base", new Vector3(0f, 0.3f, 0f), new Vector3(3.2f, 0.6f, 3.2f), _stone);
            Box(steleRoot.transform, "Base2", new Vector3(0f, 0.78f, 0f), new Vector3(2.4f, 0.4f, 2.4f), _stone);
            Box(steleRoot.transform, "Body", new Vector3(0f, 2.6f, 0f), new Vector3(1.1f, 3.4f, 0.7f), _stone);
            Box(steleRoot.transform, "Cap", new Vector3(0f, 4.42f, 0f), new Vector3(1.5f, 0.34f, 1.0f), _goldTrim);
            Box(steleRoot.transform, "Glyph", new Vector3(0f, 2.9f, 0.38f), new Vector3(0.55f, 1.5f, 0.06f), _lantern);

            // ---- 四座灯笼柱
            float lanternR = 12.5f;
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
                Vector3 p = new Vector3(Mathf.Cos(a) * lanternR, gy + 0.2f, Mathf.Sin(a) * lanternR);
                BuildLanternPost(root.transform, p, 3.4f);
            }

            // ---- 屋舍（6 座，环形排布，朝向广场）
            var houseDefs = new[]
            {
                (angle: 28f, radius: 25f, w: 9.5f, d: 7.5f, h: 4.0f),
                (angle: 96f, radius: 27f, w: 8.0f, d: 7.0f, h: 3.6f),
                (angle: 158f, radius: 24f, w: 11.0f, d: 8.0f, h: 4.4f),
                (angle: 208f, radius: 26f, w: 8.5f, d: 7.5f, h: 3.8f),
                (angle: 268f, radius: 25f, w: 10.0f, d: 8.0f, h: 4.2f),
                (angle: 322f, radius: 27f, w: 8.0f, d: 7.0f, h: 3.6f)
            };

            for (int i = 0; i < houseDefs.Length; i++)
            {
                var d = houseDefs[i];
                float rad = d.angle * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(rad) * d.radius, 0f, Mathf.Sin(rad) * d.radius);
                p.y = GroundHeight(p.x, p.z);
                float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; // 门（局部 -Z）朝向广场中心
                BuildHouse(root.transform, "House_" + (i + 1), p, yaw, d.w, d.d, d.h, i % 3 == 1);
            }

            // ---- 栅栏（环形，留四个门）
            BuildFence(root.transform, 33.5f, 4);

            // ---- 水井 + 木桶 + 货箱 + 幡旗
            BuildWell(root.transform, new Vector3(-9.5f, 0f, 8.5f));
            BuildBarrel(root.transform, new Vector3(7.5f, 0f, 9.5f));
            BuildBarrel(root.transform, new Vector3(8.6f, 0f, 8.2f));
            BuildCrate(root.transform, new Vector3(-7.8f, 0f, -9.0f));
            BuildBanner(root.transform, new Vector3(-14f, 0f, 12.5f), 12f);
            BuildBanner(root.transform, new Vector3(14.2f, 0f, 11.8f), -8f);

            // ---- 篝火（暖色点光，给村落一个视觉中心）
            BuildCampfire(root.transform, new Vector3(5.0f, 0f, -6.0f));
        }

        private static void BuildHouse(Transform parent, string name, Vector3 pos, float yaw, float w, float d, float h, bool bigLantern)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // 台基
            Box(root.transform, "Foundation", new Vector3(0f, 0.25f, 0f), new Vector3(w + 0.7f, 0.5f, d + 0.7f), _stone, true);
            // 夯土墙
            Box(root.transform, "Wall", new Vector3(0f, h * 0.5f + 0.45f, 0f), new Vector3(w, h, d), _plaster, true);
            // 木构：四角柱 + 上下横枋
            float hw = w * 0.5f, hd = d * 0.5f;
            Box(root.transform, "PillarFL", new Vector3(-hw, h * 0.5f + 0.45f, -hd), new Vector3(0.34f, h, 0.34f), _wood);
            Box(root.transform, "PillarFR", new Vector3(hw, h * 0.5f + 0.45f, -hd), new Vector3(0.34f, h, 0.34f), _wood);
            Box(root.transform, "PillarBL", new Vector3(-hw, h * 0.5f + 0.45f, hd), new Vector3(0.34f, h, 0.34f), _wood);
            Box(root.transform, "PillarBR", new Vector3(hw, h * 0.5f + 0.45f, hd), new Vector3(0.34f, h, 0.34f), _wood);
            Box(root.transform, "BeamTop", new Vector3(0f, h + 0.35f, -hd), new Vector3(w + 0.5f, 0.28f, 0.28f), _wood);
            Box(root.transform, "BeamTopB", new Vector3(0f, h + 0.35f, hd), new Vector3(w + 0.5f, 0.28f, 0.28f), _wood);
            Box(root.transform, "BeamMid", new Vector3(0f, h * 0.62f, -hd - 0.02f), new Vector3(w + 0.2f, 0.18f, 0.16f), _wood);

            // 门
            Box(root.transform, "Door", new Vector3(0f, 1.45f, -hd - 0.08f), new Vector3(1.5f, 2.3f, 0.12f),
                MakeMat("M_Door", Tex("T_Wood"), new Color(0.30f, 0.22f, 0.16f), new Vector2(1f, 1f), false, false));
            Box(root.transform, "DoorFrameL", new Vector3(-0.85f, 1.45f, -hd - 0.1f), new Vector3(0.16f, 2.5f, 0.16f), _wood);
            Box(root.transform, "DoorFrameR", new Vector3(0.85f, 1.45f, -hd - 0.1f), new Vector3(0.16f, 2.5f, 0.16f), _wood);
            Box(root.transform, "DoorLintel", new Vector3(0f, 2.72f, -hd - 0.1f), new Vector3(1.9f, 0.22f, 0.18f), _wood);

            // 窗（暖光：夜里也能读出"有人住"）
            BuildWindow(root.transform, new Vector3(-hw + 1.6f, h * 0.62f + 0.45f, -hd - 0.06f), 1.5f, 1.1f);
            BuildWindow(root.transform, new Vector3(hw - 1.6f, h * 0.62f + 0.45f, -hd - 0.06f), 1.5f, 1.1f);
            BuildWindow(root.transform, new Vector3(hw + 0.06f, h * 0.62f + 0.45f, 0f), 1.2f, 1.0f, 90f);

            // 歇山瓦顶（含出檐）
            GableRoof(root.transform, "M_Roof_" + name, new Vector3(0f, h + 0.5f, 0f), w + 2.0f, d + 2.0f, 2.0f);
            Box(root.transform, "Ridge", new Vector3(0f, h + 2.55f, 0f), new Vector3(w + 2.2f, 0.24f, 0.32f), _roof);
            // 檐口压边
            Box(root.transform, "EaveF", new Vector3(0f, h + 0.52f, -hd - 1.0f), new Vector3(w + 2.2f, 0.18f, 0.3f), _roof);
            Box(root.transform, "EaveB", new Vector3(0f, h + 0.52f, hd + 1.0f), new Vector3(w + 2.2f, 0.18f, 0.3f), _roof);

            // 灯笼（屋檐下）
            BuildHangingLantern(root.transform, new Vector3(-hw + 0.6f, h + 0.2f, -hd - 0.7f));
            if (bigLantern) BuildHangingLantern(root.transform, new Vector3(hw - 0.6f, h + 0.2f, -hd - 0.7f));
        }

        private static void BuildWindow(Transform parent, Vector3 pos, float w, float h, float yaw = 0f)
        {
            var root = new GameObject("Window");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Box(root.transform, "Glass", Vector3.zero, new Vector3(w, h, 0.08f), MakeMat("M_WindowGlow", null,
                new Color(1.0f, 0.78f, 0.45f), Vector2.one, false, false, 1.5f));
            Box(root.transform, "BarV", new Vector3(0f, 0f, 0.06f), new Vector3(0.07f, h, 0.05f), _wood);
            Box(root.transform, "BarH", new Vector3(0f, 0f, 0.06f), new Vector3(w, 0.07f, 0.05f), _wood);
            Box(root.transform, "FrameT", new Vector3(0f, h * 0.5f + 0.06f, 0.04f), new Vector3(w + 0.2f, 0.12f, 0.1f), _wood);
            Box(root.transform, "FrameB", new Vector3(0f, -h * 0.5f - 0.06f, 0.04f), new Vector3(w + 0.2f, 0.12f, 0.1f), _wood);
        }

        private static void BuildHangingLantern(Transform parent, Vector3 pos)
        {
            var root = new GameObject("Lantern");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            Box(root.transform, "Rope", new Vector3(0f, -0.22f, 0f), new Vector3(0.035f, 0.44f, 0.035f), _wood);

            // 灯身：矮圆柱（直径 0.40m、高 0.42m）。
            // 旧版是一个 0.5m 的光滑球体 + 2.6 自发光 = 白气球；换成柱身 + 竖棱 + 宽盖，才读得出"纸灯笼"。
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.localPosition = new Vector3(0f, -0.72f, 0f);
            body.transform.localScale = new Vector3(0.40f, 0.21f, 0.40f);
            body.GetComponent<Renderer>().sharedMaterial = _lantern;

            // 八根竖棱：木条露出灯身 3mm，破坏"光滑球"的轮廓
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Box(root.transform, "Rib" + i,
                    new Vector3(Mathf.Cos(a) * 0.197f, -0.72f, Mathf.Sin(a) * 0.197f),
                    new Vector3(0.026f, 0.425f, 0.026f), _wood);
            }

            // 上下木盖：必须比灯身宽，否则看着像"球上扣了个小碟子"
            Box(root.transform, "CapT", new Vector3(0f, -0.50f, 0f), new Vector3(0.31f, 0.06f, 0.31f), _wood);
            Box(root.transform, "CapB", new Vector3(0f, -0.945f, 0f), new Vector3(0.26f, 0.05f, 0.26f), _wood);
            Box(root.transform, "Tassel", new Vector3(0f, -1.055f, 0f), new Vector3(0.045f, 0.17f, 0.045f), _banner);
        }

        private static void BuildLanternPost(Transform parent, Vector3 pos, float height)
        {
            var root = new GameObject("LanternPost");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            Box(root.transform, "Post", new Vector3(0f, height * 0.5f, 0f), new Vector3(0.22f, height, 0.22f), _wood);
            Box(root.transform, "Base", new Vector3(0f, 0.12f, 0f), new Vector3(0.62f, 0.24f, 0.62f), _stone);
            Box(root.transform, "Arm", new Vector3(0.3f, height - 0.2f, 0f), new Vector3(0.7f, 0.12f, 0.12f), _wood);
            BuildHangingLantern(root.transform, new Vector3(0.56f, height - 0.25f, 0f));
        }

        private static void BuildWell(Transform parent, Vector3 pos)
        {
            float y = GroundHeight(pos.x, pos.z);
            var root = new GameObject("Well");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(pos.x, y, pos.z);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            ring.transform.localScale = new Vector3(2.4f, 0.45f, 2.4f);
            ring.GetComponent<Renderer>().sharedMaterial = _stone;

            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "Water";
            water.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            water.transform.localScale = new Vector3(2.0f, 0.02f, 2.0f);
            water.GetComponent<Renderer>().sharedMaterial = _water;

            Box(root.transform, "PostL", new Vector3(-1.1f, 1.35f, 0f), new Vector3(0.18f, 1.8f, 0.18f), _wood);
            Box(root.transform, "PostR", new Vector3(1.1f, 1.35f, 0f), new Vector3(0.18f, 1.8f, 0.18f), _wood);
            Box(root.transform, "Beam", new Vector3(0f, 2.2f, 0f), new Vector3(2.6f, 0.16f, 0.16f), _wood);
            GableRoof(root.transform, "M_Roof_Well", new Vector3(0f, 2.3f, 0f), 3.0f, 2.6f, 0.8f);
            Box(root.transform, "Bucket", new Vector3(0.2f, 1.05f, 0f), new Vector3(0.4f, 0.4f, 0.4f), _wood);
        }

        private static void BuildBarrel(Transform parent, Vector3 pos)
        {
            float y = GroundHeight(pos.x, pos.z);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Barrel";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, y + 0.55f, pos.z);
            go.transform.localScale = new Vector3(0.85f, 0.55f, 0.85f);
            go.GetComponent<Renderer>().sharedMaterial = _wood;
            var hoop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hoop.name = "Hoop";
            hoop.transform.SetParent(go.transform, false);
            Object.DestroyImmediate(hoop.GetComponent<Collider>());
            hoop.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            hoop.transform.localScale = new Vector3(1.04f, 0.06f, 1.04f);
            hoop.GetComponent<Renderer>().sharedMaterial = _goldTrim;
        }

        private static void BuildCrate(Transform parent, Vector3 pos)
        {
            float y = GroundHeight(pos.x, pos.z);
            var root = new GameObject("Crate");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(pos.x, y, pos.z);
            root.transform.rotation = Quaternion.Euler(0f, 18f, 0f);
            Box(root.transform, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(1.0f, 0.9f, 1.0f), _wood);
            Box(root.transform, "Band", new Vector3(0f, 0.45f, 0f), new Vector3(1.04f, 0.12f, 1.04f), _goldTrim);
        }

        private static void BuildBanner(Transform parent, Vector3 pos, float yaw)
        {
            float y = GroundHeight(pos.x, pos.z);
            var root = new GameObject("Banner");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(pos.x, y, pos.z);
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Box(root.transform, "Pole", new Vector3(0f, 3.0f, 0f), new Vector3(0.18f, 6.0f, 0.18f), _wood);
            Box(root.transform, "Cross", new Vector3(0.62f, 5.45f, 0f), new Vector3(1.35f, 0.12f, 0.12f), _wood);
            // 幡面：窄长竖幡，上下横杆 + 中缝暗金条 + 底边流苏
            // （旧版是 1.2×2.6 的纯红大平板，无绑扎无纹样，远看就是"贴了张红胶带"）
            Box(root.transform, "Cloth", new Vector3(0.86f, 4.32f, 0f), new Vector3(0.78f, 2.05f, 0.045f), _banner);
            Box(root.transform, "ClothStripe", new Vector3(0.86f, 4.32f, -0.035f), new Vector3(0.16f, 1.85f, 0.02f), _goldTrim);
            Box(root.transform, "RodTop", new Vector3(0.86f, 5.36f, 0f), new Vector3(0.94f, 0.07f, 0.07f), _wood);
            Box(root.transform, "RodBottom", new Vector3(0.86f, 3.27f, 0f), new Vector3(0.92f, 0.07f, 0.07f), _wood);
            for (int i = 0; i < 3; i++)
                Box(root.transform, "Hem" + i, new Vector3(0.62f + i * 0.24f, 3.19f, 0f),
                    new Vector3(0.05f, 0.14f, 0.05f), _goldTrim);
            Ball(root.transform, "Finial", new Vector3(0f, 6.1f, 0f), new Vector3(0.26f, 0.26f, 0.26f), _goldTrim);
        }

        private static void BuildCampfire(Transform parent, Vector3 pos)
        {
            float y = GroundHeight(pos.x, pos.z);
            var root = new GameObject("Campfire");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(pos.x, y, pos.z);

            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f;
                var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stone.name = "Stone" + i;
                stone.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(stone.GetComponent<Collider>());
                stone.transform.localPosition = new Vector3(Mathf.Cos(a) * 1.1f, 0.14f, Mathf.Sin(a) * 1.1f);
                stone.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                stone.transform.localScale = new Vector3(0.55f, 0.32f, 0.4f);
                stone.GetComponent<Renderer>().sharedMaterial = _rock;
            }
            for (int i = 0; i < 4; i++)
            {
                Box(root.transform, "Log" + i, new Vector3(0f, 0.18f + i * 0.06f, 0f),
                    new Vector3(0.18f, 0.18f, 1.5f), _wood);
            }
            // 火焰：外橙内黄的双层锥体。
            // 旧版是一个 0.75×0.95 的自发光球——在柴堆上悬着一个白蛋，是最出戏的一处。
            AddCone(root.transform, "FlameOuter", new Vector3(0f, 0.26f, 0f), new Vector3(0.62f, 1.12f, 0.62f), _flame);
            AddCone(root.transform, "FlameInner", new Vector3(0.05f, 0.28f, 0.04f), new Vector3(0.34f, 0.74f, 0.34f), _lantern);
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.62f, 0.32f);
            l.range = 14f;
            l.intensity = 3.2f;
            l.shadows = LightShadows.None;
        }

        private static void BuildFence(Transform parent, float radius, int gates)
        {
            var root = new GameObject("Fence");
            root.transform.SetParent(parent, false);
            int segments = 64;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                float mid = (a0 + a1) * 0.5f;
                // 四个方向留门（±30° 缺口）
                bool nearGate = false;
                for (int g = 0; g < gates; g++)
                {
                    float gateAngle = g * Mathf.PI * 0.5f;
                    if (Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, gateAngle * Mathf.Rad2Deg)) < 22f) nearGate = true;
                }
                if (nearGate) continue;

                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                p0.y = GroundHeight(p0.x, p0.z);
                p1.y = GroundHeight(p1.x, p1.z);
                Vector3 midP = (p0 + p1) * 0.5f;
                float len = Vector3.Distance(p0, p1);
                float yaw = Mathf.Atan2(p1.x - p0.x, p1.z - p0.z) * Mathf.Rad2Deg;

                var seg = new GameObject("FenceSeg_" + i);
                seg.transform.SetParent(root.transform, false);
                seg.transform.position = midP;
                seg.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Box(seg.transform, "Post", new Vector3(0f, 0.75f, 0f), new Vector3(0.2f, 1.5f, 0.2f), _wood);
                Box(seg.transform, "RailTop", new Vector3(0f, 1.05f, 0f), new Vector3(0.14f, 0.14f, len), _wood);
                Box(seg.transform, "RailBot", new Vector3(0f, 0.62f, 0f), new Vector3(0.12f, 0.12f, len), _wood);
            }
        }

        // ================================================================ 植被

        private static void BuildVegetation(Transform parent)
        {
            var root = new GameObject("Vegetation");
            root.transform.SetParent(parent, false);
            var rng = new System.Random(20261021);

            // 环状分布：村子外围成林，远处更密（挡住地形边缘）
            for (int i = 0; i < 120; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = 38f + (float)rng.NextDouble() * 62f;
                float x = Mathf.Cos(a) * dist;
                float z = Mathf.Sin(a) * dist;
                // 让开土路
                if (Mathf.Abs(x) < 6f || Mathf.Abs(z) < 6f) continue;
                float y = GroundHeight(x, z);
                if (y > 22f) continue; // 太高的山脊不放树

                float scale = 0.8f + (float)rng.NextDouble() * 0.75f;
                bool pine = rng.NextDouble() < 0.55;
                BuildTree(root.transform, new Vector3(x, y, z), scale, pine, rng);
            }

            // 边缘林带：地形在半径 128m 处收口，光靠"抬起来的绿色碗沿"挡不住接缝，
            // 远看就是一圈纯色绿墙。沿 88~126m 补一圈树林，既遮住地形边缘，
            // 又把地平线从"绿墙"变成"有层次的树线"。
            for (int i = 0; i < 190; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = 88f + (float)rng.NextDouble() * 38f;
                float x = Mathf.Cos(a) * dist;
                float z = Mathf.Sin(a) * dist;
                if (Mathf.Abs(x) < 6f || Mathf.Abs(z) < 6f) continue;
                float y = GroundHeight(x, z);
                float scale = 1.0f + (float)rng.NextDouble() * 0.9f;
                BuildTree(root.transform, new Vector3(x, y, z), scale, rng.NextDouble() < 0.72, rng);
            }

            // 岩石
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = 34f + (float)rng.NextDouble() * 66f;
                float x = Mathf.Cos(a) * dist;
                float z = Mathf.Sin(a) * dist;
                if (Mathf.Abs(x) < 5f || Mathf.Abs(z) < 5f) continue;
                RockCluster(root.transform, new Vector3(x, GroundHeight(x, z), z), 0.8f + (float)rng.NextDouble() * 1.6f, rng);
            }

            // 灌木（村子近处也要有，避免"空场"）
            for (int i = 0; i < 70; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = 30f + (float)rng.NextDouble() * 50f;
                float x = Mathf.Cos(a) * dist;
                float z = Mathf.Sin(a) * dist;
                if (Mathf.Abs(x) < 4f || Mathf.Abs(z) < 4f) continue;
                float y = GroundHeight(x, z);
                float s = 0.7f + (float)rng.NextDouble() * 0.9f;
                Bush(root.transform, new Vector3(x, y, z), s, rng);
            }
        }

        private static void BuildTree(Transform parent, Vector3 pos, float scale, bool pine, System.Random rng)
        {
            var root = new GameObject(pine ? "Pine" : "Tree");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            root.transform.localScale = Vector3.one * scale;

            // 树干
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            trunk.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            trunk.transform.localScale = new Vector3(0.42f, 2.0f, 0.42f);
            trunk.GetComponent<Renderer>().sharedMaterial = _bark;

            if (pine)
            {
                for (int i = 0; i < 4; i++)
                {
                    float h = 2.6f + i * 1.15f;
                    float r = 2.1f - i * 0.42f;
                    var cone = new GameObject("Canopy" + i);
                    cone.transform.SetParent(root.transform, false);
                    cone.transform.localPosition = new Vector3(0f, h, 0f);
                    cone.transform.localScale = new Vector3(r * 2f, 2.4f, r * 2f);
                    cone.AddComponent<MeshFilter>().sharedMesh = ConeAsset;
                    cone.AddComponent<MeshRenderer>().sharedMaterial = _leafDark;
                }
            }
            else
            {
                Ball(root.transform, "CanopyA", new Vector3(0f, 4.3f, 0f), new Vector3(3.4f, 2.7f, 3.4f), _leafLight);
                Ball(root.transform, "CanopyB", new Vector3(0.5f, 5.2f, -0.3f), new Vector3(2.3f, 2.0f, 2.3f), _leafDark);
                Ball(root.transform, "CanopyC", new Vector3(-0.7f, 4.9f, 0.5f), new Vector3(1.9f, 1.7f, 1.9f), _leafLight);
            }
        }

        private static void Bush(Transform parent, Vector3 pos, float scale, System.Random rng)
        {
            var root = new GameObject("Bush");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            root.transform.localScale = Vector3.one * scale;
            Ball(root.transform, "B1", new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 1.1f, 1.5f), _leafDark);
            Ball(root.transform, "B2", new Vector3(0.6f, 0.75f, 0.2f), new Vector3(1.0f, 0.9f, 1.0f), _leafLight);
            Ball(root.transform, "B3", new Vector3(-0.55f, 0.6f, -0.3f), new Vector3(0.9f, 0.8f, 0.9f), _leafDark);
        }

        private static void RockCluster(Transform parent, Vector3 pos, float scale, System.Random rng)
        {
            var root = new GameObject("Rock");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            root.transform.localScale = Vector3.one * scale;

            int count = 2 + rng.Next(2);
            for (int i = 0; i < count; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "R" + i;
                rock.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(rock.GetComponent<Collider>());
                float s = 1.1f + (float)rng.NextDouble() * 0.9f;
                rock.transform.localPosition = new Vector3((float)rng.NextDouble() * 1.2f - 0.6f, s * 0.36f,
                    (float)rng.NextDouble() * 1.2f - 0.6f);
                rock.transform.localScale = new Vector3(s * 1.3f, s * 0.85f, s * 1.15f);
                rock.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f);
                rock.GetComponent<Renderer>().sharedMaterial = _rock;
            }
        }

        /// <summary>草丛：全部合并成一个网格（一次 draw call），用 alpha clip 的交叉面片。</summary>
        private static void BuildGrassField(Transform parent)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            var rng = new System.Random(4242);

            // 【为什么是 7000 丛】上一版 1800 丛摊在半径 92m 的圆里 ≈ 0.07 丛/m²，
            // 镜头前一屏看不到两撮草，地面读起来是一张纯色塑料布。
            // 现在把 70% 撒在村子周边 17~55m（玩家 90% 时间待的地方），30% 撒到 95m 远景，
            // 单丛也放大到 0.42~0.86，近景才有"草"的量感。
            for (int i = 0; i < 7000; i++)
            {
                bool near = i % 10 < 7;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = near
                    ? 15f + (float)rng.NextDouble() * 40f
                    : 50f + (float)rng.NextDouble() * 46f;
                float x = Mathf.Cos(a) * dist;
                float z = Mathf.Sin(a) * dist;
                if (Mathf.Abs(x) < 3.5f || Mathf.Abs(z) < 3.5f) continue;
                if (dist < 17f) continue; // 广场上不长草
                float y = GroundHeight(x, z);
                float s = 0.42f + (float)rng.NextDouble() * 0.44f;
                float yaw = (float)rng.NextDouble() * 180f;
                // 每丛一个色相偏移：没有它，7000 丛会被读成"一片绿色塑料片"
                float v = 0.72f + (float)rng.NextDouble() * 0.30f;
                var tint = new Color(v * 0.98f, v * 1.04f, v * 0.80f, 1f);
                // 每丛一个倾角：法线不再全是 +Y，阳光下才有明暗层次（否则整片草是同一个亮度）
                float lean = ((float)rng.NextDouble() - 0.5f) * 0.62f;
                AddTuft(verts, uvs, normals, colors, tris, new Vector3(x, y, z), s, yaw, tint, lean);
            }

            var mesh = new Mesh { name = "M_GrassField", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("GrassField");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, "M_GrassField");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _foliage;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void AddTuft(List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals, List<Color> colors,
            List<int> tris, Vector3 pos, float scale, float yaw, Color tint, float lean)
        {
            for (int q = 0; q < 2; q++)
            {
                float a = (yaw + q * 90f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.55f * scale;
                // 丛身向本丛的朝向倒 lean 弧度：法线跟着偏，光照才有变化（全竖直 → 整片草一个亮度）
                Vector3 dirN = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 up = (Vector3.up * Mathf.Cos(lean) + dirN * Mathf.Sin(lean)) * (0.95f * scale);
                Vector3 nrm = (Vector3.up * Mathf.Cos(lean) + dirN * Mathf.Sin(lean)).normalized;
                int b = verts.Count;
                verts.Add(pos - dir);
                verts.Add(pos - dir + up);
                verts.Add(pos + dir + up);
                verts.Add(pos + dir);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                // 根部压暗、梢部提亮：单丛内部也有明暗，才不会像贴纸
                colors.Add(new Color(tint.r * 0.72f, tint.g * 0.74f, tint.b * 0.70f, 1f));
                colors.Add(tint);
                colors.Add(tint);
                colors.Add(new Color(tint.r * 0.72f, tint.g * 0.74f, tint.b * 0.70f, 1f));
                for (int n = 0; n < 4; n++) normals.Add(nrm);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // ================================================================ 基础几何工具

        private static void Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void Ball(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>圆锥实体（底面半径 0.5、高 1、顶点朝 +Y），无碰撞体。篝火火焰用它拼。</summary>
        private static void AddCone(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = ConeAsset;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>双坡屋顶（屋脊沿 X 轴）。每个面独立顶点，保证瓦片 UV 不被共享顶点拉扯。</summary>
        private static void GableRoof(Transform parent, string assetName, Vector3 localPos, float width, float depth, float height)
        {
            float w = width * 0.5f, d = depth * 0.5f, h = height;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // 左坡：A(-w,0,-d) → D(-w,0,d) → R1(0,h,d) → R2(0,h,-d)
            int b0 = verts.Count;
            verts.Add(new Vector3(-w, 0f, -d)); verts.Add(new Vector3(-w, 0f, d));
            verts.Add(new Vector3(0f, h, d)); verts.Add(new Vector3(0f, h, -d));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
            tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2);
            tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 3);

            // 右坡：B(w,0,-d) → R2 → R1 → C(w,0,d)
            int b1 = verts.Count;
            verts.Add(new Vector3(w, 0f, -d)); verts.Add(new Vector3(0f, h, -d));
            verts.Add(new Vector3(0f, h, d)); verts.Add(new Vector3(w, 0f, d));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            tris.Add(b1); tris.Add(b1 + 1); tris.Add(b1 + 2);
            tris.Add(b1); tris.Add(b1 + 2); tris.Add(b1 + 3);

            // 前山墙（-Z）：A → R2 → B
            int b2 = verts.Count;
            verts.Add(new Vector3(-w, 0f, -d)); verts.Add(new Vector3(0f, h, -d)); verts.Add(new Vector3(w, 0f, -d));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(1f, 0f));
            tris.Add(b2); tris.Add(b2 + 1); tris.Add(b2 + 2);

            // 后山墙（+Z）：D → C → R1
            int b3 = verts.Count;
            verts.Add(new Vector3(-w, 0f, d)); verts.Add(new Vector3(w, 0f, d)); verts.Add(new Vector3(0f, h, d));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0.5f, 1f));
            tris.Add(b3); tris.Add(b3 + 1); tris.Add(b3 + 2);

            // 底面（从屋内抬头能看到封板，避免漏光）
            int b4 = verts.Count;
            verts.Add(new Vector3(-w, 0f, -d)); verts.Add(new Vector3(w, 0f, -d));
            verts.Add(new Vector3(w, 0f, d)); verts.Add(new Vector3(-w, 0f, d));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            tris.Add(b4); tris.Add(b4 + 2); tris.Add(b4 + 1);
            tris.Add(b4); tris.Add(b4 + 3); tris.Add(b4 + 2);

            var mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Roof");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, assetName);
            go.AddComponent<MeshRenderer>().sharedMaterial = _roof;
        }
    }
}
