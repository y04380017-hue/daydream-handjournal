Shader "Daydream/Pearl cloud sea"
{
 Properties { _BaseMap("Original cloud fibres",2D)="white"{} _BaseColor("Milk haze",Color)=(1,1,1,.7) _Night("Night",Range(0,1))=0 }
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-30"}
 Pass { Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float2 uv:TEXCOORD1;};
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;half _Night;
 CBUFFER_END
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.w=TransformObjectToWorld(a.p.xyz);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);return o;}
 half4 frag(V i):SV_Target{
 float2 p=i.w.xz*.48;float t=_Time.y*.035;
 float haze=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv*.8).a;
 float folds=p.y*1.8+1.8*sin(p.x*.77+t)+.76*cos(p.x*1.67-p.y*.24+t);
 float roll=.5+.5*cos(folds);
 float rim=pow(roll,17)*(.4+.6*haze);float glint=pow(roll,110);
 float farFade=1-smoothstep(12,30,distance(i.w,_WorldSpaceCameraPos));
 float3 day=lerp(float3(.77,.72,.82),float3(.995,.91,.95),haze*.7+.28*roll)+rim*float3(.11,.12,.13)+glint*.035;
 float3 night=lerp(float3(.012,.019,.059),float3(.067,.079,.155),haze*.39+.38*roll)+rim*.022;
 return half4(lerp(day,night,_Night),lerp((.32+.47*haze+.16*rim)*farFade,(.72+.13*rim)*farFade,_Night));}
 ENDHLSL
 }} }

