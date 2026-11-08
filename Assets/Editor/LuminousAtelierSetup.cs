using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

namespace VintageJournal.Editor
{
    public static partial class LuminousAtelierSetup
    {
        const string Folder="Assets/Daydream/LuminousPost/";
        const string Scene="Assets/Scenes/VintageJournalDesk.unity";
        static Material glass, glow, linen, blush, amber, gold, navy, cream, rose, mint, violet;
        static Transform master;
        static DeskCameraProfile seat;
        static LuminousAtelier room;
        static DreamTimeController time;
        static readonly List<Renderer> surfaces=new List<Renderer>();

        [MenuItem("Vintage Journal/Daydream/Install luminous atelier")]
        public static void Install()
        {
            var scene=EditorSceneManager.OpenScene(Scene);
            if(GameObject.Find("Luminous postal atelier")) throw new InvalidOperationException("The scene already contains the luminous atelier.");
            foreach(string path in new[]{"Materials","Meshes","Prefabs"})Directory.CreateDirectory(Folder+path);
            AssetDatabase.Refresh();
            var camera=Object.FindFirstObjectByType<DeskCamera>();seat=camera.profile;
            camera.GetComponent<UniversalAdditionalCameraData>().requiresColorOption=CameraOverrideOption.On;
            if(PrefabUtility.IsPartOfPrefabInstance(camera))PrefabUtility.RecordPrefabInstancePropertyModifications(camera.GetComponent<UniversalAdditionalCameraData>());
            time=Object.FindFirstObjectByType<DreamTimeController>();
            master=new GameObject("Luminous postal atelier").transform;
            room=master.gameObject.AddComponent<LuminousAtelier>();time.atmosphere=room;
            surfaces.Clear();
            glass=MaterialAsset("Rose quartz water","Daydream/Spun glass","F6DDEB");
            glow=MaterialAsset("Beeswax aureole","Daydream/Lantern bloom card","FFBC78");
            glow.SetColor("_BaseColor",new Color(1,.53f,.22f,.48f));
            linen=Lit("Folded ivory","F9D8A0",.12f,.27f);blush=Lit("Blush paper fold","CB8F98",.14f,.20f);
            amber=Lit("Honey lantern","FFBE6B",.37f,2.2f);gold=Lit("Brass thread","D2A477",.49f,.12f);
            navy=Lit("Indigo post ink","433D63",.18f,.13f);cream=Lit("Cream satin piping","FFE3B1",.28f,.55f);
            rose=Lit("Copper rose","D78F91",.27f,.26f);mint=Lit("Sea glass","83B7BD",.3f,.33f);violet=Lit("Stamp violet","8F85AF",.16f,.32f);
            var skyMat=new Material(time.sky.sharedMaterial){name="Day palette and midnight sky"};
            skyMat.shader=Shader.Find("Daydream/Midnight post sky");AssetDatabase.CreateAsset(skyMat,Folder+"Materials/Midnight post sky.mat");time.sky.sharedMaterial=skyMat;
            var sea=GameObject.Find("Daydream Studio/Far cloud sea").GetComponent<Renderer>();
            var seaMat=new Material(sea.sharedMaterial){name="Pearl cloud bed"};seaMat.shader=Shader.Find("Daydream/Pearl cloud sea");AssetDatabase.CreateAsset(seaMat,Folder+"Materials/Pearl cloud bed.mat");
            sea.sharedMaterial=seaMat;surfaces.Add(sea);
            var veils=Node(master,"Spun water folds",Vector3.zero);
            // Each fold is a warped surface with a rolled lip, real interpolated normals and a tapered silhouette.
            Ribbon(veils,"Left flowing veil",-.23f,.58f,.26f,.80f,.06f,2.85f,.19f,1.12f);
            Ribbon(veils,"Distant sweeping crest",-.14f,.70f,1.22f,.91f,.11f,6.5f,.13f,3.1f);
            Ribbon(veils,"Right flowing veil",.78f,.85f,1.14f,.29f,.095f,3.45f,.12f,1.7f);
            Ribbon(veils,"Low glass swell",-.19f,.30f,.105f,.67f,.085f,2.15f,.07f,2.5f);
            NightLighting();
            var daytime=Object.FindObjectsByType<DaydreamFloat>(FindObjectsSortMode.None)
                .Where(f=>!f.transform.IsChildOf(GameObject.Find("CommissionDesk").transform)&&!f.transform.IsChildOf(GameObject.Find("Daydream Studio P2").transform)&&!f.GetComponentInChildren<Collider>()&&!f.name.ToLowerInvariant().Contains("cloud"))
                .SelectMany(f=>f.GetComponentsInChildren<Renderer>()).Where(r=>r.enabled).Distinct().ToArray();
            room.daytimeKeepsakes=daytime;
            var archive=Node(master,"Previous night decorations",Vector3.zero);
            foreach(var passer in Object.FindObjectsByType<DreamSkyPasser>(FindObjectsSortMode.None))
            {
                if(passer.nightOnly){passer.transform.SetParent(archive,true);passer.gameObject.SetActive(false);}
                else {passer.dayOnly=true;EditorUtility.SetDirty(passer);}
            }
            archive.gameObject.SetActive(false);
            BuildPostRoute();
            room.phaseSurfaces=surfaces.ToArray();
            EditorUtility.SetDirty(time);EditorUtility.SetDirty(room);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("[LuminousAtelier] Authored four glass folds, three warm lighting zones and three night-only postal models. Day-only keepsake renderers: "+daytime.Length);
        }

        static Color Hex(string s){ColorUtility.TryParseHtmlString("#"+s,out var c);return c;}
        static Material MaterialAsset(string name,string shader,string hex)
        {
            var sh=Shader.Find(shader);if(!sh||!sh.isSupported)throw new InvalidOperationException("Unavailable shader "+shader);
            var m=new Material(sh){name=name};if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",Hex(hex));AssetDatabase.CreateAsset(m,Folder+"Materials/"+name+".mat");return m;
        }
        static Material Lit(string name,string hex,float smooth,float emission)
        {var m=MaterialAsset(name,"Universal Render Pipeline/Lit",hex);m.SetFloat("_Smoothness",smooth);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(hex).linear*emission);return m;}
        static Transform Node(Transform parent,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
        static Transform Part(Transform p,string name,PrimitiveType type,Vector3 xyz,Vector3 size,Material m)
        {var t=GameObject.CreatePrimitive(type).transform;t.name=name;t.SetParent(p,false);t.localPosition=xyz;t.localScale=size;Object.DestroyImmediate(t.GetComponent<Collider>());var r=t.GetComponent<Renderer>();r.sharedMaterial=m;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return t;}
        static Transform Box(Transform p,string name,Vector3 xyz,Vector3 size,Material m)=>Part(p,name,PrimitiveType.Cube,xyz,size,m);
        static Transform Rod(Transform p,string name,Vector3 a,Vector3 b,float radius,Material m)
        {var t=Part(p,name,PrimitiveType.Cylinder,(a+b)*.5f,new Vector3(radius*2,(a-b).magnitude*.5f,radius*2),m);t.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);return t;}
        static Vector3 At(float x,float y,float z)
        {
            var look=Quaternion.LookRotation(seat.seatedTarget-seat.seatedPosition,Vector3.up);
            float tan=Mathf.Tan(seat.seatedFieldOfView*Mathf.Deg2Rad*.5f);
            var dir=look*new Vector3((x*2-1)*tan*(16f/9f),(y*2-1)*tan,1);
            return seat.seatedPosition+dir*((z-seat.seatedPosition.z)/dir.z);
        }
        static Renderer Surface(Transform p,string name,Vector3[] v,int[] tris,Vector2[] uv,Material mat)
        {
            var mesh=new Mesh{name=name,vertices=v,triangles=tris,uv=uv};mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Folder+"Meshes/"+name+".asset");
            var t=Node(p,name,Vector3.zero);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return r;
        }
        static void Ribbon(Transform root,string name,float x0,float y0,float x1,float y1,float width,float depth,float wave,float phase)
        {
            const int along=120,across=14;var v=new Vector3[(along+1)*(across+1)];var uv=new Vector2[v.Length];var tris=new List<int>();
            for(int i=0;i<=along;i++)for(int j=0;j<=across;j++)
            {
                float t=i/(float)along,s=j/(float)across,h=s*2-1;
                float x=Mathf.Lerp(x0,x1,t)+.014f*h*Mathf.Sin(t*9+phase);
                float y=Mathf.Lerp(y0,y1,t)+wave*(.65f*Mathf.Sin(t*6.28f+phase)+.23f*Mathf.Sin(t*11-phase));
                float flare=.32f+.68f*Mathf.Sin(t*Mathf.PI);
                y+=h*width*flare*(.54f+.46f*Mathf.Cos(t*7+phase))+.02f*h*h*Mathf.Sin(t*8+phase);
                float z=depth+.25f*Mathf.Sin(t*6+phase)+h*.31f*Mathf.Sin(t*8+phase)+h*h*.13f;
                int k=i*(across+1)+j;v[k]=At(x,y,z);uv[k]=new Vector2(t,s);
                if(i<along&&j<across){int b=k+across+1;tris.AddRange(new[]{k,k+1,b,b,k+1,b+1});}
            }
            surfaces.Add(Surface(root,name,v,tris.ToArray(),uv,glass));
        }
        static void NightLighting()
        {
            var cluster=Node(master,"Evening lamplight",Vector3.zero);
            room.nightFixtures=Node(cluster,"Hanging reading lanterns",Vector3.zero).gameObject;
            room.leftSoftbox=Lamp(cluster,"Amber light from the left",new Vector3(-.51f,1.36f,-.17f),new Vector3(-.20f,.80f,.06f),"FFD4A3",1.6f,114);
            room.overheadLantern=Lamp(cluster,"Broad light over the journal",new Vector3(.08f,1.49f,-.22f),new Vector3(.02f,.76f,-.07f),"FFE8C4",1.8f,104);
            room.edgeLight=Lamp(cluster,"Amber shelf and envelope light",new Vector3(.53f,1.23f,.37f),new Vector3(.34f,.8f,.08f),"FFC689",1.35f,112);
            // Practical lamps establish where the bounced light comes from. The thread sags outside the work area.
            for(int side=0;side<2;side++)
            {
                float x=side==0?.085f:.935f,y=side==0?.80f:.77f;
                var anchor=At(x,y,.82f);var top=At(side==0?-.04f:1.04f,1.08f,.85f);
                var mid=Vector3.Lerp(top,anchor,.58f)+Vector3.down*.036f;
                var fixtures=room.nightFixtures.transform;
                Rod(fixtures,"Braided cord",top,mid,.0024f,gold);Rod(fixtures,"Braided cord",mid,anchor+Vector3.up*.063f,.0024f,gold);
                Lantern(fixtures,"Pleated honey lantern",anchor,.11f);
                Glow(fixtures,anchor,.37f);
            }
            room.nightFixtures.SetActive(false);
        }
        static Light Lamp(Transform p,string name,Vector3 pos,Vector3 target,string color,float range,float angle)
        {
            var t=Node(p,name,pos);t.rotation=Quaternion.LookRotation(target-pos);var l=t.gameObject.AddComponent<Light>();
            l.type=LightType.Spot;l.spotAngle=angle;l.innerSpotAngle=angle*.40f;l.color=Hex(color);l.range=range;l.shadows=LightShadows.None;l.intensity=0;l.enabled=false;return l;
        }
        static void Glow(Transform p,Vector3 pos,float width)
        {
            var t=Part(p,"Diffused lamp light",PrimitiveType.Quad,pos,Vector3.one*width,glow);
            t.rotation=Quaternion.LookRotation(t.position-seat.seatedPosition,Vector3.up);
            surfaces.Add(t.GetComponent<Renderer>());
        }
        static void Lantern(Transform p,string name,Vector3 pos,float height)
        {
            var t=Node(p,name,pos);int n=24;var pts=new List<Vector3>();var tris=new List<int>();var uv=new List<Vector2>();
            float[] radii={.19f,.36f,.41f,.36f,.19f};float[] ys={-.5f,-.31f,0,.31f,.5f};
            for(int k=0;k<5;k++)for(int j=0;j<n;j++)
            {float a=2*Mathf.PI*j/n;float r=radii[k]*height*(j%2==0?1:.92f);pts.Add(new Vector3(Mathf.Cos(a)*r,ys[k]*height,Mathf.Sin(a)*r));uv.Add(new Vector2(j/(float)n,k/4f));
             if(k<4){int aa=k*n+j,b=k*n+(j+1)%n;tris.AddRange(new[]{aa,aa+n,b,b,aa+n,b+n});}}
            Surface(t,name+height.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+(pos.x<0?" L":" R"),pts.ToArray(),tris.ToArray(),uv.ToArray(),amber);
            for(int s=-1;s<=1;s+=2)Part(t,"Spun cap",PrimitiveType.Cylinder,new Vector3(0,s*height*.5f,0),new Vector3(height*.41f,height*.027f,height*.41f),gold);
            for(int j=0;j<6;j++){float a=j*Mathf.PI/3;Vector3 offset=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));for(int k=0;k<4;k++)Rod(t,"Silk rib",offset*radii[k]*height+Vector3.up*(ys[k]*height),offset*radii[k+1]*height+Vector3.up*(ys[k+1]*height),height*.009f,gold);}
        }
    }
}
