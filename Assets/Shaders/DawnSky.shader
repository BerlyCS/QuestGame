Shader "Custom/DawnSky"
{
    Properties
    {
        _HorizonColor ("Horizon Color", Color) = (1, 0.5, 0.3, 1)
        _ZenithColor ("Zenith Color", Color) = (0.22, 0.34, 0.62, 1)
        _SunGlowColor ("Sun Glow Color", Color) = (1, 0.75, 0.45, 1)
        _SunGlowStrength ("Sun Glow Strength", Float) = 1.1
        _SunDirection ("Sun Direction (world)", Vector) = (0, 1, 0, 0)
        _Alpha ("Alpha", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Rendered from inside a large dome, so cull the outward faces, never
        // write depth, and blend over the (night) skybox underneath.
        Cull Front
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "DawnSky"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HorizonColor;
                float4 _ZenithColor;
                float4 _SunGlowColor;
                float _SunGlowStrength;
                float4 _SunDirection;
                float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.direction = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 dir = normalize(IN.direction);

                // Warm band at the horizon fading to a cooler blue overhead.
                float up = saturate(dir.y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, 0.55));

                // Extra warmth gathered around wherever the sun is rising.
                float3 sunDir = normalize(_SunDirection.xyz);
                float sunAmount = saturate(dot(dir, sunDir));
                sky += _SunGlowColor.rgb * _SunGlowStrength * pow(sunAmount, 6.0);

                // Unlit and fog-free: the dome must read as sky, not geometry.
                return half4(sky, saturate(_Alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
