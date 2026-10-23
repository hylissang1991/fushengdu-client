// 《浮生渡》编辑器工具 · 程序化构建垂直切片场景与预制体
// 要求：不手写 .unity / .prefab 的 YAML，全部用 Editor 脚本生成。
// @trace D-02（Unity + URP）、ADR-001 §4.1（L0 客户端 / L2 服务器分层）
// 用法：Unity 菜单「浮生渡 → 3. 全部执行（构建并保存）」。
//
// 【重要变更】场景观感由运行时组件 WorldArt 负责生成（地形 / 天空 / 村落 / 植被 / 光照）。
// 场景里只放一个空 GameObject + WorldArt，避免把上千个程序化物体序列化进 .unity（那是 level0 corrupted 的根因）。
// 本文件只负责「玩法对象」：NPC、采集点、刷怪点、复活点、战斗世界、网络、UI、相机。
// 角色 / 怪物 / NPC 的可见模型在运行时由 RuntimeArt 拼装（不进场景，避免把运行时材质写进资源）。
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using Fushengdu.Core;
using Fushengdu.Gameplay;
using Fushengdu.Network.FishNet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Fushengdu.Editor
{
    public static class VerticalSliceBuilder
    {
        private const string SceneDir = "Assets/Scenes";
        private const string PrefabDir = "Assets/Prefabs";
        private const string AssetDir = "Assets/Settings";
        private const string ScenePath = SceneDir + "/NewcomerZone.unity";

        // ============================================================ 入口
        [MenuItem("浮生渡/3. 全部执行（构建并保存）", priority = 30)]
        public static void BuildAll()
        {
            SetupUniversalRenderPipeline();
            BuildScene();
            Debug.Log("[浮生渡] 垂直切片构建完成：" + ScenePath);
        }

        [MenuItem("浮生渡/1. 配置 URP 渲染管线", priority = 10)]
        public static void SetupUniversalRenderPipeline()
        {
            EnsureFolder(AssetDir);

            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDir + "/Fushengdu_Renderer.asset");
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, AssetDir + "/Fushengdu_Renderer.asset");
            }

            UniversalRenderPipelineAsset urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDir + "/Fushengdu_URP.asset");
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, AssetDir + "/Fushengdu_URP.asset");
            }

            urp.supportsCameraDepthTexture = true;
            urp.shadowDistance = 90f;
            urp.shadowCascadeCount = 2;
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            Debug.Log("[浮生渡] URP 渲染管线已配置");
        }

        /// <summary>
        /// 在 Resources 里预置一份天空材质：让 FDSky 着色器被打进包，
        /// 否则运行时 Shader.Find("Fushengdu/SkyDome") 可能拿到 null（天空变粉红）。
        /// </summary>
        private static void EnsureSkyMaterial()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Art");
            const string path = "Assets/Resources/Art/Mat_Sky.mat";

            Shader sh = Shader.Find("Fushengdu/SkyDome");
            if (sh == null)
            {
                Debug.LogWarning("[浮生渡] 未找到 Fushengdu/SkyDome 着色器，天空将回退到风格化主着色器");
                return;
            }

            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = sh;
            }
            m.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("浮生渡/2. 构建垂直切片场景（新手区）", priority = 20)]
        public static void BuildScene()
        {
            EnsureFolder(SceneDir);
            EnsureFolder(PrefabDir);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 清掉模板自带的相机与灯光，统一由本脚本创建
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == "Main Camera" || go.name == "Directional Light") Object.DestroyImmediate(go);
            }

            // ---- 美术层：只挂一个运行时生成器，几何体在 Awake 里生成（见 WorldArt.cs 顶部说明）
            EnsureSkyMaterial();
            var artGo = new GameObject("WorldArt");
            artGo.AddComponent<WorldArt>();

            // ---- 玩法层
            GameObject playerPrefab = BuildPlayerPrefab();
            BuildNpcs();
            BuildGatherNodes();
            BuildMonsters();
            BuildWorldObjects(playerPrefab);
            BuildCamera();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ============================================================ 玩法对象

        private static Material Mat(string name, Color color)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(PrefabDir + "/" + name + ".mat");
            if (m == null)
            {
                Shader shader = Shader.Find("Fushengdu/Stylized");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(shader);
                m.color = color;
                AssetDatabase.CreateAsset(m, PrefabDir + "/" + name + ".mat");
            }
            return m;
        }

        private static void BuildNpcs()
        {
            GameObject root = new GameObject("Npcs");

            // 任务 NPC：村长处者（QuestTable 中 TargetId = 9001）
            GameObject npc = new GameObject("NPC_Elder");
            npc.transform.SetParent(root.transform, false);
            float y = WorldArt.GroundHeight(3.5f, 6.5f);
            npc.transform.position = new Vector3(3.5f, y, 6.5f);
            npc.transform.rotation = Quaternion.Euler(0f, 195f, 0f);
            NpcEntity npcComp = npc.AddComponent<NpcEntity>();
            npcComp.NpcId = 9001;
            npcComp.NpcName = "处者";
            npcComp.Role = NpcRole.QuestGiver;
            npcComp.QuestIds = new System.Collections.Generic.List<int> { 1, 2, 3, 4, 5 };
            BuildQuestMarker(npc.transform);

            // 商人
            GameObject merchant = new GameObject("NPC_Merchant");
            merchant.transform.SetParent(root.transform, false);
            y = WorldArt.GroundHeight(-6.5f, 7.5f);
            merchant.transform.position = new Vector3(-6.5f, y, 7.5f);
            merchant.transform.rotation = Quaternion.Euler(0f, 160f, 0f);
            NpcEntity merchantComp = merchant.AddComponent<NpcEntity>();
            merchantComp.NpcId = 9002;
            merchantComp.NpcName = "行商";
            merchantComp.Role = NpcRole.Merchant;

            // 复活点（在低 y 处，玩家怎么落都要贴地）。
            // 【教训】出生坐标不要在这里改：每改一次就要重建场景，而 batchmode 重建场景后
            // BuildPipeline 有一定概率产出被截断 32 字节的 level0（播放器启动即 "level0 is corrupted"，
            // 自检会把它挡下来，但会白白耗掉 4 次重打）。出生点偏移改在 WorldArt 运行时做。
            var respawn = new GameObject("Respawn_NewbieVillage");
            respawn.transform.SetParent(root.transform, false);
            float ry = WorldArt.GroundHeight(0f, -10f);
            respawn.transform.position = new Vector3(0f, ry + 1.6f, -10f);
            respawn.AddComponent<RespawnPointMarker>().PointName = "新手村";
        }

        /// <summary>NPC 头顶任务标记（金色感叹号，始终面向相机）。</summary>
        private static void BuildQuestMarker(Transform npc)
        {
            var marker = new GameObject("QuestMarker");
            marker.transform.SetParent(npc, false);
            marker.transform.localPosition = new Vector3(0f, 2.55f, 0f);
            marker.AddComponent<Billboard>();

            Material gold = Mat("Mat_QuestMark", new Color(1f, 0.82f, 0.35f));
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Stem";
            bar.transform.SetParent(marker.transform, false);
            bar.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            bar.transform.localScale = new Vector3(0.10f, 0.42f, 0.10f);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.GetComponent<Renderer>().sharedMaterial = gold;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dot.name = "Dot";
            dot.transform.SetParent(marker.transform, false);
            dot.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            dot.transform.localScale = new Vector3(0.13f, 0.13f, 0.13f);
            Object.DestroyImmediate(dot.GetComponent<Collider>());
            dot.GetComponent<Renderer>().sharedMaterial = gold;
        }

        private static void BuildGatherNodes()
        {
            GameObject root = new GameObject("GatherPoints");
            Material herb = Mat("Mat_Herb", new Color(0.42f, 0.72f, 0.38f));
            Material berry = Mat("Mat_Berry", new Color(0.95f, 0.72f, 0.25f));

            // 灵草（QuestTable 环 3：ItemDefId 3001）
            Vector3[] positions =
            {
                new Vector3(-24f, 0f, -16f), new Vector3(-28f, 0f, 10f), new Vector3(21f, 0f, -21f),
                new Vector3(29f, 0f, 18f), new Vector3(-8f, 0f, 25f), new Vector3(24f, 0f, 4f),
                new Vector3(-16f, 0f, -26f), new Vector3(16f, 0f, 24f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject node = new GameObject("Herb_" + (i + 1));
                node.transform.SetParent(root.transform, false);
                float y = WorldArt.GroundHeight(positions[i].x, positions[i].z);
                node.transform.position = new Vector3(positions[i].x, y, positions[i].z);
                node.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);

                // 草叶
                for (int b = 0; b < 5; b++)
                {
                    GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blade.name = "Blade" + b;
                    blade.transform.SetParent(node.transform, false);
                    blade.transform.localPosition = new Vector3(Mathf.Cos(b * 1.3f) * 0.16f, 0.28f, Mathf.Sin(b * 1.3f) * 0.16f);
                    blade.transform.localEulerAngles = new Vector3(Mathf.Sin(b) * 16f, b * 72f, Mathf.Cos(b) * 14f);
                    blade.transform.localScale = new Vector3(0.05f, 0.55f, 0.02f);
                    Object.DestroyImmediate(blade.GetComponent<Collider>());
                    blade.GetComponent<Renderer>().sharedMaterial = herb;
                }
                // 灵果（发光，远处也能看见）
                GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "Orb";
                orb.transform.SetParent(node.transform, false);
                orb.transform.localPosition = new Vector3(0f, 0.62f, 0f);
                orb.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
                Object.DestroyImmediate(orb.GetComponent<Collider>());
                orb.GetComponent<Renderer>().sharedMaterial = berry;

                GatherNode gather = node.AddComponent<GatherNode>();
                gather.ItemDefId = 3001;
                gather.AmountPerGather = 1;
                gather.RespawnSeconds = 25f;

                SphereCollider col = node.AddComponent<SphereCollider>();
                col.radius = 1.2f;
            }
        }

        // 运行时按此预制体生成怪物（已在 NetworkManager 可生成集合中注册，含合法 PrefabId）
        private static GameObject _monsterPrefab;

        private static void BuildMonsters()
        {
            // 先构建可生成怪物预制体（含 NetworkObject + MonsterActor + NetActorSync + NetworkTransform），
            // 注册进 NetworkManager 可生成集合后由 MonsterSpawnPoint 在运行时实例化。
            // 这正是修复 65535 报错的根因：原先把无 PrefabId 的 NetworkObject 直接塞进场景，
            // FishNet 在场景加载时无法初始化它们。
            _monsterPrefab = BuildMonsterPrefab();

            // 刷怪点：灰狼 ×6 / 山贼 ×3 / 野猪王（精英）×1
            SpawnGroup(new Vector3(-38f, 0f, -34f), 1, 6, 5f);
            SpawnGroup(new Vector3(42f, 0f, 30f), 3, 3, 4f);
            SpawnGroup(new Vector3(-44f, 0f, 34f), 2, 1, 0f);
            SpawnGroup(new Vector3(46f, 0f, -40f), 1, 5, 5f);
        }

        /// <summary>构建可生成的怪物预制体（模式与 Player.prefab 完全一致）。</summary>
        private static GameObject BuildMonsterPrefab()
        {
            GameObject go = new GameObject("Monster");
            MonsterActor actor = go.AddComponent<MonsterActor>();
            NetworkObject nob = go.AddComponent<NetworkObject>();
            NetActorSync sync = go.AddComponent<NetActorSync>();
            sync.Entity = actor;
            go.AddComponent<NetworkTransform>();
            nob.SetIsSpawnable(true);

            string path = PrefabDir + "/Monster.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void SpawnGroup(Vector3 center, int monsterDefId, int count, float scatter)
        {
            MonsterDef def = MonsterTable.Default.Get(monsterDefId);
            if (def == null) return;

            float y = WorldArt.GroundHeight(center.x, center.z);
            GameObject root = new GameObject("SpawnPoint_" + monsterDefId);
            root.transform.position = new Vector3(center.x, y, center.z);
            var rootPoint = root.AddComponent<MonsterSpawnPoint>();
            rootPoint.MonsterDefId = monsterDefId;
            rootPoint.Count = count;
            rootPoint.ScatterRadius = scatter;
            rootPoint.MonsterPrefab = _monsterPrefab;
            // 注意：场景里只放"标记"，怪物在运行时由 MonsterSpawnPoint 按预制体生成（见 MonsterSpawnPoint.cs）。
            // 不再向场景写入任何 NetworkObject，避免 65535 报错。
        }

        private static GameObject BuildPlayerPrefab()
        {
            GameObject go = new GameObject("Player");
            go.tag = "Player";
            go.transform.position = new Vector3(0f, 1.0f, 0f);

            PlayerActor player = go.AddComponent<PlayerActor>();
            NetworkObject nob = go.AddComponent<NetworkObject>();
            NetActorSync sync = go.AddComponent<NetActorSync>();
            sync.Entity = player;
            go.AddComponent<NetworkTransform>();
            nob.SetIsSpawnable(true);

            // CharacterController 由 PlayerActor.Awake 自动补齐
            string path = PrefabDir + "/Player.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void BuildCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 1600f;
            camGo.transform.position = new Vector3(0f, 6f, -10f);
            camGo.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

            CameraFollow rig = camGo.AddComponent<CameraFollow>();
            rig.Distance = 7.2f;
            rig.Height = 2.9f;
            rig.Pitch = 14f;
        }

        private static void BuildWorldObjects(GameObject playerPrefab)
        {
            // ---- 战斗世界（权威端模拟）
            var worldGo = new GameObject("CombatWorld");
            float wy = WorldArt.GroundHeight(0f, -10f);
            worldGo.transform.position = new Vector3(0f, wy + 1.2f, -10f);
            CombatWorld world = worldGo.AddComponent<CombatWorld>();
            world.RespawnPoint = new Vector3(0f, wy + 1.6f, -10f);

            // ---- FishNet 网络管理
            var netGo = new GameObject("NetworkManager");
            NetworkManager nm = netGo.AddComponent<NetworkManager>();

            // 重建「可生成集合」资产，避免旧条目（怪物/桥接预制体）跨次构建残留导致重复注册
            string collPath = AssetDir + "/Fushengdu_SpawnablePrefabs.asset";
            if (AssetDatabase.LoadAssetAtPath<PrefabObjects>(collPath) != null)
                AssetDatabase.DeleteAsset(collPath);

            // 传输层：Tugboat（FishNet 免费核心自带 UDP 传输）
            if (netGo.GetComponent<FishNet.Transporting.Tugboat.Tugboat>() == null)
                netGo.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();

            // 玩家生成器
            PlayerSpawner spawner = netGo.AddComponent<PlayerSpawner>();
            if (playerPrefab != null)
            {
                NetworkObject playerNob = playerPrefab.GetComponent<NetworkObject>();
                if (playerNob != null)
                {
                    spawner.SetPlayerPrefab(playerNob);
                    RegisterSpawnablePrefab(nm, playerNob);
                }
            }
            spawner.Spawns = new[] { worldGo.transform };

            // 会话（实现 INetSession，供 UI 调用）
            FishNetSession session = netGo.AddComponent<FishNetSession>();
            session.NetworkManager = nm;
            session.DefaultPort = 7770;

            // ---- 可生成集合：注册怪物预制体（场景内不再直接摆放怪物 NetworkObject）
            if (_monsterPrefab != null)
            {
                NetworkObject mob = _monsterPrefab.GetComponent<NetworkObject>();
                if (mob != null) RegisterSpawnablePrefab(nm, mob);
            }

            // ---- 世界事件广播（需要 NetworkObject）：改为可生成预制体，运行时由服务端生成。
            // 直接把 NetworkObject 摆进场景会因无合法 PrefabId 触发 65535 报错。
            GameObject bridgePrefab = BuildBridgePrefab();
            if (bridgePrefab != null)
            {
                NetworkObject bNob = bridgePrefab.GetComponent<NetworkObject>();
                if (bNob != null) RegisterSpawnablePrefab(nm, bNob);
                session.WorldBridgePrefab = bridgePrefab;
            }

            // ---- UI（GameHud + 命令行自启动入口 + 自动截图（验证用））
            var uiGo = new GameObject("GameHud");
            uiGo.AddComponent<Fushengdu.UI.GameHud>();
            uiGo.AddComponent<Fushengdu.UI.CliAutoStart>();
            uiGo.AddComponent<Fushengdu.UI.AutoScreenshot>();
            uiGo.AddComponent<Fushengdu.UI.NameplateOverlay>();
        }

        /// <summary>构建可生成的世界事件桥接预制体（替代原先直接摆进场景的 NetworkBridge）。</summary>
        private static GameObject BuildBridgePrefab()
        {
            GameObject go = new GameObject("NetWorldBridge");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetWorldBridge>();
            string path = PrefabDir + "/NetWorldBridge.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        /// <summary>把预制体注册进 NetworkManager 的可生成集合（FishNet 依赖该集合分配 PrefabId）。</summary>
        private static void RegisterSpawnablePrefab(NetworkManager nm, NetworkObject prefab)
        {
            SerializedObject so = new SerializedObject(nm);
            SerializedProperty prop = so.FindProperty("_spawnablePrefabs");

            PrefabObjects collection = prop.objectReferenceValue as PrefabObjects;
            if (collection == null)
            {
                collection = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
                AssetDatabase.CreateAsset(collection, AssetDir + "/Fushengdu_SpawnablePrefabs.asset");
                prop.objectReferenceValue = collection;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            collection.AddObject(prefab, true, true);
            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(nm);
            AssetDatabase.SaveAssets();
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == path) return;
            }
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
