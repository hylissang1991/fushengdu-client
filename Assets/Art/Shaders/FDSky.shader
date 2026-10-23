// 《浮生渡》表现层着色器 · 天穹渐变（顶点色）
// 用途：手搓天穹穹顶（不依赖 Skybox 材质，避免 URP 下天空盒丢失导致"白屏"）。
// 顶点色由 Editor 侧按高度插值写入：地平线暖白 → 中天青蓝 → 天顶深靛。
Shader "Fushengdu/SkyDome"
{
    Properties
    {
        [MainColor] _Color ("主色", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
