Shader "Daydream/Spun glass"
{
 Properties { _BaseColor("Glass tint", Color)=(.98,.87,.93,1) _Night("Night",Range(0,1))=0 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+12" "RenderType"="Transparent" }
 Pass {
 Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;};
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor; half _Night;
 CBUFFER_END
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.w=TransformObjectToWorld(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);o.uv=a.uv;return o;}
 half4 frag(V i):SV_Target {
 float3 eye=normalize(_WorldSpaceCameraPos-i.w);
 float u=i.uv.x,v=i.uv.y;
 float3 n=normalize(i.n+float3(.045*sin(u*31+v*6),.025*cos(u*23-v*5),0));
 float edge=pow(1-saturate(abs(dot(n,eye))),3);
 float2 suv=GetNormalizedScreenSpaceUV(i.p);
 float2 bend=mul((float3x3)UNITY_MATRIX_V,n).xy*.012*(.2+edge);
 float3 behind=SampleSceneColor(saturate(suv+bend));
 float3 reflected=reflect(-eye,n);
 float stripe=sin(reflected.y*10.0+reflected.x*2.8);
 float silk=smoothstep(.38,.99,stripe);
 float gleam=pow(saturate(stripe),18);
 float lip=pow(saturate(abs(v*2-1)),13);
 float tintShift=.5+.5*sin(u*14.0+v*5.7+reflected.x*3);
 float3 film=lerp(float3(.79,.89,1.0),_BaseColor.rgb,tintShift);
 float well=smoothstep(.30,.92,cos(v*7.2+u*8.4+reflected.x));
 float3 transmission=lerp(float3(1,1,1),float3(.76,.64,.80),.68*well+.25*lip);
 float3 day=behind*lerp(1.0,film,.41)*transmission+film*(.075*silk+.31*gleam+.22*lip)+.06*edge;
 float3 dark=behind*float3(.60,.65,.83)+float3(.65,.48,.37)*(.12*gleam+.08*lip);
 float tail=smoothstep(0,.065,u)*smoothstep(0,.09,1-u);
 return half4(lerp(day,dark,_Night),tail*lerp(.22+.39*silk+.28*lip+.1*edge,.16+.17*gleam,_Night));}
 ENDHLSL
 }} }

