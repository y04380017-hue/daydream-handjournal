using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static partial class LuminousAtelierSetup
    {
        static void BuildPostRoute()
        {
            var sky=Node(master,"Night mail route",Vector3.zero);
            var boats=new List<DreamSkyPasser>();
            string[] labels={"Lantern paper boat","Letter carrier airship","Luminous postage stamp"};
            for(int i=0;i<labels.Length;i++)
            {
                var carrier=Node(sky,labels[i],Vector3.zero);
                var visual=Node(carrier,"Drifting parcel",Vector3.zero);
                var artwork=Node(visual,"Postal craft",Vector3.zero);
                artwork.localRotation=Quaternion.Euler(0,i==0?-14:i==1?12:-17,i==2?-12:0);
                if(i==0)Boat(artwork);else if(i==1)Airship(artwork);else Stamp(artwork);
                float depth=3.7f+i*.35f;var pass=carrier.gameObject.AddComponent<DreamSkyPasser>();
                pass.timeOfDay=time;pass.visual=visual;pass.nightOnly=true;pass.resetOnNightfall=true;
                pass.from=At(i==1?1.08f:-.08f,i==1?.81f:.77f,depth);pass.to=At(i==1?-.08f:1.08f,i==2?.83f:.80f,depth);
                pass.period=108;pass.duration=28;pass.phaseSeconds=new[]{8f,79f,44f}[i];pass.bob=.043f;
                boats.Add(pass);
                // Prefabs keep the art independent from the paths and the desk's time controller.
                PrefabUtility.SaveAsPrefabAsset(artwork.gameObject,Folder+"Prefabs/"+labels[i]+".prefab");
            }
            room.nightMail=boats.ToArray();
        }
        static void Facets(Transform p,string name,Vector3[][] faces,Material mat)
        {
            var verts=new List<Vector3>();var tris=new List<int>();
            foreach(var f in faces){int k=verts.Count;verts.AddRange(f);
                for(int n=1;n<f.Length-1;n++){tris.AddRange(new[]{k,k+n,k+n+1});tris.AddRange(new[]{k,k+n+1,k+n});}}
            Surface(p,name,verts.ToArray(),tris.ToArray(),new Vector2[verts.Count],mat);
        }
        static void Boat(Transform p)
        {
            var bow=new Vector3(.37f,.074f,0);var stern=new Vector3(-.39f,.065f,0);
            var keel=new Vector3(0,-.096f,0);var front=new Vector3(0,.014f,-.145f);var back=new Vector3(0,.014f,.145f);
            Facets(p,"Hull ivory folds",new[]{new[]{stern,front,keel},new[]{bow,keel,back},new[]{front,bow,new Vector3(.12f,.085f,-.036f)}},linen);
            Facets(p,"Hull shaded folds",new[]{new[]{front,bow,keel},new[]{stern,keel,back},new[]{stern,front,new Vector3(-.13f,.091f,-.03f)}},blush);
            Rod(p,"Folded rim",stern,front,.004f,cream);Rod(p,"Folded rim",front,bow,.004f,cream);
            Rod(p,"Lantern staff",new Vector3(-.09f,-.025f,0),new Vector3(-.09f,.255f,0),.006f,gold);
            Rod(p,"Lantern arm",new Vector3(-.11f,.246f,0),new Vector3(.075f,.246f,0),.005f,gold);
            Rod(p,"Lantern hook",new Vector3(.055f,.246f,0),new Vector3(.055f,.202f,0),.003f,gold);
            Lantern(p,"Boat lantern",new Vector3(.055f,.138f,0),.133f);
            var flare=Part(p,"Lantern aura",PrimitiveType.Quad,new Vector3(.055f,.138f,.014f),Vector3.one*.32f,glow);surfaces.Add(flare.GetComponent<Renderer>());
            Facets(p,"Pennant",new[]{new[]{new Vector3(-.09f,.249f,0),new Vector3(-.224f,.228f,0),new Vector3(-.09f,.206f,0)}},rose);
        }
        static void Envelope(Transform p,Vector3 pos,float w,float angle)
        {
            var t=Node(p,"Folded letter",pos);t.localEulerAngles=new Vector3(0,0,angle);float h=w*.64f,d=w*.12f;
            Box(t,"Ivory envelope",Vector3.zero,new Vector3(w,h,d),linen);
            var left=new Vector3(-w*.48f,h*.46f,-d*.55f);var right=new Vector3(w*.48f,h*.46f,-d*.55f);var tip=new Vector3(0,-h*.13f,-d*.55f);
            Rod(t,"Seal seam",left,tip,w*.012f,gold);Rod(t,"Seal seam",right,tip,w*.012f,gold);
            Part(t,"Sealing wax",PrimitiveType.Sphere,tip+Vector3.back*.001f,new Vector3(w*.12f,w*.12f,w*.03f),rose);
        }
        static void Airship(Transform p)
        {
            var shell=Part(p,"Woven aerostat",PrimitiveType.Sphere,new Vector3(0,.10f,0),new Vector3(.70f,.29f,.24f),linen);
            for(int j=-2;j<=2;j++)
            {
                float x=j*.107f;float s=Mathf.Sqrt(1-(x/.35f)*(x/.35f));
                for(int k=0;k<20;k++){float a=k*Mathf.PI/10,b=(k+1)*Mathf.PI/10;
                    Rod(p,"Hull ribbon",new Vector3(x,.10f+Mathf.Cos(a)*.147f*s,Mathf.Sin(a)*.122f*s),new Vector3(x,.10f+Mathf.Cos(b)*.147f*s,Mathf.Sin(b)*.122f*s),.0037f,gold);}
            }
            Facets(p,"Rose tail fins",new[]{new[]{new Vector3(.21f,.16f,0),new Vector3(.38f,.26f,0),new Vector3(.337f,.103f,0)},new[]{new Vector3(.258f,.10f,0),new Vector3(.385f,.12f,-.13f),new Vector3(.342f,.095f,0)}},rose);
            for(int i=-1;i<=1;i+=2){Rod(p,"Basket rigging",new Vector3(i*.15f,.035f,-.06f),new Vector3(i*.083f,-.13f,-.065f),.003f,gold);}
            Box(p,"Mail basket",new Vector3(0,-.136f,0),new Vector3(.19f,.052f,.118f),blush);
            for(int i=0;i<3;i++)Envelope(p,new Vector3(-.065f+.062f*i,-.096f,-.045f-i*.005f),.09f,-12+i*9);
            Part(p,"Navigation light",PrimitiveType.Sphere,new Vector3(-.337f,.098f,0),Vector3.one*.03f,amber);
        }
        static void Stamp(Transform p)
        {
            const float w=.38f,h=.48f;
            // The scalloped outline is actual paper geometry, visible against the dark sky.
            var outline=new List<Vector3>();var corners=new[]{new Vector2(-w/2,-h/2),new Vector2(w/2,-h/2),new Vector2(w/2,h/2),new Vector2(-w/2,h/2)};
            for(int side=0;side<4;side++)
            {Vector2 a=corners[side],b=corners[(side+1)%4],v=(b-a).normalized,inward=new Vector2(-v.y,v.x);int teeth=side%2==0?8:10;
                for(int j=0;j<teeth*8;j++){float f=j/(float)(teeth*8);float notch=Mathf.Pow(Mathf.Abs(Mathf.Sin(j*Mathf.PI/8)),4)*.008f;var q=Vector2.Lerp(a,b,f)+inward*notch;outline.Add(new Vector3(q.x,q.y,0));}}
            Facets(p,"Perforated ivory stamp",outline.Select((q,i)=>new[]{Vector3.zero,q,outline[(i+1)%outline.Count]}).ToArray(),cream);
            Box(p,"Printed keyline",new Vector3(0,0,-.003f),new Vector3(.332f,.427f,.004f),gold);
            Box(p,"Violet print",new Vector3(0,0,-.006f),new Vector3(.320f,.412f,.003f),violet);
            Box(p,"Midnight illustration",new Vector3(0,.017f,-.009f),new Vector3(.285f,.263f,.003f),navy);
            Facets(p,"Printed mountains",new[]{new[]{new Vector3(-.14f,-.11f,-.013f),new Vector3(-.05f,.04f,-.013f),new Vector3(.062f,-.11f,-.013f)},new[]{new Vector3(-.01f,-.11f,-.014f),new Vector3(.075f,-.005f,-.014f),new Vector3(.141f,-.11f,-.014f)}},mint);
            Part(p,"Golden moon",PrimitiveType.Sphere,new Vector3(.074f,.09f,-.014f),new Vector3(.058f,.058f,.003f),amber);
            for(int i=0;i<6;i++)Part(p,"Ink star",PrimitiveType.Sphere,new Vector3(-.124f+i*.043f,.085f+.034f*Mathf.Sin(i*2.4f),-.016f),Vector3.one*(i%2==0?.007f:.004f),cream);
            Caption(p,"NIGHT POST",new Vector3(0,.177f,-.016f),.031f);
            Caption(p,"AIR MAIL     03",new Vector3(0,-.158f,-.016f),.020f);
        }
        static void Caption(Transform p,string words,Vector3 pos,float size)
        {var t=Node(p,"Letterpress",pos);var text=t.gameObject.AddComponent<TextMesh>();text.text=words;text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=80;text.characterSize=size*.099f;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=Color.white;text.GetComponent<Renderer>().sharedMaterial=text.font.material;}
    }
}
