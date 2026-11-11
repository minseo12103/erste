Shader "ShapeCoop/WaterBarrierFlow"
{
    Properties
    {
        _MainTex ("Water Sprite Sheet", 2D) = "white" {}
        _AtlasRect ("Water Sprite UV Rect", Vector) = (0, 0, 1, 1)
        _UseStreamArt ("Use Water Sprite", Float) = 0
        _FlowTime ("Flow Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _AtlasRect;
                float _UseStreamArt;
                float _FlowTime;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 flow : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 flow : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.flow = input.flow;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float x = input.flow.x;
                float edge = min(x, 1 - x);
                half4 fallback = half4(lerp(half3(.18, .50, .79), half3(.54, .85, .98), smoothstep(0, .3, edge)),
                    .88 * smoothstep(0, .065, edge));
                // The authored frames have wide transparent margins. Use the center as
                // detail while the ribbon's edges define the visible stream width.
                float2 artUV = lerp(float2(.23, .025), float2(.79, .975), input.flow.xy);
                half4 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, _AtlasRect.xy + artUV * _AtlasRect.zw);
                half4 water = half4(lerp(fallback.rgb, art.rgb, art.a * _UseStreamArt), fallback.a);

                float lanes = x * 11;
                float seed = frac((floor(lanes) + 1) * .618034);
                float across = 1 - smoothstep(.065, .17, abs(frac(lanes) - .5));
                float dash = frac(input.flow.z * 3.2 - _FlowTime * (2.2 + seed) + seed);
                float along = smoothstep(.70, .77, dash) * (1 - smoothstep(.90, .98, dash));
                water.rgb = lerp(water.rgb, half3(.90, .98, 1), across * along * .55);
                return water;
            }
            ENDHLSL
        }
    }
}
