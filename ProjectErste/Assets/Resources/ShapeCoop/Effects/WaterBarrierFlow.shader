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
                float4 flow : TEXCOORD0;
                // Incoming U, outer-edge feather, branch side, and junction progress.
                float4 junction : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 flow : TEXCOORD0;
                float4 junction : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.flow = input.flow;
                output.junction = input.junction;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                bool branch = input.flow.w > .5;
                float progress = input.junction.w;
                float overlap = saturate(progress + 1);
                float transition = branch ? smoothstep(0, 1, progress) : 1;
                float3 coordinates = input.flow.xyz;
                if (branch) coordinates.x = lerp(input.junction.x, coordinates.x, transition);
                float x = coordinates.x;
                float edge = min(x, 1 - x);
                half4 fallback = half4(lerp(half3(.18, .50, .79), half3(.54, .85, .98), smoothstep(0, .3, edge)),
                    .88 * smoothstep(0, .065, edge));
                // The authored frames have wide transparent margins. Use the center as
                // detail while the ribbon's edges define the visible stream width.
                // Blend the two water patterns instead of squeezing an entire frame
                // into the turn. Both samples agree at the top of the overlap.
                float2 sourceUV = float2(branch && transition < 1 ? input.junction.x : input.flow.x, input.flow.y);
                float2 artUV = lerp(float2(.23, .025), float2(.79, .975), sourceUV);
                half4 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, _AtlasRect.xy + artUV * _AtlasRect.zw);
                if (branch && transition > 0 && transition < 1)
                {
                    float2 branchUV = lerp(float2(.23, .025), float2(.79, .975), input.flow.xy);
                    half4 branchArt = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, _AtlasRect.xy + branchUV * _AtlasRect.zw);
                    art = lerp(art, branchArt, transition);
                }
                half4 water = half4(lerp(fallback.rgb, art.rgb, art.a * _UseStreamArt), fallback.a);

                float lanes = x * 11;
                float seed = frac((floor(lanes) + 1) * .618034);
                float across = 1 - smoothstep(.065, .17, abs(frac(lanes) - .5));
                float dash = frac(coordinates.z * 3.2 - _FlowTime * (2.2 + seed) + seed);
                float along = smoothstep(.70, .77, dash) * (1 - smoothstep(.90, .98, dash));
                water.rgb = lerp(water.rgb, half3(.90, .98, 1), across * along * .55);
                if (branch)
                {
                    float outerDistance = input.junction.z < 0 ? input.flow.x : 1 - input.flow.x;
                    float inset = input.junction.y;
                    float feather = smoothstep(inset * .25, inset + .04, outerDistance);
                    water.a *= lerp(1, lerp(.22, 1, feather), smoothstep(0, .015, inset));
                    // A restrained highlight marks the actual overlapping part of the streams.
                    water.rgb = lerp(water.rgb, half3(.88, .97, 1), 4 * overlap * (1 - overlap) * .16);
                    water.a *= overlap;
                }
                else
                {
                    // Keep the combined opacity steady while the branches replace the main flow.
                    water.a *= (1 - overlap) / max(.0001, 1 - water.a * overlap);
                }
                return water;
            }
            ENDHLSL
        }
    }
}
