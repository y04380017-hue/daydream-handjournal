Shader "Daydream/Night dream floor"
{
    // Endless still floor under the cloud layer: a very low-contrast checkerboard with a soft glossy sheen
    // that dissolves into transparency (and so into fog and sky) a few table lengths from the desk.
    Properties
    {
        _BaseColor ("Base", Color) = (0.612,0.588,0.784,1)
        _TileColor ("Tile", Color) = (0.663,0.639,0.824,1)
        _SheenColor ("Reflected sky", Color) = (0.788,0.663,0.788,1)
        _TileSize ("Tile size (m)", Float) = 0.65
        _Center ("Fade centre (world xz)", Vector) = (0,0,0.6,0)
        _FadeStart ("Fade start (m)", Float) = 1.6
        _FadeEnd ("Fade end (m)", Float) = 4.2
        _Reflection ("Reflection strength", Range(0,1)) = 0.35
        _Fade ("Night fade", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-60" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _TileColor, _SheenColor; float _TileSize; float4 _Center; float _FadeStart, _FadeEnd, _Reflection, _Fade;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 cell = floor(i.positionWS.xz / _TileSize);
                float checker = fmod(abs(cell.x + cell.y), 2);
                float2 f = abs(frac(i.positionWS.xz / _TileSize) - .5);
                float seam = smoothstep(.47, .5, max(f.x, f.y)) * .25;   // blurred, barely-there grout
                half3 c = lerp(_BaseColor.rgb, _TileColor.rgb, saturate(checker * (1 - seam)));
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float grazing = pow(1 - saturate(view.y), 3);
                c = lerp(c, _SheenColor.rgb, _Reflection * (.35 + .65 * grazing));
                float dist = length(i.positionWS.xz - _Center.xy);
                float alpha = (1 - smoothstep(_FadeStart, _FadeEnd, dist)) * _Fade;
                c = MixFog(c, i.fog);
                return half4(c, alpha);
            }
            ENDHLSL
        }
    }
}
