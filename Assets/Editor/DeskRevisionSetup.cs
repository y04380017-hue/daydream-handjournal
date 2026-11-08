using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    public static partial class DeskRevisionSetup
    {
        const string ScenePath="Assets/Scenes/VintageJournalDesk.unity";
        const string Root="Assets/Daydream/DeskRevision/";
        static Material cream,mint,pink,blue,lilac,ink,gold,maple,cloth;
        static Transform archive;
        static T Need<T>() where T:Component => Object.FindFirstObjectByType<T>();
        public static void Audit()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory("docs/desk-revision");
            var lines=new List<string>();
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if(t.GetComponent<PhysicalContainer>()||t.GetComponent<PhysicalInteractable>()||t.GetComponent<JournalController>()||t.GetComponent<CommissionDeskController>())
                {lines.Add(t.name+" pos="+t.position+" scale="+t.lossyScale);foreach(Transform c in t)lines.Add("  "+c.name+" local="+c.localPosition+" scale="+c.localScale+" collider="+(bool)c.GetComponent<Collider>());}
            File.WriteAllLines("docs/desk-revision/scene-before.txt",lines);
            Debug.Log("[DeskRevision] Audit complete");
        }
        public static void CalibrateSky()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var profile=Need<DeskCamera>().profile;
            var sky=GameObject.Find("Distant floating curiosities").transform;
            var rotation=Quaternion.LookRotation(profile.seatedTarget-profile.seatedPosition,Vector3.up);
            var heights=new[]{.78f,.84f,.77f,.83f,.71f,.80f};
            for(int i=0;i<sky.childCount;i++)
            {
                var passer=sky.GetChild(i).GetComponent<DreamSkyPasser>();float z=3.2f+i*.18f;
                Vector3 At(float u,float v)
                {
                    float halfHeight=Mathf.Tan(profile.seatedFieldOfView*Mathf.Deg2Rad*.5f);
                    var direction=rotation*new Vector3((2*u-1)*halfHeight*16f/9f,(2*v-1)*halfHeight,1);
                    return profile.seatedPosition+direction*((z-profile.seatedPosition.z)/direction.z);
                }
                passer.from=At(i%2==0?-.06f:1.06f,heights[i]);passer.to=At(i%2==0?1.06f:-.06f,heights[i]+.035f);
                passer.bob=.035f;EditorUtility.SetDirty(passer);
                PrefabUtility.SaveAsPrefabAsset(passer.gameObject,Root+"Prefabs/"+passer.name+".prefab");
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        public static void Calibrate()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var box=GameObject.Find("Pastel floor mailbox").transform;
            if(PrefabUtility.IsPartOfPrefabInstance(box))PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(box.gameObject),PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            box.position=new Vector3(.88f,0,.08f);
            box.rotation=Quaternion.Euler(0,20,0);
            var bag=GameObject.Find("Cloth note pocket organizer").transform;
            for(int i=0;i<8;i++)
            {
                var pocket=bag.Find("Sewn pocket "+i); if(!pocket||pocket.Find("Embroidered patch"))continue;
                var front=pocket.Find("Pocket front");var scale=front.localScale;scale.x*=i%3==0?1.07f:.98f;scale.y*=i%3==1?1.12f:1;front.localScale=scale;
                int[] motifs={5,9,13,2,7,10,15,18};string id="dream_sticker_"+motifs[i].ToString("D2");
                var mat=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/"+id+".mat");
                Piece(pocket,"Embroidered patch",PrimitiveType.Quad,new Vector3(0,.001f,-.0155f),new Vector3(.029f,.029f,1),mat);
            }
            PrefabUtility.SaveAsPrefabAsset(bag.gameObject,Root+"Prefabs/ClothNoteOrganizer.prefab");
            var work=Need<NotebookWorkbench>();
            foreach(var notebook in work.notebooks)
            {
                var slot=notebook.homeSlot; var p=slot.transform.localPosition;p.z=-.008f;slot.transform.localPosition=p;
                notebook.ConfigureHome(slot);EditorUtility.SetDirty(notebook);EditorUtility.SetDirty(slot.transform);
                if(PrefabUtility.IsPartOfPrefabInstance(slot))PrefabUtility.RecordPrefabInstancePropertyModifications(slot.transform);
                if(PrefabUtility.IsPartOfPrefabInstance(notebook))PrefabUtility.RecordPrefabInstancePropertyModifications(notebook.transform);
            }
            work.binderOnly=work.binderOnly.Concat(work.journal.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Wire binding"||r.name=="Punched eyelet")).Distinct().ToArray();
            EditorUtility.SetDirty(work);
            var camera=Need<DeskCamera>();
            string profilePath=Root+"SeatedProfile.asset";
            var profile=AssetDatabase.LoadAssetAtPath<DeskCameraProfile>(profilePath);
            if(!profile){profile=Object.Instantiate(camera.profile);AssetDatabase.CreateAsset(profile,profilePath);}
            profile.minimumHorizontalAspect=16f/9f;profile.seatedFieldOfView=58;profile.seatedTarget=new Vector3(0,.83f,.022f);camera.profile=profile;EditorUtility.SetDirty(profile);EditorUtility.SetDirty(camera);
            if(PrefabUtility.IsPartOfPrefabInstance(camera))PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            if(PrefabUtility.IsPartOfPrefabInstance(work))PrefabUtility.RecordPrefabInstancePropertyModifications(work);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Whitewashed maple.mat");
            var tex=new Texture2D(512,512,TextureFormat.RGB24,false);var pixels=new Color32[512*512];
            for(int y=0;y<512;y++)for(int x=0;x<512;x++){float u=x*Mathf.PI/256f,v=y*Mathf.PI/256f;int grain=Mathf.RoundToInt(4*Mathf.Sin(31*v+2*Mathf.Sin(u))+2*Mathf.Cos(9*v+4*Mathf.Sin(2*u)));pixels[y*512+x]=new Color32((byte)(245+grain),(byte)(231+grain),(byte)(211+grain),255);}
            tex.SetPixels32(pixels);tex.Apply();string woodPath=Root+"PaleMaple.png";File.WriteAllBytes(woodPath,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(woodPath);material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(woodPath));EditorUtility.SetDirty(material);
            var sign=box.GetComponentsInChildren<TextMesh>().FirstOrDefault(l=>l.text.Contains("DREAM"));if(sign){sign.characterSize=.0033f;sign.transform.localPosition=new Vector3(0,.944f,-.096f);}
            var dome=box.Find("Domed roof");dome.localRotation=Quaternion.identity;dome.localScale=new Vector3(.204f,.056f,.167f);
            box.Find("Weighted plinth").localPosition=new Vector3(0,.04f,0);
            box.Find("Mint base").localPosition=new Vector3(0,.086f,0);
            box.Find("Fluted post").localPosition=new Vector3(0,.48f,0);
            box.Find("Fluted post").localScale=new Vector3(.053f,.40f,.053f);
            PrefabUtility.SaveAsPrefabAsset(box.gameObject,Root+"Prefabs/PastelPostMailbox.prefab");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        [MenuItem("Vintage Journal/Daydream/Apply desk revision November 7")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            if(GameObject.Find("Desk revision 20261107"))throw new InvalidOperationException("Revision already authored; use specific calibration methods.");
            Directory.CreateDirectory(Root+"Materials");Directory.CreateDirectory(Root+"Models");Directory.CreateDirectory(Root+"Prefabs");
            AssetDatabase.Refresh();
            foreach(var name in new[]{"Wooden washi atelier","WorkspaceStage4B"})
            {var go=GameObject.Find(name);if(go){var prefab=PrefabUtility.GetOutermostPrefabInstanceRoot(go);if(prefab)PrefabUtility.UnpackPrefabInstance(prefab,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);}}
            cream=Mat("Paper cream","FFF9EF");mint=Mat("Seafoam","B9E4D4");pink=Mat("Peach milk","F6C3C9");
            blue=Mat("Sky linen","B9DDEB");lilac=Mat("Lilac milk","D2C6EF");ink=Mat("Ink","536583");gold=Mat("Pearl fittings","E9C87C",.4f);gold.SetFloat("_Metallic",.45f);
            maple=Mat("Whitewashed maple","FFF9F1",.15f);maple.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Daydream/Textures/Maple.png"));
            cloth=Mat("Woven ivory","FFF9EF",.08f);cloth.SetTexture("_BaseMap",CreateWeave());
            var root=new GameObject("Desk revision 20261107").transform;
            archive=Child(root,"Previous presentation archive",Vector3.zero);archive.gameObject.SetActive(false);
            LowerCabinet();Mailbox(root);NotebookBindings();Containers(root);Passers(root);
            var help=Need<WorkspaceGameplayController>().gameObject.AddComponent<AtelierTutorial>();help.workspace=Need<WorkspaceGameplayController>();
            foreach(var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(c&&PrefabUtility.IsPartOfPrefabInstance(c))PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("docs/desk-revision");File.WriteAllText("docs/desk-revision/authoring.txt","Incremental scene revision: post mailbox, lower open pale maple rack, cloth note organizer, sticker boxes, notebook workbench, tutorial, distant passers. Original art retained in files/archive; original instance and slot IDs retained.");
            Debug.Log("[DeskRevision] Scene authoring complete");
        }
        static void LowerCabinet()
        {
            var rack=GameObject.Find("Wooden washi atelier").transform;
            var visual=rack.Find("Replaceable maple cabinet");
            if(PrefabUtility.IsPartOfPrefabInstance(visual))PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(visual.gameObject),PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var r in rack.GetComponentsInChildren<Renderer>())
            {if(!r.GetComponentInParent<PhysicalInteractable>()&&!r.GetComponent<TextMesh>())r.sharedMaterial=r.sharedMaterial&&r.sharedMaterial.GetFloat("_Metallic")>.3f?gold:maple;}
            foreach(string name in new[]{"Left solid maple side","Right solid maple side"})
            {var side=visual.Find(name);side.localScale=new Vector3(1,.302f/.370f,1);var p=side.localPosition;p.y=.151f;side.localPosition=p;}
            var crown=visual.Find("Top crown");crown.localPosition=new Vector3(0,.305f,.004f);
            visual.Find("Recessed slatted back").gameObject.SetActive(false);
            visual.Find("Continuous work shelf 2").gameObject.SetActive(false);visual.Find("Shelf lip 2").gameObject.SetActive(false);
            foreach(var label in visual.GetComponentsInChildren<TextMesh>())label.transform.localPosition=new Vector3(0,.304f,-.100f);
            for(int i=0;i<2;i++)Box(visual,"Open back rail "+i,new Vector3(0,.212f+i*.060f,.097f),new Vector3(.354f,.010f,.006f),maple);
            var shafts=rack.GetComponentsInChildren<WorkspaceShaft>().OrderBy(s=>s.shaftId).ToArray();
            for(int i=0;i<shafts.Length;i++)
            {
                int row=i<8?0:1,col=i<8?i:i-8,count=i<8?8:7;var shaft=shafts[i];
                shaft.transform.localPosition=new Vector3((col-(count-1)*.5f)*.044f,.212f+row*.060f,-.012f);
                shaft.rod.Find("Horizontal support rod").localScale=new Vector3(.0044f,.0185f,.0044f);
                foreach(var name in new[]{"Fixed left socket","Releasable right socket"})
                {var t=shaft.transform.Find(name);t.localPosition=new Vector3(name.StartsWith("Fixed")?-.019f:.019f,0,.009f);}
                var handle=shaft.rod.Find("Shaft pull handle");handle.localPosition=new Vector3(.018f,0,-.085f);
                var stem=shaft.rod.Find("Pull loop stem");stem.localPosition=new Vector3(.018f,0,-.039f);
            }
            PrefabUtility.SaveAsPrefabAsset(visual.gameObject,Root+"Prefabs/LowOpenMapleFrame.prefab");
        }
        static void Mailbox(Transform revision)
        {
            var controller=Need<CommissionDeskController>();var old=controller.mailboxRoot;
            var box=Child(revision,"Pastel floor mailbox",new Vector3(.91f,0,.22f));
            // Grounded on the platform, with a tall visible column and rounded postbox above the desk edge.
            Box(box,"Weighted plinth",new Vector3(0,.08f,0),new Vector3(.23f,.08f,.20f),cream,true);
            Box(box,"Mint base",new Vector3(0,.124f,0),new Vector3(.20f,.018f,.17f),mint);
            Piece(box,"Fluted post",PrimitiveType.Cylinder,new Vector3(0,.49f,0),new Vector3(.053f,.36f,.053f),mint);
            for(int i=0;i<3;i++)Piece(box,"Post collar",PrimitiveType.Cylinder,new Vector3(0,.18f+i*.30f,0),new Vector3(.067f,.007f,.067f),cream);
            Box(box,"Rounded postbox",new Vector3(0,.945f,0),new Vector3(.205f,.18f,.17f),pink);
            Piece(box,"Domed roof",PrimitiveType.Capsule,new Vector3(0,1.028f,0),new Vector3(.205f,.10f,.17f),pink,new Vector3(0,0,90));
            Box(box,"Front enamel door",new Vector3(0,.944f,-.089f),new Vector3(.178f,.145f,.008f),cream);
            Box(box,"Mail opening",new Vector3(0,.978f,-.095f),new Vector3(.120f,.010f,.003f),ink);
            Box(box,"Door grip",new Vector3(0,.911f,-.101f),new Vector3(.035f,.006f,.009f),gold);
            Label(box,"DREAM\nPOST",new Vector3(0,.943f,-.096f),.005f,ink.color);
            var collider=box.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,.95f,0);collider.size=new Vector3(.215f,.25f,.19f);
            if(controller.flag){controller.flag.SetParent(box,false);controller.flag.localPosition=new Vector3(.113f,.974f,0);}
            if(controller.floatingEnvelope){controller.floatingEnvelope.SetParent(box,false);controller.floatingEnvelope.localPosition=new Vector3(0,1.18f,0);}
            if(controller.stampCounter){controller.stampCounter.transform.SetParent(box,false);controller.stampCounter.transform.localPosition=new Vector3(0,.858f,-.09f);}
            controller.mailboxRoot=box;controller.mailboxCollider=collider;
            old.SetParent(archive,true);old.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(box.gameObject,Root+"Prefabs/PastelPostMailbox.prefab");
        }
        static void NotebookBindings()
        {
            var journal=Need<JournalController>();var work=journal.gameObject.AddComponent<NotebookWorkbench>();
            work.journal=journal;work.workspace=Need<WorkspaceGameplayController>();work.binderScale=journal.transform.localScale;
            work.notebooks=Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(i=>i.itemType==PhysicalItemType.TravelersNotebook).OrderBy(i=>i.assetId).ToArray();
            // Authoring binds explicit renderer references. Runtime never depends on temporary object names/material names.
            work.binderOnly=journal.GetComponentsInChildren<Renderer>().Where(r=>r!=journal.pageView.leftRenderer&&r!=journal.pageView.rightRenderer &&
                (r.name.ToLowerInvariant().Contains("ring")||r.name.ToLowerInvariant().Contains("cover")||r.name.ToLowerInvariant().Contains("leather")||r.name.ToLowerInvariant().Contains("spine")||r.name.ToLowerInvariant().Contains("hole"))).ToArray();
            var cover=Child(journal.transform,"Bound traveler presentation",Vector3.zero);
            Box(cover,"Continuous soft cover",new Vector3(0,-.006f,0),new Vector3(.86f,.021f,.57f),mint);
            Box(cover,"Sewn spine",new Vector3(0,.050f,0),new Vector3(.012f,.008f,.52f),cream);
            for(int i=0;i<9;i++)Box(cover,"Saddle stitch",new Vector3(0,.056f,-.22f+i*.055f),new Vector3(.008f,.002f,.010f),gold);
            work.boundCover=cover.gameObject;work.boundCoverRenderers=new[]{cover.GetChild(0).GetComponent<Renderer>()};cover.gameObject.SetActive(false);
            string[] names={"薄荷旅行手账 · Standard","蜜桃旅行手账 · Standard","淡紫旅行手账 · Passport","天蓝旅行手账 · Passport"};
            for(int i=0;i<work.notebooks.Length;i++)
            {var item=work.notebooks[i];item.displayName=names[i];item.allowedOperations=PhysicalOperations.All;item.interactionCollider.enabled=true;}
        }
        static Material Mat(string name,string hex,float smooth=.2f)
        {
            var path=Root+"Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};ColorUtility.TryParseHtmlString("#"+hex,out var c);m.color=c;m.SetFloat("_Smoothness",smooth);AssetDatabase.CreateAsset(m,path);return m;
        }
        static Transform Child(Transform p,string name,Vector3 pos){var t=new GameObject(name).transform;t.SetParent(p,false);t.localPosition=pos;return t;}
        static Transform Piece(Transform p,string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material mat,Vector3 euler=default)
        {var t=GameObject.CreatePrimitive(type).transform;t.name=name;t.SetParent(p,false);t.localPosition=pos;t.localScale=scale;t.localEulerAngles=euler;t.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(t.GetComponent<Collider>());return t;}
        static Transform Box(Transform p,string name,Vector3 pos,Vector3 size,Material mat,bool collide=false)
        {var t=Piece(p,name,PrimitiveType.Cube,pos,size,mat);if(collide)t.gameObject.AddComponent<BoxCollider>();return t;}
        static void Label(Transform p,string text,Vector3 pos,float size,Color color)
        {var t=Child(p,"Label",pos);var l=t.gameObject.AddComponent<TextMesh>();l.text=text;l.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");l.characterSize=size;l.fontSize=72;l.anchor=TextAnchor.MiddleCenter;l.alignment=TextAlignment.Center;l.color=color;l.GetComponent<Renderer>().sharedMaterial=l.font.material;}
        static Texture2D CreateWeave()
        {
            string path=Root+"WovenCotton.png";var t=new Texture2D(1024,1024,TextureFormat.RGBA32,false);
            var pixels=new Color32[1024*1024];for(int y=0;y<1024;y++)for(int x=0;x<1024;x++)
            {byte v=(byte)(239+((x%8<2)^(y%8<2)?9:0)+((x*17+y*31)%5));pixels[y*1024+x]=new Color32(v,v,(byte)(v-4),255);}
            t.SetPixels32(pixels);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
