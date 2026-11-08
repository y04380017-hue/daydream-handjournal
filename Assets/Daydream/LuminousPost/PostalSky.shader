Shader "Daydream/Midnight post sky"
{
    Properties
    {
        _DayTex ("Day sky", 2D) = "white" {}
        _NightTex ("Night sky", 2D) = "black" {}
        _DuskTex ("Dusk sky", 2D) = "white" {}
        _Dusk ("Dusk blend", Range(0,1)) = 0
        _Night ("Night blend", Range(0,1)) = 0
        _DreamNight ("Dreamcore night gradient", Range(0,1)) = 0
        _NightTop ("Dream night top", Color) = (0.231,0.247,0.431,1)
        _NightMid ("Dream night 60%", Color) = (0.435,0.416,0.639,1)
        _NightHorizon ("Dream night horizon", Color) = (0.788,0.663,0.788,1)
        _NightBelow ("Dream night below horizon", Color) = (0.557,0.537,0.745,1)
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_DuskTex); SAMPLER(sampler_DuskTex);
            CBUFFER_START(UnityPerMaterial)
            float _Night;
            float _Dusk;
            float _DreamNight;
            half4 _NightTop, _NightMid, _NightHorizon, _NightBelow;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 day = lerp(SAMPLE_TEXTURE2D(_DayTex,sampler_DayTex,i.uv), SAMPLE_TEXTURE2D(_DuskTex,sampler_DuskTex,i.uv), _Dusk);
                float2 grid=i.uv*float2(760,430),cell=floor(grid);
                float seed=frac(sin(dot(cell,float2(12.989,78.233)))*43758.5);
                float2 jitter=frac(float2(seed*71.35,seed*47.73))*.54+.23;
                float dist=length(frac(grid)-jitter),aa=max(.025,min(.11,length(fwidth(grid))*.32));
                float star=(1-smoothstep(.072-aa,.072+aa,dist))*step(.943,seed);
                float haze=.5+.5*sin(i.uv.y*7+i.uv.x*3);
                half4 night=half4(lerp(float3(.012,.019,.055),float3(.047,.046,.115),haze)+star*lerp(float3(.44,.59,.85),float3(1,.83,.64),seed),1);
                // Dreamcore night: dusty indigo -> periwinkle -> a mauve horizon band that never fades; fewer, softer, twinkling stars.
                float elev = normalize(i.positionWS - _WorldSpaceCameraPos).y;
                float3 grad = elev >= 0
                    ? (elev < .3 ? lerp(_NightHorizon.rgb, _NightMid.rgb, smoothstep(0, .3, elev)) : lerp(_NightMid.rgb, _NightTop.rgb, smoothstep(.3, .9, elev)))
                    : lerp(_NightHorizon.rgb, _NightBelow.rgb, smoothstep(0, .16, -elev));
                float twinkle = .55 + .45 * sin(_Time.y * 6.2831 / (3 + 5 * frac(seed * 13.7)) + seed * 40);
                float layer = step(.968, seed);                       // mid layer twinkles, the rest stay dim and still
                float dreamStar = star * lerp(.35, twinkle, layer) * saturate(elev * 4 + .6);
                float3 dream = grad + dreamStar * float3(1, .96, .9) * .55;
                night.rgb = lerp(night.rgb, dream, _DreamNight);
                return lerp(day,night,_Night);
            }
            ENDHLSL
        }
    }
}
