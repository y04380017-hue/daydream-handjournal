Shader "Daydream/FloatingSky"
{
    Properties
    {
        _DayTex ("Day sky", 2D) = "white" {}
        _NightTex ("Night sky", 2D) = "black" {}
        _DuskTex ("Dusk sky", 2D) = "white" {}
        _Dusk ("Dusk blend", Range(0,1)) = 0
        _Night ("Night blend", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_DuskTex); SAMPLER(sampler_DuskTex);
            CBUFFER_START(UnityPerMaterial)
            float _Night;
            float _Dusk;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 day = lerp(SAMPLE_TEXTURE2D(_DayTex,sampler_DayTex,i.uv), SAMPLE_TEXTURE2D(_DuskTex,sampler_DuskTex,i.uv), _Dusk);
                return lerp(day, SAMPLE_TEXTURE2D(_NightTex,sampler_NightTex,i.uv),_Night);
            }
            ENDHLSL
        }
    }
}
