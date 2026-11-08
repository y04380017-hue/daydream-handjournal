Shader "Daydream/Night glow card"
{
    // Additive, sourceless glow: a soft disc (_Shape 0) or a soft rounded rectangle (_Shape 1).
    // Colour and strength are driven per renderer by NightDreamcore through a MaterialPropertyBlock.
    Properties
    {
        _BaseColor ("Glow colour", Color) = (1,0.95,0.8,1)
        _Intensity ("Intensity", Float) = 0
        _Shape ("Shape 0 disc 1 rect", Range(0,1)) = 0
        _Softness ("Edge softness", Range(0.02,1)) = 0.5
        _Core ("Bright core", Range(0,1)) = 0
        _FogAmount ("Fog influence", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float _Intensity; float _Shape; float _Softness; float _Core; float _FogAmount;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float disc = length(p);
                float2 q = abs(p); float rect = max(q.x, q.y);
                float d = lerp(disc, rect, _Shape);
                float soft = 1 - smoothstep(1 - _Softness, 1, d);
                soft *= soft;                                   // quadratic falloff, no ring
                float core = _Core * (1 - smoothstep(0, .18, disc));
                half3 c = _BaseColor.rgb * _Intensity * (soft + core);
                float fogVisible = lerp(1, ComputeFogIntensity(i.fog), _FogAmount);
                return half4(c * fogVisible, 1);
            }
            ENDHLSL
        }
    }
}
