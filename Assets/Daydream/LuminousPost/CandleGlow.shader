Shader "Daydream/Lantern bloom card" {
 Properties{ _BaseColor("Glow",Color)=(1,.56,.20,.55) _Night("Night",Range(0,1))=0 }
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+15"} Pass{
 Cull Off ZWrite Off Blend SrcAlpha One
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;half _Night;
 CBUFFER_END
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;return o;}
 half4 frag(V i):SV_Target {float r=length(i.uv*2-1);float a=exp(-r*r*6)*smoothstep(1,.65,r);return half4(_BaseColor.rgb,_BaseColor.a*a*_Night);}
 ENDHLSL
 }}}

