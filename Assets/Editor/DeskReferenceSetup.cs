using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace VintageJournal.Editor
{
    /// <summary>Second, reference-led presentation pass. Geometry changes live in this adapter; stock retains real scale and identity.</summary>
    public static class DeskReferenceSetup
    {
        const string Scene="Assets/Scenes/VintageJournalDesk.unity",Folder="Assets/Daydream/ReferenceRoom/";
        static DeskLayoutBindings layout;static DeskReferenceBindings reference;
        static Transform root,archive,stock;
        static Material wood,cream,cloth,blue,pink,mint,ink,gold,clear;
        static readonly Color[] colors={new Color(.55f,.78f,.86f),new Color(.96f,.70f,.63f),new Color(.66f,.82f,.69f),new Color(.79f,.73f,.89f)};
        [MenuItem("Vintage Journal/Daydream/Apply reference room revision")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene(Scene);layout=Object.FindFirstObjectByType<DeskLayoutBindings>();
            if(!layout)throw new InvalidOperationException("The retained usability layout is required.");
            foreach(string sub in new[]{"Materials","Meshes","Textures"})Directory.CreateDirectory(Folder+sub);
            AssetDatabase.Refresh();stock=layout.transform;
            root=Child(null,"Reference room revision",Vector3.zero);archive=Child(root,"Retained pre-reference art",Vector3.zero);archive.gameObject.SetActive(false);
            reference=stock.GetComponent<DeskReferenceBindings>();if(!reference)reference=stock.gameObject.AddComponent<DeskReferenceBindings>();
            wood=Mat("Pale maple",new Color(.91f,.82f,.67f));cream=Mat("Cotton ivory",new Color(.98f,.96f,.90f));
            cloth=Mat("Woven ivory",new Color(.93f,.91f,.82f),AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Daydream/DeskRevision/WovenCotton.png"));
            blue=Mat("Pocket blue",colors[0]);pink=Mat("Pocket peach",colors[1]);mint=Mat("Pocket sage",colors[2]);ink=Mat("Stitch ink",new Color(.27f,.33f,.39f));gold=Mat("Satin gold",new Color(.89f,.77f,.51f));
            clear=Mat("Intentional transparent polycarbonate",new Color(.70f,.86f,.91f,.20f));
            clear.SetFloat("_Surface",1);clear.SetFloat("_ZWrite",0);clear.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);clear.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);clear.SetFloat("_Cull",0);
            clear.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");clear.SetOverrideTag("RenderType","Transparent");clear.renderQueue=3000;
            var ids=layout.placements.Select(p=>p.item.itemInstanceId).ToArray();
            ClothOrganizer();CompactDrawers();TapeCabinet();RetainObsoleteReleaseTray();SupplyBoxAndGlue();ExtractionIndex();
            layout.version=2;DeskOpaqueSurfaceSetup.Apply();
            foreach(var c in Object.FindObjectsByType<PhysicalContainer>(FindObjectsSortMode.None))c.Refresh();
            foreach(var p in layout.placements){if(p.item.CurrentSlot)p.item.transform.SetPositionAndRotation(p.item.CurrentSlot.WorldPose.position,p.item.CurrentSlot.WorldPose.rotation);EditorUtility.SetDirty(p.item);}
            ValidateCurrent();if(ids.Any(id=>!layout.placements.Any(p=>p.item.itemInstanceId==id)))throw new InvalidOperationException("An earlier entity was lost.");
            EditorUtility.SetDirty(layout);EditorUtility.SetDirty(reference);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("docs/reference-room");File.WriteAllLines("docs/reference-room/layout-authoring.txt",layout.placements.Select(p=>p.item.itemInstanceId+" | home="+p.homeSlot.slotId+" | display="+p.desiredDisplaySlot.slotId));
            Debug.Log("[ReferenceRoom] Preserved 135 entities, added 15 exclusive display rolls; compact drawers retain real-scale stock. Layout="+layout.version);
        }
        public static void Validate(){EditorSceneManager.OpenScene(Scene);layout=Object.FindFirstObjectByType<DeskLayoutBindings>();reference=Object.FindFirstObjectByType<DeskReferenceBindings>();ValidateCurrent();Debug.Log("[ReferenceRoom] Scene identity/metric validation passed.");}
        static void ValidateCurrent()
        {
            if(!layout||!reference||layout.version!=2||layout.placements.Length!=150)throw new InvalidOperationException("Reference layout is incomplete.");
            if(layout.placements.Select(p=>p.item.itemInstanceId).Distinct().Count()!=150||layout.placements.Select(p=>p.homeSlot.slotId).Distinct().Count()!=150)throw new InvalidOperationException("Duplicate reference stock or permanent home.");
            if(layout.placements.Count(p=>p.item.itemType==PhysicalItemType.Tape)!=45)throw new InvalidOperationException("Forty-five distinct tape rolls are required.");
            if(reference.clothBag.GetComponentsInChildren<PhysicalStorageSlot>(true).Length!=24)throw new InvalidOperationException("All 24 original notes must live in the cloth pockets.");
            if(layout.placements.Where(p=>p.item.itemType==PhysicalItemType.ReleaseBook).Any(p=>p.homeSlot.container!=reference.releaseShelf||p.desiredDisplaySlot!=p.homeSlot||!p.relocateStockOnMigration||!p.relocateDesktopOnMigration||string.IsNullOrEmpty(p.releaseBookId)))throw new InvalidOperationException("Release books require an explicit open bottom-shelf address.");
            foreach(var p in layout.placements)if(p.item.transform.lossyScale!=Vector3.one)throw new InvalidOperationException("Stock scale changed: "+p.item.itemInstanceId);
            if(reference.glueShell.GetComponentsInChildren<MeshRenderer>().Length<3)throw new InvalidOperationException("The glue dispenser requires a real transparent shell.");
        }
        static void ClothOrganizer()
        {
            var old=layout.desktopNotes;var bag=Container(root,"Hanging cloth note organizer","布艺分格收纳包",new Vector3(-.015f,.780f,.275f));
            reference.clothBag=bag;layout.desktopNotes=bag;
            Box(bag.transform,"Woven backing",new Vector3(0,.119f,.014f),new Vector3(.354f,.238f,.003f),cloth);
            Tube(bag.transform,"Top wooden dowel",new Vector3(0,.240f,.014f),.004f,.378f,wood,Vector3.right);
            for(int side=-1;side<=1;side+=2){Tube(bag.transform,"Stand upright "+side,new Vector3(side*.187f,.133f,.026f),.0025f,.266f,wood,Vector3.up);Box(bag.transform,"Stand foot "+side,new Vector3(side*.187f,-.014f,.012f),new Vector3(.048f,.010f,.060f),wood,true);Link(bag.transform,"Hanging cord "+side,new Vector3(side*.148f,.240f,.014f),new Vector3(0,.320f,.014f),.0011f,cream);}
            var notes=layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Note&&p.homeSlot.container==old||p.item.itemType==PhysicalItemType.Note&&p.homeSlot.transform.IsChildOf(bag.transform)).OrderBy(p=>p.item.itemInstanceId).ToArray();
            if(notes.Length!=24)throw new InvalidOperationException("Expected 24 desktop note instances.");
            for(int cell=0;cell<8;cell++){
                float x=(cell%4-1.5f)*.085f,y=.048f+cell/4*.091f;var pocket=Container(bag.transform,"Sewn pocket "+cell,"布袋小兜 "+(cell+1),new Vector3(x,y,0));
                var accent=new[]{blue,pink,mint,cream}[cell%4];
                Box(pocket.transform,"Soft pocket belly",new Vector3(0,-.017f,-.016f),new Vector3(.080f,.056f,.003f),accent);
                Box(pocket.transform,"Folded top binding",new Vector3(0,.012f,-.0176f),new Vector3(.080f,.004f,.002f),cream);
                Box(pocket.transform,"Bottom gusset",new Vector3(0,-.045f,-.006f),new Vector3(.080f,.003f,.024f),accent);
                for(int s=-1;s<=1;s+=2){Box(pocket.transform,"Side gusset "+s,new Vector3(s*.0385f,-.017f,-.004f),new Vector3(.003f,.056f,.025f),accent);for(int n=0;n<9;n++)Box(pocket.transform,"Visible seam "+s+"-"+n,new Vector3(s*.035f,-.041f+n*.006f,-.018f),new Vector3(.0006f,.0016f,.0004f),cream);}
                // Small embroidered 3D animal patch, separate from the usable note instances.
                if(cell%3==0){Sphere(pocket.transform,"Patch face",new Vector3(0,-.016f,-.019f),new Vector3(.027f,.024f,.0012f),cream);for(int s=-1;s<=1;s+=2){Sphere(pocket.transform,"Patch ear "+s,new Vector3(s*.012f,-.005f,-.019f),new Vector3(.010f,.010f,.0012f),cream);Sphere(pocket.transform,"Patch eye "+s,new Vector3(s*.006f,-.015f,-.020f),Vector3.one*.0018f,ink);}}
                for(int layer=0;layer<3;layer++){var p=notes[cell+layer*8];MoveSlot(p.homeSlot,pocket,new Vector3((layer-1)*.0015f,.010f,-.010f-layer*.0012f),Quaternion.Euler(-90,0,0));BindHome(p,p.homeSlot);}
            }
            if(old!=bag)Archive(old.transform);
        }
        static void CompactDrawers()
        {
            foreach(var d in new[]{layout.leftDrawer,layout.rightDrawer}){
                d.transform.position=new Vector3(d==layout.leftDrawer?-.24f:.24f,.620f,-.20f);d.transform.localScale=Vector3.one;d.travel=.278f;
                foreach(Transform c in d.transform)if(c!=d.slide&&!c.name.StartsWith("Ref ",StringComparison.Ordinal))c.gameObject.SetActive(false);
                foreach(Transform c in d.slide)if(!c.GetComponent<PhysicalContainer>()&&!c.GetComponent<PhysicalStorageSlot>()&&!c.name.StartsWith("Ref ",StringComparison.Ordinal))c.gameObject.SetActive(false);
                Box(d.transform,"Ref top rail",new Vector3(0,.073f,0),new Vector3(.3156f,.006f,.270f),wood,true);
                for(int s=-1;s<=1;s+=2)Box(d.transform,"Ref guide "+s,new Vector3(s*.1558f,.035f,0),new Vector3(.004f,.070f,.270f),wood,true);
                Box(d.slide,"Ref tray floor",Vector3.zero,new Vector3(.3076f,.004f,.264f),wood,true);
                for(int s=-1;s<=1;s+=2){Box(d.slide,"Ref tray side "+s,new Vector3(s*.1518f,.034f,0),new Vector3(.004f,.068f,.264f),wood,true);Box(d.slide,"Ref tray end "+s,new Vector3(0,.034f,s*.130f),new Vector3(.3076f,.068f,.004f),wood,true);}
                Box(d.slide,"Ref pull handle",new Vector3(0,.039f,-.142f),new Vector3(.078f,.010f,.020f),gold,true);d.SetOpenAmount(d.OpenAmount);EditorUtility.SetDirty(d);
            }
            var tape=layout.leftDrawer.slide.Find("Tape archive box").GetComponent<PhysicalContainer>();RefBox(tape,new Vector3(.079f,.004f,0),new Vector3(.143f,.060f,.246f),mint);
            var paper=layout.leftDrawer.slide.Find("Refill archive box").GetComponent<PhysicalContainer>();RefBox(paper,new Vector3(-.0745f,.004f,0),new Vector3(.153f,.025f,.216f),cream);
            var pages=layout.placements.Where(p=>p.item.itemType==PhysicalItemType.LoosePage).OrderBy(p=>p.item.itemInstanceId).ToArray();
            for(int i=0;i<pages.Length;i++){MoveSlot(pages[i].homeSlot,paper,new Vector3(0,.0045f+i*.0012f,0),Quaternion.identity);BindHome(pages[i],pages[i].homeSlot);}
            var rollGroups=new[]{layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Tape&&p.item.itemInstanceId.Contains("usability-archive")).ToArray(),layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Tape&&!p.item.itemInstanceId.Contains("usability-archive")&&!p.item.itemInstanceId.Contains("reference-display")).ToArray(),layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Tape&&p.item.itemInstanceId.Contains("reference-display")).ToArray()};
            float[] tierHeight={.045f,.011f,.028f};
            for(int group=0;group<rollGroups.Length;group++){var arr=rollGroups[group].OrderBy(p=>p.item.itemInstanceId).ToArray();for(int i=0;i<arr.Length;i++){MoveSlot(arr[i].homeSlot,tape,new Vector3((i%3-1)*.047f,tierHeight[group],(i/3-2)*.047f),Quaternion.identity);if(arr[i].item.CurrentSlot==arr[i].homeSlot)arr[i].item.transform.SetPositionAndRotation(arr[i].homeSlot.WorldPose.position,arr[i].homeSlot.WorldPose.rotation);}}
            var stickers=layout.rightDrawer.slide.GetComponentsInChildren<PhysicalContainer>(true).Where(c=>c.name.StartsWith("SmallNoteBox_",StringComparison.Ordinal)).OrderBy(c=>c.name).ToArray();
            for(int n=0;n<stickers.Length;n++){RefBox(stickers[n],new Vector3(-.078f,.004f+n*.019f,.041f),new Vector3(.128f,.014f,.153f),new[]{pink,mint,blue}[n]);foreach(var p in layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Sticker&&p.homeSlot.container==stickers[n]))BindHome(p,p.homeSlot);}
            var noteBoxes=layout.rightDrawer.slide.GetComponentsInChildren<PhysicalContainer>(true).Where(c=>c.name.StartsWith("Boxed note refills ",StringComparison.Ordinal)).OrderBy(c=>c.name).ToArray();
            for(int n=0;n<noteBoxes.Length;n++){RefBox(noteBoxes[n],new Vector3(.095f,.004f+n*.019f,.072f),new Vector3(.090f,.016f,.090f),new[]{pink,mint,blue}[n]);Box(noteBoxes[n].transform,"Ref dense pad",new Vector3(0,.005f,0),new Vector3(.075f,.007f,.075f),cream);var notes=layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Note&&p.homeSlot.container==noteBoxes[n]).OrderBy(p=>p.item.itemInstanceId).ToArray();for(int i=0;i<notes.Length;i++){MoveSlot(notes[i].homeSlot,noteBoxes[n],new Vector3(0,.009f+i*.0008f,0),Quaternion.identity);BindHome(notes[i],notes[i].homeSlot);}}
            var pens=layout.rightDrawer.slide.Find("Pen case").GetComponent<PhysicalContainer>();RefBox(pens,new Vector3(0,.004f,-.087f),new Vector3(.292f,.018f,.064f),blue);
            var penItems=layout.placements.Where(p=>p.item.assetId.StartsWith("atelier_pen_",StringComparison.Ordinal)).OrderBy(p=>p.item.assetId).ToArray();for(int i=0;i<penItems.Length;i++){MoveSlot(penItems[i].homeSlot,pens,new Vector3(i<3?-.073f:.073f,.008f,(i%3-1)*.018f),Quaternion.Euler(0,90,0));BindHome(penItems[i],penItems[i].homeSlot);}
        }
        static void TapeCabinet()
        {
            var cabinet=GameObject.Find("Wooden washi atelier").transform;reference.cabinet=cabinet;
            var old=cabinet.Find("Replaceable maple cabinet");if(old)Archive(old);
            var frame=Child(cabinet,"Ref open maple cabinet",Vector3.zero);
            for(int s=-1;s<=1;s+=2)Box(frame,"Maple side "+s,new Vector3(s*.187f,.164f,.004f),new Vector3(.012f,.328f,.198f),wood,true);
            Box(frame,"Crown",new Vector3(0,.328f,.004f),new Vector3(.388f,.010f,.204f),wood,true);Box(frame,"Plinth",new Vector3(0,.005f,.004f),new Vector3(.382f,.010f,.198f),wood,true);
            Box(frame,"Recessed wood back",new Vector3(0,.162f,.098f),new Vector3(.362f,.316f,.004f),wood);
            var bottom=Container(cabinet,"Reference lowest open release shelf","最低层 · 离型本开放格",new Vector3(0,.008f,0));reference.releaseShelf=bottom;
            var access=bottom.GetComponent<PhysicalStorageAccessPath>();if(!access)access=bottom.gameObject.AddComponent<PhysicalStorageAccessPath>();
            access.localExitOffset=new Vector3(0,0,-.20f);EditorUtility.SetDirty(access);
            Box(bottom.transform,"Shelf floor",Vector3.zero,new Vector3(.352f,.004f,.175f),wood,true);
            var books=layout.placements.Where(p=>p.item.itemType==PhysicalItemType.ReleaseBook).OrderBy(p=>p.item.itemInstanceId).ToArray();
            for(int n=0;n<books.Length;n++){var p=books[n];string legacy=p.desiredDisplaySlot.slotId;MoveSlot(p.homeSlot,bottom,new Vector3(n==0?-.077f:.077f,.0085f,0),Quaternion.identity);p.legacySlotIds=p.legacySlotIds.Concat(new[]{legacy}).Distinct().ToArray();p.desiredDisplaySlot=p.homeSlot;p.relocateStockOnMigration=p.relocateDesktopOnMigration=true;p.releaseBookId=p.item.GetComponent<ReleaseBookView>().bookId;BindHome(p,p.homeSlot);}
            foreach(var d in cabinet.GetComponentsInChildren<WorkspaceDrawer>(true))d.gameObject.SetActive(false);
            var originals=layout.placements.Where(p=>p.item.itemType==PhysicalItemType.Tape&&!p.item.itemInstanceId.Contains("usability-archive")&&!p.item.itemInstanceId.Contains("reference-display")).OrderBy(p=>p.item.itemInstanceId).ToArray();
            var shafts=originals.Select(p=>p.desiredDisplaySlot.accessShaft).ToArray();
            var entries=layout.placements.ToList();var added=new List<PhysicalInteractable>();
            for(int row=0;row<3;row++){
                float y=.095f+row*.080f;Box(frame,"Work shelf "+row,new Vector3(0,y-.027f,.014f),new Vector3(.354f,.006f,.166f),wood,true);Tube(frame,"Front sample dowel "+row,new Vector3(0,y-.012f,-.089f),.0025f,.354f,wood,Vector3.right);
                for(int col=0;col<10;col++){
                    int i=row*5+col/2;float x=(col-4.5f)*.033f;DeskLayoutPlacement p;
                    if(col%2==0){p=originals[i];var shaft=shafts[i];shaft.transform.localPosition=new Vector3(x,y,-.012f);shaft.transform.localRotation=Quaternion.identity;}
                    else{
                        string id="physical-"+originals[i].item.assetId+"-reference-display-"+(i+1).ToString("D2");p=entries.FirstOrDefault(e=>e.item.itemInstanceId==id);
                        var shaft=NewShaft(cabinet,i,new Vector3(x,y,-.012f));
                        if(p==null){var item=Object.Instantiate(originals[i].item.gameObject,stock).GetComponent<PhysicalInteractable>();item.itemInstanceId=id;item.name="Reference exclusive display roll "+(i+1);item.homeSlot=null;item.homeAnchor=null;item.transform.localScale=Vector3.one;var tape=layout.leftDrawer.slide.Find("Tape archive box").GetComponent<PhysicalContainer>();var home=Slot(tape.transform,tape,"reference-home-"+id,PhysicalItemType.Tape,new Vector3((i%3-1)*.047f,.028f,(i/3-2)*.047f));p=new DeskLayoutPlacement{item=item,homeSlot=home,desiredDisplaySlot=shaft.slot};entries.Add(p);}else p.desiredDisplaySlot=shaft.slot;
                        added.Add(p.item);
                    }
                    SetDisplay(p);Pattern(frame,p,row,col,new Vector3(x,y-.012f,-.101f));
                }
            }
            layout.placements=entries.ToArray();reference.addedRolls=added.ToArray();
            reference.showcaseSlots=entries.Where(p=>p.item.itemType==PhysicalItemType.Tape&&p.desiredDisplaySlot.accessShaft).Select(p=>p.desiredDisplaySlot).ToArray();
            reference.releaseShelfSlots=books.Select(p=>p.homeSlot).ToArray();reference.sampleStripViews=frame.GetComponentsInChildren<TapePatternPreview>(true);
        }
        static WorkspaceShaft NewShaft(Transform parent,int i,Vector3 pos)
        {
            string id="reference-rack-shaft-"+(i+1).ToString("D2");var t=Child(parent,id,pos);var c=t.GetComponent<PhysicalContainer>();if(!c)c=t.gameObject.AddComponent<PhysicalContainer>();c.isOpen=true;c.canClose=false;c.displayName="胶带陈列横轴 "+(i+1);
            var rod=Child(t,"Rod",Vector3.zero);Tube(rod,"Support pin",Vector3.zero,.002f,.026f,gold,Vector3.right);Box(rod,"Pull handle",new Vector3(.014f,0,-.035f),new Vector3(.007f,.009f,.012f),wood,true);
            var slot=Slot(rod,c,"reference-rack-display-"+(i+1).ToString("D2"),PhysicalItemType.Tape,Vector3.zero,Quaternion.Euler(0,0,90));slot.permanentReservation=false;
            var shaft=t.GetComponent<WorkspaceShaft>();if(!shaft)shaft=t.gameObject.AddComponent<WorkspaceShaft>();shaft.shaftId=id;shaft.rod=rod;shaft.slot=slot;shaft.slideAxis=Vector3.back;shaft.travel=.13f;slot.accessShaft=shaft;shaft.SetOpenAmount(0);return shaft;
        }
        static void RetainObsoleteReleaseTray()
        {
            // Two purely decorative DaydreamRoom props occupy the entrances of the new A/B shelf slots.
            // Bind their saved scene identities, rather than disabling objects by display names.
            foreach(ulong localId in new[]{438968679UL,1405284294UL}) // Metal tray and glass clip-box originals.
            {
                string key="GlobalObjectId_V1-2-"+AssetDatabase.AssetPathToGUID(Scene)+"-"+localId+"-0";
                if(!GlobalObjectId.TryParse(key,out var id))throw new InvalidOperationException("Cannot resolve a retained shelf-decor scene identity.");
                var decor=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if(!decor||decor.scene.path!=Scene)throw new InvalidOperationException("An explicitly bound historical shelf prop is missing.");
                if(decor.GetComponentsInChildren<PhysicalInteractable>(true).Length!=0||decor.GetComponentsInChildren<PhysicalStorageSlot>(true).Length!=0||
                    decor.GetComponentsInChildren<PhysicalContainer>(true).Length!=0)
                    throw new InvalidOperationException("A historical shelf prop contains stateful stock; refusing to archive it.");
                Archive(decor.transform);EditorUtility.SetDirty(decor);
            }
        }
        static void Pattern(Transform parent,DeskLayoutPlacement p,int row,int col,Vector3 pos)
        {
            var t=Child(parent,"Pattern swatch "+row+"-"+col,pos);float width=p.item.dimensions.size.y,length=.034f;var def=DecorationCatalog.Load().Find(p.item.assetId);var mat=Mat("Swatch "+p.item.assetId,Color.white,def.texture);mat.SetFloat("_Cull",0);
            float repeat=length/(width*Mathf.Max(1,(float)def.texture.width/def.texture.height));
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"Meshes/Swatch_"+row+"_"+col+".asset");if(!mesh){mesh=new Mesh{name="Non-stock continuous pattern sample"};mesh.vertices=new[]{new Vector3(-width*.5f,0,0),new Vector3(-width*.5f,-length,0),new Vector3(width*.5f,-length,0),new Vector3(width*.5f,0,0)};mesh.uv=new[]{new Vector2(0,0),new Vector2(repeat,0),new Vector2(repeat,1),new Vector2(0,1)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();AssetDatabase.CreateAsset(mesh,Folder+"Meshes/Swatch_"+row+"_"+col+".asset");}
            var f=t.GetComponent<MeshFilter>();if(!f)f=t.gameObject.AddComponent<MeshFilter>();f.sharedMesh=mesh;var r=t.GetComponent<MeshRenderer>();if(!r)r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
            var hit=t.GetComponent<BoxCollider>();if(!hit)hit=t.gameObject.AddComponent<BoxCollider>();hit.size=new Vector3(width,length,.001f);hit.center=new Vector3(0,-length*.5f,0);
            var preview=t.GetComponent<TapePatternPreview>();if(!preview)preview=t.gameObject.AddComponent<TapePatternPreview>();preview.source=p.item;preview.displaySlot=p.desiredDisplaySlot;preview.preview=r;preview.hitCollider=hit;
        }
        static void SupplyBoxAndGlue()
        {
            layout.desktopBox.transform.position=new Vector3(.505f,.760f,-.115f);
            var glue=layout.glue;var old=glue.visualRoot;var visual=Child(glue.transform,"Reference glue dispenser presentation",Vector3.zero);glue.visualRoot=visual;glue.RebindVisual(visual);if(old!=visual)Archive(old);
            var polygon=new[]{new Vector2(-.026f,-.006f),new Vector2(-.021f,-.012f),new Vector2(-.006f,-.015f),new Vector2(.011f,-.011f),new Vector2(.027f,-.003f),new Vector2(.027f,.003f),new Vector2(.011f,.011f),new Vector2(-.006f,.015f),new Vector2(-.021f,.012f),new Vector2(-.026f,.006f)};
            Extrusion(visual,"Transparent top shell",polygon,.0088f,.0096f,clear);Extrusion(visual,"Transparent bottom shell",polygon,-.0096f,-.0088f,clear);Extrusion(visual,"Transparent curved body",polygon,-.0088f,.0088f,clear,false);
            for(int n=0;n<polygon.Length;n++){var a=polygon[n];var b=polygon[(n+1)%polygon.Length];Link(visual,"Shell edge "+n,new Vector3(a.x,.0098f,a.y),new Vector3(b.x,.0098f,b.y),.00035f,blue);}
            Wheel(visual,"Supply reel",new Vector3(-.013f,0,0),.010f,cream,24);Wheel(visual,"Takeup gear",new Vector3(.0075f,0,0),.0072f,cream,20);
            Link(visual,"Adhesive film top",new Vector3(-.013f,.004f,-.009f),new Vector3(.023f,.004f,-.002f),.00065f,cream);
            Link(visual,"Adhesive film return",new Vector3(.023f,.004f,.002f),new Vector3(.0075f,.004f,.0065f),.00055f,mint);
            Tube(visual,"Application roller",new Vector3(.025f,0,0),.003f,.007f,gold,Vector3.forward);Box(visual,"Glue application head",new Vector3(.025f,-.003f,0),new Vector3(.004f,.006f,.009f),cream);
            reference.glueShell=visual;reference.glueShellRenderers=new[]{"Transparent top shell","Transparent bottom shell","Transparent curved body"}.Select(n=>visual.Find(n).GetComponent<Renderer>()).ToArray();
            var desired=layout.placements.First(p=>p.item==glue).desiredDisplaySlot;desired.transform.position=new Vector3(.425f,.774f,-.235f);SetDisplay(layout.placements.First(p=>p.item==glue));
        }
        static void Wheel(Transform p,string name,Vector3 center,float radius,Material material,int teeth)
        {
            var t=Child(p,name,center);Tube(t,"Hub",Vector3.zero,.0023f,.012f,gold,Vector3.up);
            for(int i=0;i<40;i++){float a=i*Mathf.PI/20,b=(i+1)*Mathf.PI/20;Link(t,"Reel ring "+i,new Vector3(Mathf.Cos(a)*radius,.002f,Mathf.Sin(a)*radius),new Vector3(Mathf.Cos(b)*radius,.002f,Mathf.Sin(b)*radius),.0012f,material);}
            for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Link(t,"Spoke "+i,Vector3.zero,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(radius-.001f),.0007f,material);}
            for(int i=0;i<teeth;i++){float a=i*Mathf.PI*2/teeth;var tooth=Box(t,"Gear tooth "+i,new Vector3(Mathf.Cos(a)*(radius+.0005f),.001f,Mathf.Sin(a)*(radius+.0005f)),new Vector3(.0012f,.004f,.0018f),material);tooth.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);}
        }
        static void ExtractionIndex()
        {
            var index=stock.GetComponent<DrawerStockIndex>();if(!index)index=stock.gameObject.AddComponent<DrawerStockIndex>();index.workspace=stock.GetComponent<WorkspaceGameplayController>();index.layout=layout;
            var surface=Object.FindFirstObjectByType<DeskPlacementSurface>();var point=new Vector3(-.435f,.76f,-.17f);Box(root,"Desk side extraction pad",point,new Vector3(.164f,.0004f,.224f),mint);
            // A coloured work mat, rather than a white paper-looking rectangle; all decoration is visual only.
            for(int s=-1;s<=1;s+=2){Box(root,"Extraction mat long border "+s,point+new Vector3(s*.080f,.000205f,0),new Vector3(.0014f,.00001f,.222f),blue);Box(root,"Extraction mat short border "+s,point+new Vector3(0,.000205f,s*.110f),new Vector3(.160f,.00001f,.0014f),blue);}
            MatLabel(point+new Vector3(-.026f,.000205f,-.098f));
            var slots=new List<PhysicalStorageSlot>();foreach(var type in new[]{PhysicalItemType.Tape,PhysicalItemType.Sticker,PhysicalItemType.Note,PhysicalItemType.LoosePage,PhysicalItemType.Tool}){var sample=layout.placements.First(p=>p.item.itemType==type).item;var pose=surface.RestingPose(sample,point,Quaternion.identity);var slot=Slot(root,null,"reference-extract-"+type,type,Vector3.zero);slot.transform.SetPositionAndRotation(pose.position,pose.rotation);slot.permanentReservation=false;slots.Add(slot);}index.extractionSlots=slots.ToArray();reference.stockIndex=index;EditorUtility.SetDirty(index);
            reference.leftDrawerBody=DrawerBody(layout.leftDrawer);reference.rightDrawerBody=DrawerBody(layout.rightDrawer);
        }
        static Renderer[] DrawerBody(WorkspaceDrawer d)=>d.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Ref top rail"||r.name.StartsWith("Ref guide ",StringComparison.Ordinal)||r.name.StartsWith("Ref tray ",StringComparison.Ordinal)).ToArray();
        static void MatLabel(Vector3 origin)
        {
            // Simple geometric strokes read 取用 without requiring a machine-specific Chinese 3D font.
            Vector4[] strokes={new Vector4(0,9,5,9),new Vector4(1,9,1,1),new Vector4(4,9,4,0),new Vector4(1,7,4,7),new Vector4(1,5,4,5),new Vector4(0,2,5,3),new Vector4(5,8,9,8),new Vector4(9,8,8,4),new Vector4(8,4,5,0),new Vector4(5.5f,6,9.5f,0),new Vector4(17,0,17,9),new Vector4(17,9,25,9),new Vector4(25,9,25,0),new Vector4(25,0,23.5f,1),new Vector4(17,6,25,6),new Vector4(17,3,25,3),new Vector4(21,9,21,0)};
            for(int n=0;n<strokes.Length;n++){var s=strokes[n];var a=origin+new Vector3(s.x*.0017f,0,s.y*.0017f);var b=origin+new Vector3(s.z*.0017f,0,s.w*.0017f);var t=Box(root,"Extraction mat label stroke "+n,(a+b)*.5f,new Vector3(Vector3.Distance(a,b),.00001f,.00055f),ink);t.localRotation=Quaternion.Euler(0,-Mathf.Atan2(b.z-a.z,b.x-a.x)*Mathf.Rad2Deg,0);}
        }
        static void RefBox(PhysicalContainer box,Vector3 position,Vector3 size,Material material)
        {
            foreach(Transform c in box.transform)if(!c.GetComponent<PhysicalStorageSlot>()&&!c.name.StartsWith("Ref ",StringComparison.Ordinal))c.gameObject.SetActive(false);
            box.transform.localPosition=position;box.transform.localRotation=Quaternion.identity;box.isOpen=true;box.canClose=false;
            Box(box.transform,"Ref floor",Vector3.zero,new Vector3(size.x,.002f,size.z),material);
            for(int s=-1;s<=1;s+=2){Box(box.transform,"Ref side "+s,new Vector3(s*(size.x*.5f-.001f),size.y*.5f,0),new Vector3(.002f,size.y,size.z),material);Box(box.transform,"Ref end "+s,new Vector3(0,size.y*.5f,s*(size.z*.5f-.001f)),new Vector3(size.x,size.y,.002f),material);}
        }
        static void MoveSlot(PhysicalStorageSlot s,PhysicalContainer c,Vector3 p,Quaternion q){s.transform.SetParent(c.transform,false);s.container=c;s.transform.localPosition=p;s.transform.localRotation=q;EditorUtility.SetDirty(s);}
        static void BindHome(DeskLayoutPlacement p,PhysicalStorageSlot home){p.item.ConfigureHome(home);p.homeSlot=home;p.desiredDisplaySlot=home;}
        static void SetDisplay(DeskLayoutPlacement p){var home=p.homeSlot;p.item.ConfigureHome(p.desiredDisplaySlot);p.item.homeSlot=home;p.item.homeAnchor=home.transform;home.TryReserve(p.item);}
        static PhysicalStorageSlot Slot(Transform p,PhysicalContainer c,string id,PhysicalItemType type,Vector3 pos,Quaternion? rotation=null){var t=Child(p,id,pos);t.localRotation=rotation??Quaternion.identity;var s=t.GetComponent<PhysicalStorageSlot>();if(!s)s=t.gameObject.AddComponent<PhysicalStorageSlot>();s.slotId=id;s.container=c;s.acceptedType=type;s.permanentReservation=true;return s;}
        static PhysicalContainer Container(Transform p,string name,string title,Vector3 pos){var t=Child(p,name,pos);var c=t.GetComponent<PhysicalContainer>();if(!c)c=t.gameObject.AddComponent<PhysicalContainer>();c.displayName=title;c.isOpen=true;c.canClose=false;return c;}
        static Transform Child(Transform p,string name,Vector3 pos){Transform t=p?p.Find(name):GameObject.Find(name)?.transform;if(!t){t=new GameObject(name).transform;t.SetParent(p,false);}t.localPosition=pos;t.gameObject.SetActive(true);return t;}
        static void Archive(Transform t){if(!t||t.IsChildOf(archive))return;t.SetParent(archive,true);t.gameObject.SetActive(false);}
        static Material Mat(string name,Color color,Texture texture=null){var path=Folder+"Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}m.SetColor("_BaseColor",color);m.SetTexture("_BaseMap",texture);m.SetFloat("_Smoothness",.25f);EditorUtility.SetDirty(m);return m;}
        static Transform Box(Transform p,string name,Vector3 pos,Vector3 size,Material mat,bool hit=false){return Piece(p,name,PrimitiveType.Cube,pos,size,mat,hit);}
        static Transform Sphere(Transform p,string name,Vector3 pos,Vector3 size,Material mat){return Piece(p,name,PrimitiveType.Sphere,pos,size,mat,false);}
        static void Tube(Transform p,string name,Vector3 pos,float radius,float length,Material mat,Vector3 axis){var t=Piece(p,name,PrimitiveType.Cylinder,pos,new Vector3(radius*2,length*.5f,radius*2),mat,false);t.localRotation=Quaternion.FromToRotation(Vector3.up,axis);}
        static Transform Piece(Transform p,string name,PrimitiveType type,Vector3 pos,Vector3 size,Material mat,bool hit){var t=p.Find(name);if(!t){t=GameObject.CreatePrimitive(type).transform;t.name=name;t.SetParent(p,false);}t.gameObject.SetActive(true);t.localPosition=pos;t.localRotation=Quaternion.identity;t.localScale=size;t.GetComponent<Renderer>().sharedMaterial=mat;var c=t.GetComponent<Collider>();if(!hit&&c)Object.DestroyImmediate(c);return t;}
        static void Link(Transform p,string name,Vector3 a,Vector3 b,float radius,Material m){Tube(p,name,(a+b)*.5f,radius,(b-a).magnitude,m,(b-a).normalized);}
        static void Extrusion(Transform p,string name,Vector2[] poly,float bottom,float top,Material m,bool caps=true)
        {
            string path=Folder+"Meshes/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){var v=new List<Vector3>();var tri=new List<int>();for(int n=0;n<poly.Length;n++){v.Add(new Vector3(poly[n].x,bottom,poly[n].y));v.Add(new Vector3(poly[n].x,top,poly[n].y));}int count=poly.Length;for(int n=0;n<count;n++){int next=(n+1)%count;tri.AddRange(new[]{n*2,next*2,n*2+1,n*2+1,next*2,next*2+1});}if(caps){v.Add(new Vector3(0,bottom,0));v.Add(new Vector3(0,top,0));for(int n=0;n<count;n++){int next=(n+1)%count;tri.AddRange(new[]{2*count,n*2,next*2,2*count+1,next*2+1,n*2+1});}}mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}var t=Child(p,name,Vector3.zero);var f=t.GetComponent<MeshFilter>();if(!f)f=t.gameObject.AddComponent<MeshFilter>();f.sharedMesh=mesh;var r=t.GetComponent<MeshRenderer>();if(!r)r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=m;r.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
}
