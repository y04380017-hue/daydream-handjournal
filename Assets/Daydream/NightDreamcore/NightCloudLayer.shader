Shader "Daydream/Night cloud layer"
{
    // Thin, slowly drifting cloud layer below the furniture. Soft value-noise wisps, no hard edges.
    Properties
    {
        _BaseColor ("Cloud", Color) = (0.839,0.812,0.918,0.5)
        _Scale ("Noise scale (1/m)", Float) = 0.55
        _Drift ("Drift (m/s)", Vector) = (0.035,0.012,0,0)
        _Center ("Fade centre (world xz)", Vector) = (0,0.6,0,0)
        _Radius ("Visible radius (m)", Float) = 6
        _Fade ("Night fade", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float _Scale; float4 _Drift; float4 _Center; float _Radius; float _Fade;
            CBUFFER_END
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1,0)), f.x), lerp(Hash(i + float2(0,1)), Hash(i + float2(1,1)), f.x), f.y);
            }
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = (i.positionWS.xz + _Drift.xy * _Time.y) * _Scale;
                float n = Noise(p) * .55 + Noise(p * 2.1 + 7.3) * .3 + Noise(p * 4.3 - 3.1) * .15;
                float wisp = smoothstep(.38, .78, n);
                float dist = length(i.positionWS.xz - _Center.xy);
                float alpha = _BaseColor.a * wisp * (1 - smoothstep(_Radius * .55, _Radius, dist)) * _Fade;
                return half4(MixFog(_BaseColor.rgb, i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
