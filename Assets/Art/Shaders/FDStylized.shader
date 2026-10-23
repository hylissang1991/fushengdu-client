// 《浮生渡》表现层着色器 · 风格化写实（70 写实 : 30 夸张，D-10）
// 用途：地形 / 建筑 / 角色 / 道具的主材质。手写 URP 着色器（不依赖被剥离的 Built-in Standard）。
// 设计要点：
//   1) 半兰伯特 wrap 光照 + 两段式硬边明暗 —— 保证 64px 缩略图下剪影与体块仍可读（美术圣经三原则①）
//   2) 顶点色支持（地形做色斑变化、路径不需要额外贴图）
//   3) 边缘光统一方向（地平线冷色），让不同材质的物体"长在同一世界里"
//   4) 主色属性名用 _Color，与 Material.color / 怪物受击闪红逻辑兼容
Shader "Fushengdu/Stylized"
{
    Properties
    {
        [MainColor] _Color ("主色", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("基础贴图", 2D) = "white" {}
        _RimColor ("边缘光颜色", Color) = (0.45, 0.62, 0.85, 0.30)
        _RimPower ("边缘光衰减", Range(0.5, 8)) = 3.2
        _Wrap ("光照包裹", Range(0, 1)) = 0.42
        _Shade ("暗部强度", Range(0, 1)) = 0.62
        _Emission ("自发光", Range(0, 4)) = 0
        _Saturation ("饱和度", Range(0.4, 2.2)) = 1.28
        // 【归墟侵蚀层】形态图鉴 §9.2 三层叠加法之材质层。
        // _ErosionMap 通道约定：R = 侵蚀序场（裂隙中心=1，向外衰减，控制扩散顺序）；
        //                     G = 洋红隙缝线（满饱和信号色，见 frag 内顺序裁定注释）。
        // _Erosion01 由 MaterialPropertyBlock 驱动 0→1 表现扩散 —— 参数绝不写进材质：
        // RuntimeArt.Mat() 按 Color|emission 缓存且开启 instancing，写进材质会污染缓存键。
        // 褪色不新增参数：复用 _Saturation/_Shade（形态图鉴 §9.2"本色饱和度-50%"即
        // _Saturation 1.28→0.64，配 _Shade 0.62→0.78 才是"被抽干"而非"变灰"）。
        _ErosionMap ("侵蚀遮罩(R序场/G隙缝)", 2D) = "black" {}
        _Erosion01 ("侵蚀进度", Range(0, 1)) = 0
        _ErosionGlow ("侵蚀辉光强度", Range(0, 4)) = 1.5
        _Cutoff ("透明裁剪", Range(0, 1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("启用透明裁剪", Float) = 0
        [Toggle(_VERTEXCOLOR_ON)] _UseVertexColor ("启用顶点色", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // ---------------------------------------------------------------- 主光照
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_vertex _VERTEXCOLOR_ON
            // 注：不要在这里手写骨骼蒙皮。项目 gpuSkinning=0（CPU 蒙皮）时，
            // Unity 把形变后的顶点缓冲直接交给任意着色器，自定义着色器无需任何蒙皮代码；
            // 若开了 GPU 蒙皮再由着色器算一次，会变成"重复蒙皮"。见 RigBakeTest.cs 的验证结论。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BaseMap_ST;
                float4 _RimColor;
                float _RimPower;
                float _Wrap;
                float _Shade;
                float _Emission;
                float _Saturation;
                float _Erosion01;
                float _ErosionGlow;
                float _Cutoff;
                float _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            // 三个 pass 的 CBUFFER 必须布局一致（SRP Batcher 兼容性），所以
            // _Erosion01/_ErosionGlow 三处都声明；纹理声明未使用者会被编译器剔除。
            TEXTURE2D(_ErosionMap);
            SAMPLER(sampler_ErosionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 color : COLOR;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                #ifdef _VERTEXCOLOR_ON
                o.color = v.color;
                #else
                o.color = float4(1, 1, 1, 1);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);

                #ifdef _ALPHATEST_ON
                clip(tex.a * i.color.a - _Cutoff);
                #endif

                half3 albedo = tex.rgb * _Color.rgb * i.color.rgb;
                half3 n = normalize(i.normalWS);

                // ---------------- 归墟侵蚀层（材质层）----------------
                // _ErosionMap.R = 侵蚀序场：裂隙中心=1、向外衰减。_Erosion01 从 0→1 时，
                // 侵蚀区按序场从裂隙向外扩散（软边 8 倍率控制过渡带宽度）。
                half2 erosionTex = SAMPLE_TEXTURE2D(_ErosionMap, sampler_ErosionMap, i.uv).rg;
                half erosionVis = saturate((erosionTex.r - (1.0 - _Erosion01)) * 8.0);
                // 被侵蚀区域向"虚空近黑 #0F0C14"收敛（形态图鉴 §9.3 主色）。
                // 在 albedo 上染，侵蚀区仍受光照（保留体块），只是被"抽掉本色"。
                albedo = lerp(albedo, half3(0.059, 0.047, 0.078), erosionVis * 0.85);
                // 洋红隙缝 #C0407A：**独立加算通道**，不进 albedo、不进 _Emission。
                // （_Emission 是 albedo 的乘法项，会被本色染色 + 被饱和化，洋红绝不能走那里。）
                half3 erosionGlow = half3(0.753, 0.251, 0.478)
                                  * erosionTex.g * erosionVis * _Erosion01 * _ErosionGlow;

                // 主光（半兰伯特包裹；URP 的 mainLight.color 已含强度，不要再乘一遍）
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, mainLight.direction);
                half wrap = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half shade = smoothstep(0.30, 0.58, wrap);   // 明暗交界更"硬"一点，体块才立得起来
                half3 direct = albedo * mainLight.color * (0.42 + shade * 0.72) * saturate(mainLight.shadowAttenuation);

                // 半球环境光（天光偏冷 / 地面反弹偏暖）
                half up = n.y * 0.5 + 0.5;
                half3 skyAmb = half3(0.42, 0.50, 0.62);
                half3 gndAmb = half3(0.30, 0.26, 0.22);
                // 环境光占比过高会把一切压成同一个亮度 —— 画面"平"就是这么来的
                half3 ambient = albedo * lerp(gndAmb, skyAmb, up) * 0.30;

                // 边缘光：让所有物体共享同一条"世界边缘线"
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);
                half3 rimCol = _RimColor.rgb * rim * _RimColor.a;

                half3 col = direct + ambient + rimCol + albedo * _Emission;

                // ACES 近似色调映射：垂直切片不开后处理（URP 资源的 postProcessData 未必挂好），
                // 高光否则会直接 clip 成亮黄白色块（就是上一版"一片荧光绿"的根因）。
                col = (col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14);

                // 色调映射会压高光、顺手把饱和度一起吃掉 —— 画面因此发灰发白。
                // 这里在映射之后按亮度补回饱和度（风格化需要有"色"，不是有"亮"）。
                half lum = dot(col, half3(0.2126, 0.7152, 0.0722));
                col = lerp(lum.xxx, col, _Saturation);

                // 【顺序裁定 · team-lead 2026-10-22】侵蚀辉光必须在饱和化**之后**叠加。
                // 若加在 140 行随 _Emission 一起进 col，洋红 #C0407A 会被 _Saturation
                // 褪成灰紫，废掉形态图鉴 §9.5「归墟死亡最后一帧洋红熄灭」的信号可读性，
                // 并连带使 §13.1-7 灰度门禁失败（主识别通道之一变色）。
                // 【雾层迁移点】当前位于 MixFog 之前 → 远处洋红会被距离雾削弱。
                // 洋红是机制信号（同"可打断"图标），若 art-director 拍板"洋红需穿雾"
                // 以保证全距离可读，把下一行移到 MixFog 之后即可，无其他改动。
                col += erosionGlow;

                // 距离雾：让远山退到空气里，画面才有纵深（否则像贴纸）
                col = MixFog(saturate(col), i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- 阴影投射
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl" // LerpWhiteTo（须在 Shadows 之前）
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl" // ApplyShadowBias

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BaseMap_ST;
                float4 _RimColor;
                float _RimPower;
                float _Wrap;
                float _Shade;
                float _Emission;
                float _Saturation;
                float _Erosion01;
                float _ErosionGlow;
                float _Cutoff;
                float _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            // 三个 pass 的 CBUFFER 必须布局一致（SRP Batcher 兼容性），所以
            // _Erosion01/_ErosionGlow 三处都声明；纹理声明未使用者会被编译器剔除。
            TEXTURE2D(_ErosionMap);
            SAMPLER(sampler_ErosionMap);
            float3 _LightDirection;

            struct A2V
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            V2F ShadowVert(A2V v)
            {
                V2F o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(V2F i) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a;
                clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- 深度
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BaseMap_ST;
                float4 _RimColor;
                float _RimPower;
                float _Wrap;
                float _Shade;
                float _Emission;
                float _Saturation;
                float _Erosion01;
                float _ErosionGlow;
                float _Cutoff;
                float _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            // 三个 pass 的 CBUFFER 必须布局一致（SRP Batcher 兼容性），所以
            // _Erosion01/_ErosionGlow 三处都声明；纹理声明未使用者会被编译器剔除。
            TEXTURE2D(_ErosionMap);
            SAMPLER(sampler_ErosionMap);

            struct A2V { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V2F DepthVert(A2V v)
            {
                V2F o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 DepthFrag(V2F i) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
