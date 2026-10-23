// 《浮生渡》编辑器工具 · 从 rig JSON 构建可播放 Mixamo 动画的角色预制体
//
// 【链路】Tripo 绑骨 GLB（80MB/41 骨）→ export_rig.mjs 减面+镜像转换 → rig JSON
//         → 本脚本：搭骨骼层级 + 蒙皮 Mesh + Humanoid Avatar → 存 prefab
//         → 运行时 Animator 播放那 19 个 Mixamo Humanoid 动画（自动重定向）
//
// 【为什么绕这一圈】Unity 原生不认 GLB，而 FBX 我们没有写工具；
// rig JSON 是纯数据，Unity 侧用公开 API（Mesh.bindposes / AvatarBuilder）就能拼出完全等价的结果。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <proj>
//             -executeMethod Fushengdu.Editor.RigBuilder.Run -logFile <log>
//   （JSON 放在 Assets/Art/Characters/RigData/<名字>.rig.json）
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fushengdu.Editor
{
    public static class RigBuilder
    {
        private const string DataDir = "Assets/Art/Characters/RigData";
        private const string OutDir = "Assets/Resources/Art/Characters/rig";

        // Tripo 骨名 → Unity Humanoid 槽位（41 骨里 Unity 只认这 21 根，其余自动当普通骨）
        private static readonly Dictionary<string, string> HumanMap = new Dictionary<string, string>
        {
            { "Hip", "Hips" }, { "Spine01", "Spine" }, { "Spine02", "Chest" },
            { "NeckTwist01", "Neck" }, { "Head", "Head" },
            { "L_Clavicle", "LeftShoulder" }, { "R_Clavicle", "RightShoulder" },
            { "L_Upperarm", "LeftUpperArm" }, { "L_Forearm", "LeftLowerArm" }, { "L_Hand", "LeftHand" },
            { "R_Upperarm", "RightUpperArm" }, { "R_Forearm", "RightLowerArm" }, { "R_Hand", "RightHand" },
            { "L_Thigh", "LeftUpperLeg" }, { "L_Calf", "LeftLowerLeg" }, { "L_Foot", "LeftFoot" },
            { "L_ToeBase", "LeftToes" },
            { "R_Thigh", "RightUpperLeg" }, { "R_Calf", "RightLowerLeg" }, { "R_Foot", "RightFoot" },
            { "R_ToeBase", "RightToes" },
        };

        [MenuItem("浮生渡/12. 从 rig JSON 构建角色", priority = 120)]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            int ok = 0, bad = 0;
            foreach (string f in Directory.GetFiles(DataDir, "*.rig.json"))
            {
                if (Build(Path.GetFileNameWithoutExtension(f).Replace(".rig", ""))) ok++;
                else bad++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log(string.Format("[RigBuilder] 完成：{0} 成功，{1} 失败", ok, bad));
            EditorApplication.Exit(bad > 0 ? 1 : 0);
        }

        private static bool Build(string name)
        {
            string jsonPath = Path.Combine(DataDir, name + ".rig.json");
            string pngPath = Path.Combine(DataDir, name + ".png");
            if (!File.Exists(jsonPath)) { Debug.LogError("[RigBuilder] 缺 JSON：" + jsonPath); return false; }

            RigData rig = JsonUtility.FromJson<RigData>(File.ReadAllText(jsonPath));
            if (rig == null || rig.bones == null || rig.bones.Length == 0)
            {
                Debug.LogError("[RigBuilder] JSON 解析失败：" + jsonPath);
                return false;
            }

            // —— 1) 骨骼层级
            var root = new GameObject(name + "_Rig");
            var bones = new Transform[rig.bones.Length];
            for (int i = 0; i < rig.bones.Length; i++)
            {
                RigBone b = rig.bones[i];
                var go = new GameObject(b.name);
                if (b.parent < 0) go.transform.SetParent(root.transform, false);
                else go.transform.SetParent(bones[b.parent], false);
                go.transform.localPosition = new Vector3(b.t[0], b.t[1], b.t[2]);
                go.transform.localRotation = new Quaternion(b.q[0], b.q[1], b.q[2], b.q[3]);
                go.transform.localScale = new Vector3(b.s[0], b.s[1], b.s[2]);
                bones[i] = go.transform;
            }

            // —— 2) 蒙皮 Mesh
            var mesh = new Mesh { name = name + "_Mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            int vc = rig.vertexCount;
            var verts = new Vector3[vc];
            var norms = new Vector3[vc];
            var uvs = new Vector2[vc];
            var bw = new BoneWeight[vc];
            var bindposes = new Matrix4x4[rig.bones.Length];

            for (int i = 0; i < vc; i++)
            {
                verts[i] = new Vector3(rig.mesh.positions[i * 3], rig.mesh.positions[i * 3 + 1], rig.mesh.positions[i * 3 + 2]);
                if (rig.mesh.normals != null)
                    norms[i] = new Vector3(rig.mesh.normals[i * 3], rig.mesh.normals[i * 3 + 1], rig.mesh.normals[i * 3 + 2]).normalized;
                if (rig.mesh.uvs != null)
                    uvs[i] = new Vector2(rig.mesh.uvs[i * 2], rig.mesh.uvs[i * 2 + 1]);
                if (rig.mesh.joints != null)
                {
                    bw[i].boneIndex0 = (int)rig.mesh.joints[i * 4];
                    bw[i].boneIndex1 = (int)rig.mesh.joints[i * 4 + 1];
                    bw[i].boneIndex2 = (int)rig.mesh.joints[i * 4 + 2];
                    bw[i].boneIndex3 = (int)rig.mesh.joints[i * 4 + 3];
                    bw[i].weight0 = rig.mesh.weights[i * 4];
                    bw[i].weight1 = rig.mesh.weights[i * 4 + 1];
                    bw[i].weight2 = rig.mesh.weights[i * 4 + 2];
                    bw[i].weight3 = rig.mesh.weights[i * 4 + 3];
                }
            }

            if (rig.bindposes != null)
                for (int b = 0; b < rig.bones.Length; b++)
                {
                    var m = new Matrix4x4();
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            m[r, c] = rig.bindposes[b * 16 + r * 4 + c];
                    bindposes[b] = m;
                }

            mesh.vertices = verts;
            if (norms.Length > 0) mesh.normals = norms;
            if (uvs.Length > 0) mesh.uv = uvs;
            if (rig.mesh.joints != null) mesh.boneWeights = bw;
            mesh.bindposes = bindposes;
            mesh.triangles = rig.mesh.triangles;
            mesh.RecalculateBounds();

            // —— 3) SkinnedMeshRenderer
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            // 【关键坑】rootBone 不能设成 Root 骨：设了之后 Unity 按"根骨相对空间"做蒙皮，
            // 而我们的 bindposes 是骨骼世界矩阵的逆（= glTF IBM 镜像转换后的等价物），
            // 参考系不匹配会把整个网格撕成"炸开"状。置 null 让 Unity 直接用骨骼世界矩阵，
            // 与 bindposes 的参考系一致（实测：null 之后正常变形，设 Root 之后散架）。
            smr.rootBone = null;
            smr.updateWhenOffscreen = true; //  batch 截图/远距不能剔，否则看到绑定姿势
            smr.localBounds = mesh.bounds;

            var shader = Shader.Find("Fushengdu/Stylized") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "M_Rig_" + name };
            mat.SetColor("_Color", Color.white);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            else Debug.LogWarning("[RigBuilder] 缺贴图：" + pngPath);
            smr.sharedMaterial = mat;

            // —— 4) Humanoid Avatar（Mixamo 动画能重定向到它）
            var all = root.GetComponentsInChildren<Transform>(true);
            var skeleton = new SkeletonBone[all.Length];
            for (int i = 0; i < all.Length; i++)
                skeleton[i] = new SkeletonBone
                {
                    name = all[i].name,
                    position = all[i].localPosition,
                    rotation = all[i].localRotation,
                    scale = all[i].localScale
                };

            var human = new List<HumanBone>();
            int mapped = 0;
            foreach (var kv in HumanMap)
            {
                bool has = false;
                for (int i = 0; i < rig.bones.Length; i++) if (rig.bones[i].name == kv.Key) { has = true; break; }
                if (!has) continue;
                human.Add(new HumanBone { boneName = kv.Key, humanName = kv.Value, limit = new HumanLimit { useDefaultValues = true } });
                mapped++;
            }

            var desc = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f,
                hasTranslationDoF = false
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError(string.Format("[RigBuilder] {0}：Avatar 无效（映射 {1} 根）", name, mapped));
                Object.DestroyImmediate(root);
                return false;
            }

            // —— 5) 先把 mesh / 材质 / avatar 落成磁盘资产，否则 prefab 序列化不了对它们的引用
            //    （运行时 new 出来的对象不是资产，SaveAsPrefabAsset 之后全变 null——实测踩过）
            string prefabPath = Path.Combine(OutDir, name + ".prefab");
            string avatarPath = Path.Combine(OutDir, name + ".avatar");
            string meshPath = Path.Combine(OutDir, name + "_mesh.asset");
            string matPath = Path.Combine(OutDir, name + "_mat.mat");
            AssetDatabase.CreateAsset(mesh, meshPath);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.CreateAsset(avatar, avatarPath);

            var animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 远距/截图不能被剔成绑定姿势

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            if (saved == null) { Debug.LogError("[RigBuilder] 保存失败 " + prefabPath); return false; }

            Debug.Log(string.Format("[RigBuilder] {0}：{1} 顶点 / {2} 三角 / {3} 骨，Humanoid 映射 {4} 根 → {5}",
                name, vc, rig.mesh.triangles.Length / 3, rig.bones.Length, mapped, prefabPath));
            return true;
        }
    }

    [System.Serializable] public class RigBone { public string name; public int parent; public float[] t; public float[] q; public float[] s; }
    [System.Serializable] public class RigMesh { public float[] positions; public float[] normals; public float[] uvs; public float[] joints; public float[] weights; public int[] triangles; }
    [System.Serializable] public class RigData { public string source; public int boneCount; public int vertexCount; public int triangleCount; public RigBone[] bones; public float[] bindposes; public RigMesh mesh; }
}
