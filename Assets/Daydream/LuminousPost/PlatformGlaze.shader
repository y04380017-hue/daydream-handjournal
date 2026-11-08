Shader "Daydream/Pearlescent glaze" {
 Properties { _Night("Night",Range(0,1))=0 }
 SubShader { Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+3"} Pass { ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;};
 CBUFFER_START(UnityPerMaterial)
 half _Night;
 CBUFFER_END
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.w=TransformObjectToWorld(a.p.xyz);return o;}
 half4 frag(V i):SV_Target{
 float2 p=i.w.xz;float t=_Time.y*.065;float f=p.y*15+2.3*sin(p.x*4.5+t)+1.2*sin(p.x*9-p.y*4);
 float w=.5+.5*sin(f),n=pow(w,22),sh=pow(1-w,7);
 float3 d=lerp(float3(.83,.75,.84),float3(1,.98,1),w);
 float3 k=lerp(float3(.22,.17,.19),float3(.82,.53,.27),w);
 return half4(lerp(d,k,_Night),(.10+.30*n+.12*sh)*lerp(1,.43,_Night));}
 ENDHLSL
 }}}

