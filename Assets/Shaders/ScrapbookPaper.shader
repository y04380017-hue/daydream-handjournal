Shader "VintageJournal/ScrapbookPaper"
{
    Properties
    {
        [MainTexture] _BaseMap("Original paper artwork", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _IsTape("Washi fiber edges", Float) = 0
        _UVRepeat("Horizontal print repeats", Float) = 1
        _Shadow("Soft contact shadow", Float) = 0
        _Softness("Shadow softness", Float) = .009
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PaperArtwork"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Opacity;
                float _IsTape;
                float _UVRepeat;
                float _Shadow;
                float _Softness;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=v.uv;
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float EdgeAlpha(float2 uv)
            {
                float edge=min(uv.y,1-uv.y);
                float fiber=Hash(float2(floor(uv.x*900),floor(uv.y*160)));
                return lerp(1, smoothstep(.001,.009+fiber*.008,edge)*smoothstep(0,.009,min(uv.x,1-uv.x)),saturate(_IsTape));
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 printUV=float2(i.uv.x*max(.01,_UVRepeat),i.uv.y);
                half4 art=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,printUV);
                float edge=EdgeAlpha(i.uv);
                if (_Shadow>.5)
                {
                    float s=_Softness;
                    float a=art.a*.24;
                    a+=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,printUV+float2(s,0)).a*.19;
                    a+=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,printUV-float2(s,0)).a*.19;
                    a+=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,printUV+float2(0,s)).a*.19;
                    a+=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,printUV-float2(0,s)).a*.19;
                    return half4(_BaseColor.rgb,a*_BaseColor.a*_Opacity*edge);
                }
                float noise=Hash(floor(i.uv*float2(1400,900)));
                Light sun=GetMainLight();
                float light=.88+.12*saturate(dot(normalize(i.normalWS),sun.direction));
                float grain=lerp(1,(.975+noise*.035),saturate(_IsTape));
                float3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                float glint=pow(saturate(dot(normalize(view+sun.direction),normalize(i.normalWS))),36);
                float sparkle=step(.996,noise)*glint*.065*saturate(_IsTape);
                float alpha=art.a*_BaseColor.a*_Opacity*edge*lerp(1,.89,saturate(_IsTape));
                clip(alpha-.001);
                return half4(art.rgb*_BaseColor.rgb*light*grain+sparkle,alpha);
            }
            ENDHLSL
        }
    }
}
