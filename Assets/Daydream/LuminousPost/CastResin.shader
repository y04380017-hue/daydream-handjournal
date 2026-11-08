Shader "Daydream/Cast rose resin" {
 Properties { _BaseColor("Original glaze pigment",Color)=(1,.81,.85,1) _Night("Night",Range(0,1))=0 }
 SubShader { Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+6" "RenderType"="Transparent"}
 Pass { Cull Back ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;};
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;half _Night;
 CBUFFER_END
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.w=TransformObjectToWorld(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);o.uv=a.uv;return o;}
 half4 frag(V i):SV_Target {
 float3 v=normalize(_WorldSpaceCameraPos-i.w),n=normalize(i.n);
 float2 local=i.uv*2-1;float2 bevel=local*pow(abs(local),8);
 n=normalize(n+mul((float3x3)unity_CameraToWorld,float3(bevel*.12,0)));
 float fres=pow(1-saturate(dot(n,v)),2.3);
 float3 r=reflect(-v,n);
 float wideOffset=(r.x+.6*r.y-.03)*2.8;float stripeOffset=(r.x+.58*r.y-.08)*17;float broad=exp(-wideOffset*wideOffset);float glint=exp(-stripeOffset*stripeOffset);
 float2 uv=GetNormalizedScreenSpaceUV(i.p);float2 off=mul((float3x3)UNITY_MATRIX_V,n).xy*.009;
 float3 bg=SampleSceneColor(saturate(uv+off));float3 tint=_BaseColor.rgb;
 float3 day=lerp(bg*tint,tint*(.65+.28*max(0,n.y)),.60)+(.24*broad+.30*glint+.19*fres)*float3(1,.93,.95);
 float3 evening=tint*float3(.61,.40,.31)+float3(.68,.44,.28)*(.24*broad+.29*glint+.10*fres);
 return half4(lerp(day,evening,_Night),.60+.27*fres+.09*glint);}
 ENDHLSL
 }}}

