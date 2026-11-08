using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    // Authoring only. Runtime ownership, inventory and input are still supplied by Stage 04-B.
    public static partial class DaydreamRoom
    {
        const string Root = "Assets/Daydream/";
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        static Material maple, cream, mint, sky, rose, lilac, honey, brass, graphite, ivory, wax;
        static Transform workspace, room;
        static Color C(string hex) { ColorUtility.TryParseHtmlString("#"+hex,out var c); return c; }
        static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/"+n+".png");
        static Material Mat(string name, string color, float gloss=.24f, string tex=null, float metal=0, bool unlit=false)
        {
            string path=Root+"Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(Shader.Find("Universal Render Pipeline/"+(unlit?"Unlit":"Lit"))); AssetDatabase.CreateAsset(m,path); }
            m.SetColor("_BaseColor",C(color)); m.SetTexture("_BaseMap",tex==null?null:Tex(tex));
            if(!unlit){m.SetFloat("_Smoothness",gloss);m.SetFloat("_Metallic",metal);}
            EditorUtility.SetDirty(m);return m;
        }
        static Transform Child(Transform parent,string n,Vector3 p)
        {
            var t=new GameObject(n).transform;t.SetParent(parent,false);t.localPosition=p;return t;
        }
        static Transform Shape(Transform parent,string n,PrimitiveType primitive,Vector3 p,Vector3 size,Material mat)
        {
            var t=GameObject.CreatePrimitive(primitive).transform;t.name=n;t.SetParent(parent,false);t.localPosition=p;t.localScale=size;
            Object.DestroyImmediate(t.GetComponent<Collider>());t.GetComponent<Renderer>().sharedMaterial=mat;return t;
        }
        static Transform Box(Transform p,string n,Vector3 pos,Vector3 size,Material m)=>Shape(p,n,PrimitiveType.Cube,pos,size,m);
        static Transform Oval(Transform p,string n,Vector3 pos,Vector3 size,Material m)=>Shape(p,n,PrimitiveType.Sphere,pos,size,m);
        static void Paint(Transform t,Material m){var r=t.GetComponent<Renderer>();if(r)r.sharedMaterial=m;}
        static void Palette()
        {
            maple=Mat("Honey maple","FFFFFF",.27f,"Maple");cream=Mat("Milk paint","F6F0E6");mint=Mat("Pistachio","A8DCC9");
            sky=Mat("Porcelain blue","ACD8E9");rose=Mat("Apricot","EDB39E");lilac=Mat("Wisteria","CCC1E4");
            honey=Mat("Pollen","F3D376");brass=Mat("Satin brass","C0A46F",.43f,null,.63f);
            graphite=Mat("Blue ink","364251");ivory=Mat("Cotton paper","FFFFFF",.1f,"CottonIvory");
            wax=Mat("Glassine","F1F1DE",.48f);
            var plaster=Mat("Ticking stripe","FFFFFF",.12f,"WallLinen");plaster.mainTextureScale=new Vector2(2,1);
            var linen=Mat("Sage cloth","C6DCCE",.17f);linen.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Linen.png");
            var palette=new Dictionary<string,Material>{
                {"Walnut",maple},{"DarkWalnut",maple},{"WarmPlaster",plaster},{"AgedBrass",brass},{"Linen",linen},{"LaceThread",cream},
                {"OxbloodLeather",rose},{"AnimalInk",graphite},{"PuppyCaramel",rose},{"BirdSage",mint},{"SageEnamel",mint},
                {"ReleaseBook_A_linen",mint},{"ReleaseBook_B_linen",lilac},{"Terracotta",rose},{"BookOchre",honey}};
            foreach(var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)){
                var m=r.sharedMaterial;if(m && palette.TryGetValue(m.name,out var replacement))r.sharedMaterial=replacement;
            }
        }
        [MenuItem("Vintage Journal/Daydream/Apply art to Stage 4B scene")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            if(GameObject.Find("Daydream Atelier"))throw new InvalidOperationException("Daydream art already installed on this scene.");
            workspace=GameObject.Find("WorkspaceStage4B").transform; room=GameObject.Find("RoomAndWindow").transform;
            Directory.CreateDirectory(Root+"Materials");Directory.CreateDirectory(Root+"Meshes");AssetDatabase.Refresh();
            new GameObject("Daydream Atelier");
            Palette(); Room(); Fixtures(); Book(); Daylight(); Details();
            foreach(var i in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None))
                if(i.homeSlot)i.transform.SetPositionAndRotation(i.homeSlot.WorldPose.position,i.homeSlot.WorldPose.rotation);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)){
                if(PrefabUtility.IsPartOfPrefabInstance(t)){
                    foreach(var c in t.GetComponents<Component>())if(c)PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        static void Room()
        {
            foreach(var r in room.GetComponentsInChildren<Renderer>()){
                if(r.name=="Soft garden foliage"||r.name=="Soft outdoor sky")r.enabled=false;
                if(r.name.StartsWith("Window ")||r.name=="Deep window sill")r.sharedMaterial=cream;
                if(r.name=="Wainscot rail")r.sharedMaterial=mint;
                if(r.name=="Wood floor")r.sharedMaterial=Mat("Limed boards","C5CFCA",.16f,"Maple");
            }
            var garden=Shape(room,"Distant summer garden",PrimitiveType.Quad,new Vector3(-.85f,1.12f,1.43f),new Vector3(2.25f,1.70f,1),Mat("Garden daylight","FFFFFF",.1f,"WindowGarden",0,true));
            garden.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            Box(room,"Picture ledge",new Vector3(.55f,.738f,.998f),new Vector3(1.05f,.04f,.31f),maple);
            for(int i=0;i<2;i++){
                var frame=Child(room,"Field study "+(i+1),new Vector3(.98f+i*.39f,1.05f+i*.055f,1.078f));
                frame.localEulerAngles=new Vector3(0,0,i==0?2.0f:-2.7f);
                Box(frame,"Lacquered frame",Vector3.zero,new Vector3(.352f,.430f,.021f),i==0?mint:rose);
                Shape(frame,"Cotton rag print",PrimitiveType.Quad,new Vector3(0,0,-.011f),new Vector3(.333f,.410f,1),Mat("Field print "+i,"FFFFFF",.1f,"Print"+(i+1)));
            }
            var oldFrame=room.Find("BotanicalFrame");if(oldFrame)oldFrame.localPosition=new Vector3(1.34f,1.61f,1.105f);
            var rig=Object.FindFirstObjectByType<DeskCamera>();var profile=rig.profile;
            profile.seatedPosition=new Vector3(-.008f,1.45f,-1.025f);profile.seatedTarget=new Vector3(0,.855f,.022f);profile.seatedFieldOfView=51;
            profile.openJournalAtDesk=true;profile.journalPosition=new Vector3(.018f,1.21f,-.36f);profile.journalTarget=new Vector3(.018f,.771f,-.125f);
            EditorUtility.SetDirty(profile);rig.transform.position=profile.seatedPosition;rig.transform.LookAt(profile.seatedTarget);rig.GetComponent<Camera>().fieldOfView=profile.seatedFieldOfView;
        }
        static void Fixtures()
        {
            var cabinet=workspace.Find("AcrylicTapeCabinet");cabinet.localPosition=new Vector3(-.482f,.76f,-.081f);
            foreach(var r in cabinet.GetComponentsInChildren<Renderer>()){
                if(r.name=="Walnut plinth")r.sharedMaterial=mint;
                if(r.name=="Independent brass handle")r.sharedMaterial=mint;
            }
            var clear=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/WorkspaceStage4B/Materials/ClearAcrylic.mat"));
            clear.SetColor("_BaseColor",new Color(.81f,.95f,.93f,.075f));clear.SetFloat("_Smoothness",.54f);
            clear.SetFloat("_SpecularHighlights",0);clear.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            AssetDatabase.CreateAsset(clear,Root+"Materials/Sea glass.mat");
            foreach(var r in workspace.GetComponentsInChildren<Renderer>(true))if(r.sharedMaterial&&r.sharedMaterial.name=="ClearAcrylic")r.sharedMaterial=clear;
            for(int i=0;i<3;i++){
                var t=workspace.Find(new[]{"BlackCatTapeStand","PuppyTapeStand","BirdTapeStand"}[i]);
                t.localPosition=new Vector3(-.614f+.129f*i,.76f,.263f+(i==1?.018f:0));
                var accent=new[]{lilac,rose,mint}[i];
                foreach(var r in t.GetComponentsInChildren<Renderer>()){
                    if(r.name=="Walnut foot"||r.name=="Shaft pull handle")r.sharedMaterial=accent;
                    if(r.name.EndsWith(" face")) {var s=r.transform.localScale;s.z=.010f;r.transform.localScale=s;}
                    if(r.name=="Gold animal eye") {var p=r.transform.localPosition;p.z=-.0005f;r.transform.localPosition=p;r.sharedMaterial=i==0?honey:graphite;}
                }
                // Small features sit on the front of the head, fully above the highest axle.
                for(int side=-1;side<=1;side+=2)
                    Oval(t,"Cheek",new Vector3(side*.014f,.299f,-.0004f),new Vector3(.007f,.003f,.002f),i==1?cream:rose);
                if(i==1)Oval(t,"Ivory muzzle",new Vector3(0,.296f,-.0006f),new Vector3(.014f,.008f,.002f),cream);
                if(i!=2)Oval(t,"Button nose",new Vector3(0,.298f,-.002f),new Vector3(.004f,.003f,.002f),i==0?rose:graphite);
            }
            var stickers=workspace.Find("HangingStickerRack");
            stickers.localPosition=new Vector3(.343f,.76f,.337f);
            foreach(Transform t in stickers){
                if(t.name=="Fine brass upright") {t.localPosition+=Vector3.up*.05f;var s=t.localScale;s.y+=.05f;t.localScale=s;}
                else if(t.name=="Walnut rack foot"){var s=t.localScale;s.z=.041f;t.localScale=s;Paint(t,sky);}
                else t.localPosition+=Vector3.up*.10f;
            }
            var tn=workspace.Find("TravelersBookcase");
            foreach(Transform t in tn){if(t.name=="Walnut back")Paint(t,sky);if(t.name=="Bookcase side panel")Paint(t,cream);}
            // Scalloped trim is behind the notebook lift column.
            for(int i=0;i<16;i++)Oval(tn,"Scalloped cornice",new Vector3(-.213f+i*.0284f,.25f,.050f),new Vector3(.029f,.014f,.010f),sky);
            var tns=Object.FindObjectsByType<TravelersNotebookBinding>(FindObjectsSortMode.None);
            foreach(var binding in tns){
                var item=binding.GetComponentInParent<PhysicalInteractable>();if(!item)continue;
                var cover=new[]{mint,rose,lilac,sky}[Mathf.Clamp(int.Parse(item.assetId.Substring(3))-1,0,3)];
                foreach(var r in item.visualRoot.GetComponentsInChildren<Renderer>())
                    if(r.name.IndexOf("cover",StringComparison.OrdinalIgnoreCase)>=0||r.sharedMaterial.name.ToLowerInvariant().Contains("leather"))r.sharedMaterial=cover;
            }
            for(int i=0;i<3;i++){
                var t=workspace.Find("SmallNoteBox_"+(i+1));t.localPosition=new Vector3(.48f,.76f,.123f-i*.165f);
                foreach(Transform part in t){
                    if(part.name.StartsWith("Small walnut")||part.name.StartsWith("Box ")){
                        var s=part.localScale;s.x*=1.5f;s.z*=1.5f;part.localScale=s;
                        var p=part.localPosition;p.x*=1.5f;p.z*=1.5f;part.localPosition=p;Paint(part,rose);
                    }
                    if(part.name=="Style sticker label"||part.name=="Label"){var p=part.localPosition;p.z*=1.5f;part.localPosition=p;}
                }
                Oval(t,"Thumb rest",new Vector3(0,.022f,-.075f),new Vector3(.051f,.009f,.008f),cream);
            }
            GameObject.Find("TeacupAndSaucer").transform.position=new Vector3(.61f,.76f,-.285f);
            GameObject.Find("GlassClipBox").transform.position=new Vector3(-.57f,.76f,.097f);
            GameObject.Find("MetalTray").transform.position=new Vector3(-.438f,.76f,.097f);
            var archive=workspace.Find("TieredPaperArchive");Paint(archive.Find("Archive rear"),mint);
            archive.position+=Vector3.forward*.025f;
            Object.FindFirstObjectByType<JournalController>().transform.position=new Vector3(.018f,.762f,-.125f);
            GameObject.Find("LinenMat").transform.position=new Vector3(.018f,.7602f,-.125f);
            var sheet=Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).First(i=>i.itemType==PhysicalItemType.LoosePage && i.assetId=="page_01");
            // The front sample keeps its original slot ID. Its two backing leaves are thin visual stationery.
            var slot=sheet.homeSlot;slot.transform.position=new Vector3(-.238f,.7623f,-.188f);slot.transform.rotation=Quaternion.Euler(0,-2,0);
            var stack=Child(workspace,"Loose refill samples",new Vector3(-.237f,.76f,-.177f));
            for(int i=0;i<2;i++){
                var page=Box(stack,"Cotton backing leaf",new Vector3(i*.003f,.00048f+i*.0007f,i*.004f),new Vector3(.148f,.00065f,.210f),i==0?ivory:cream);
                page.localRotation=Quaternion.Euler(0,i==0?6:-4,0);
            }
            foreach(var view in Object.FindObjectsByType<ReleaseBookView>(FindObjectsSortMode.None)){
                foreach(var r in view.pages)r.sharedMaterial=wax;
                foreach(var r in view.physical.visualRoot.GetComponentsInChildren<Renderer>(true))if(r.name=="Closed wax paper block")r.sharedMaterial=wax;
            }
            foreach(var label in workspace.GetComponentsInChildren<TextMesh>(true)){label.color=C("515E5D");}
        }
        static Mesh RingMesh()
        {
            const int n=40,k=8;var points=new Vector3[n*k];var tris=new List<int>();
            for(int i=0;i<n;i++)for(int j=0;j<k;j++){
                float a=i*2*Mathf.PI/n,b=j*2*Mathf.PI/k;Vector3 radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                points[i*k+j]=radial*(.027f+.0033f*Mathf.Cos(b))+Vector3.forward*(.0033f*Mathf.Sin(b));
                int v=i*k+j,w=((i+1)%n)*k+j,x=((i+1)%n)*k+(j+1)%k,y=i*k+(j+1)%k;
                tris.AddRange(new[]{v,w,x,v,x,y});
            }
            var mesh=new Mesh{name="Rolled wire binder ring",vertices=points,triangles=tris.ToArray()};mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Root+"Meshes/Six ring wire.asset");return mesh;
        }
        static void Book()
        {
            var book=Object.FindFirstObjectByType<JournalController>().transform;
            var cat=JournalPaperCatalog.Load();const string id="paper_daydream_ivory";var def=cat.Find(id);
            if(def==null){def=new JournalPaperDefinition{id=id,displayName="棉纸 · 纯米色",texture=Tex("CottonIvory")};ArrayUtility.Add(ref cat.papers,def);}
            cat.defaultPaperId=id;EditorUtility.SetDirty(cat);
            foreach(var r in book.GetComponentsInChildren<Renderer>()){
                if(r.name=="Left decorated page"||r.name=="Right decorated page")r.sharedMaterial=ivory;
                if(r.name=="Leather covers")r.sharedMaterial=mint;
                if(r.name=="Leather spine")r.sharedMaterial=brass;
                if(r.name.StartsWith("Clip ")||r.name=="Brass clip plate"||r.name=="Silk bookmark")r.enabled=false;
            }
            var mesh=RingMesh();
            foreach(float z in new[]{-.205f,-.151f,-.097f,.097f,.151f,.205f}){
                var r=Child(book,"Wire binding",new Vector3(0,.055f,z));r.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;r.gameObject.AddComponent<MeshRenderer>().sharedMaterial=brass;
                for(int sign=-1;sign<=1;sign+=2)Oval(book,"Punched eyelet",new Vector3(sign*.027f,.0516f,z),new Vector3(.014f,.0018f,.014f),graphite);
            }
        }
        static void Daylight()
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=C("939DAA");RenderSettings.ambientEquatorColor=C("8E8583");RenderSettings.ambientGroundColor=C("6D6564");
            var sun=GameObject.Find("AfternoonSun").GetComponent<Light>();sun.transform.rotation=Quaternion.Euler(51,-31,0);sun.color=C("FFF9F0");sun.intensity=.69f;
            sun.shadowStrength=.59f;sun.shadows=LightShadows.Soft;sun.gameObject.AddComponent<DaydreamLight>();
            foreach(var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)){if(l.type==LightType.Spot)l.intensity=.035f;if(l.name=="WindowBounce"){l.color=C("D9EAFB");l.intensity=.22f;}}
            var fill=Child(room,"North skylight",Vector3.zero).gameObject.AddComponent<Light>();fill.type=LightType.Directional;fill.color=C("E3EDFF");fill.intensity=.19f;fill.shadows=LightShadows.None;fill.transform.rotation=Quaternion.Euler(40,159,0);
            var grade=GameObject.Find("WarmColorGrade").GetComponent<Volume>();grade.weight=.18f;
        }
    }
}
