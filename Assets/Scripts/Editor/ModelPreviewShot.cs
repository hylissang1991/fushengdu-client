// 《浮生渡》编辑器工具 · 角色模型预览截图
// 背景：玩家 / 怪物 / NPC 的模型都是**运行时**由 RuntimeArt 拼装的（不写进场景资源），
//       所以 Editor 场景截图看不到它们 —— 之前"画面里根本没有角色"的问题就是这么漏过去的。
// 这里在临时场景里把各模型摆成一排拍一张证件照，用于校验剪影、配色与体量。拍完立刻丢弃临时场景。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj> -logFile <log>
//             -executeMethod Fushengdu.Editor.ModelPreviewShot.Capture -fdShot <输出png>
using Fushengdu.Core;
using Fushengdu.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class ModelPreviewShot
    {
        [MenuItem("浮生渡/5. 角色模型预览截图", priority = 50)]
        public static void Capture()
        {
            string path = "D:/AI_Workspace/浮生渡/Logs/shots/models.png";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-fdShot" && i + 1 < args.Length) path = args[i + 1];
            }

            // 临时场景（不保存）
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.58f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);

            var sun = new GameObject("Sun");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(30f, -30f, 0f);

            // 地面参照
            var groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGo.transform.position = new Vector3(0f, 0f, 0f);
            groundGo.transform.localScale = new Vector3(4f, 1f, 3f);
            var gm = new Material(Shader.Find("Fushengdu/Stylized"));
            gm.color = new Color(0.34f, 0.42f, 0.26f);
            groundGo.GetComponent<Renderer>().sharedMaterial = gm;

            float x = -6f;
            RuntimeArt.AttachPlayerModel(Named("SwordCultivator", x), ClassId.SwordCultivator, false); x += 2.4f;
            RuntimeArt.AttachPlayerModel(Named("Berserker", x), ClassId.Berserker, false); x += 2.6f;
            RuntimeArt.AttachNpcModel(Named("NpcElder", x), false, "处者"); x += 2.4f;
            RuntimeArt.AttachNpcModel(Named("NpcMerchant", x), true, "行商"); x += 2.4f;

            MonsterDef wolf = MonsterTable.Default.Get(1);
            if (wolf != null) { GameObject g = Named("Wolf", x); RuntimeArt.AttachMonsterModel(g, wolf); } x += 2.6f;
            MonsterDef bandit = MonsterTable.Default.Get(3);
            if (bandit != null) { GameObject g = Named("Bandit", x); RuntimeArt.AttachMonsterModel(g, bandit); } x += 2.6f;
            MonsterDef boar = MonsterTable.Default.Get(2);
            if (boar != null) { GameObject g = Named("BoarKing", x); RuntimeArt.AttachMonsterModel(g, boar); }

            // 相机：正面平视，看全身
            int width = 1600, height = 700;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            var camGo = new GameObject("ShotCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.28f, 0.32f, 0.38f);
            float span = x + 6f;
            cam.transform.position = new Vector3(-6f + span * 0.5f - 1.2f, 1.55f, -9.5f);
            cam.transform.LookAt(new Vector3(-6f + span * 0.5f - 1.2f, 1.15f, 0f));
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);

            Debug.Log("[浮生渡] 角色模型预览已输出：" + path);

            // 丢弃临时场景，回到游戏场景
            EditorSceneManager.OpenScene("Assets/Scenes/NewcomerZone.unity", OpenSceneMode.Single);
        }

        private static GameObject Named(string name, float x)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(x, 0f, 0f);
            return go;
        }

    }
}
