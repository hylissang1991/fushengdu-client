// 《浮生渡》编辑器工具 · 生成 DungeonArena 场景（Slice 1）
// 仅编辑器内运行（Fushengdu.Editor 程序集，includePlatforms=Editor）。
// 生成：地面 Plane + 方向光 + 出生锚点 + 复活点，保存到 Assets/Scenes/DungeonArena.unity。
// 注：Slice 1 运行时采用「当前场景内临时竞技场区域」降级方案（详见汇报），本脚本用于后续接入独立场景实例。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fushengdu.Editor
{
    public static class DungeonArenaBuilder
    {
        public const string ScenePath = "Assets/Scenes/DungeonArena.unity";

        [MenuItem("Fushengdu/Builder/生成 DungeonArena 场景")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "ArenaGround";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            var light = new GameObject("ArenaLight");
            var dir = light.AddComponent<Light>();
            dir.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var anchor = new GameObject("ArenaSpawnAnchor");
            anchor.transform.position = new Vector3(40f, 0f, 0f);

            var respawn = new GameObject("ArenaRespawnPoint");
            respawn.transform.position = new Vector3(40f, 1f, 8f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[DungeonArenaBuilder] 已生成场景: " + ScenePath);
        }
    }
}
#endif
