using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace VintageJournal.Editor
{
    public static partial class DeskRevisionSetup
    {
        static void Containers(Transform revision)
        {
            var fixtures=GameObject.Find("WorkspaceStage4B").transform;
            var oldRack=fixtures.Find("HangingStickerRack");var bag=Child(fixtures,"Cloth note pocket organizer",oldRack.localPosition);
            var container=bag.gameObject.AddComponent<PhysicalContainer>();container.displayName="布艺便签挂袋";container.canClose=false;container.isOpen=true;
            // The existing tall position is reused; note faces sit above the notebook shelf, never behind its colliders.
            Box(bag,"Woven cotton backing",new Vector3(0,.422f,.020f),new Vector3(.320f,.298f,.003f),cloth);
            var pole=Piece(bag,"Maple hanging rod",PrimitiveType.Cylinder,new Vector3(0,.580f,.023f),new Vector3(.009f,.174f,.009f),maple,new Vector3(0,0,90));
            for(int sign=-1;sign<=1;sign+=2)
            {
                Box(bag,"Support",new Vector3(sign*.174f,.287f,.045f),new Vector3(.006f,.574f,.006f),cream,true);
                Box(bag,"Foot",new Vector3(sign*.174f,.005f,.023f),new Vector3(.060f,.010f,.055f),maple,true);
                Link(bag,"Cotton cord",new Vector3(sign*.140f,.580f,.023f),new Vector3(sign*.095f,.636f,.023f),.0014f,cream);
            }
            Label(bag,"LITTLE NOTES",new Vector3(0,.560f,.016f),.0025f,ink.color);
            var notes=Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(i=>i.itemType==PhysicalItemType.Note).OrderBy(i=>i.assetId).ToArray();
            var accents=new[]{blue,pink,mint,lilac,pink,blue,mint,lilac};
            for(int i=0;i<notes.Length;i++)
            {
                int row=i/3,col=i%3;var center=new Vector3((col-1)*.103f,.330f+row*.090f,-.008f);
                // Open-top pockets, seam strips and a shallow belly, leaving the note's upper half clickable.
                var pocket=Child(bag,"Sewn pocket "+i,center-new Vector3(0,.024f,0));
                Box(pocket,"Pocket front",new Vector3(0,0,-.013f),new Vector3(.090f,.045f,.002f),accents[i%accents.Length]);
                Box(pocket,"Folded hem",new Vector3(0,.022f,-.014f),new Vector3(.090f,.003f,.0025f),cream);
                for(int side=-1;side<=1;side+=2)
                {Box(pocket,"Pocket gusset",new Vector3(side*.044f,0,-.004f),new Vector3(.002f,.045f,.019f),accents[i%accents.Length]);
                    for(int j=0;j<8;j++)Box(pocket,"Stitch",new Vector3(side*.041f,-.018f+j*.005f,-.0144f),new Vector3(.0008f,.0018f,.0003f),cream);}
                // Retain every original slot ID; saved stock automatically follows its new home transform.
                var slot=notes[i].homeSlot;slot.transform.SetParent(bag,false);slot.container=container;slot.transform.localPosition=center;slot.transform.localRotation=Quaternion.Euler(-90,0,0);
                notes[i].ConfigureHome(slot);notes[i].transform.SetPositionAndRotation(slot.WorldPose.position,slot.WorldPose.rotation);
            }
            var boxes=Enumerable.Range(1,3).Select(i=>fixtures.Find("SmallNoteBox_"+i)).ToArray();
            var boxNames=new[]{"白日梦贴纸盒","可爱生活贴纸盒","旅行票据贴纸盒"};
            for(int i=0;i<3;i++)
            {
                var c=boxes[i].GetComponent<PhysicalContainer>();c.displayName=boxNames[i];c.isOpen=true;c.canClose=false;
                foreach(var t in boxes[i].GetComponentsInChildren<Transform>(true))if(t.name=="Stationary continuous note pad"||t.name=="Style sticker label")t.gameObject.SetActive(false);
                foreach(var label in boxes[i].GetComponentsInChildren<TextMesh>())label.text=new[]{"DREAM","LITTLE JOYS","TRAVEL"}[i];
            }
            GenerateStickerAssets();
            var stickers=Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(i=>i.itemType==PhysicalItemType.Sticker).OrderBy(i=>i.itemInstanceId).ToList();
            foreach(var def in DecorationCatalog.Load().assets.Where(d=>d.assetId.StartsWith("dream_sticker_")))stickers.Add(NewSticker(def,fixtures));
            // Six cells per box. Small stacks expose one real instance at a time; removing one reveals the next.
            var counts=new int[3];
            foreach(var item in stickers)
            {
                int theme=item.assetId.StartsWith("dream_sticker_")?(int.Parse(item.assetId.Substring(item.assetId.Length-2))-1)/6:counts.Sum()%3;
                int n=counts[theme]++,cell=n%6,layer=n/6;
                var c=boxes[theme].GetComponent<PhysicalContainer>();var slot=item.homeSlot;
                if(!slot){var st=Child(boxes[theme],"Sticker cell "+item.assetId,Vector3.zero);slot=st.gameObject.AddComponent<PhysicalStorageSlot>();slot.slotId="desk-revision-home-"+item.itemInstanceId;slot.acceptedType=PhysicalItemType.Sticker;}
                slot.transform.SetParent(boxes[theme],false);slot.container=c;slot.transform.localPosition=new Vector3((cell%2-.5f)*.062f,.009f+layer*.0012f,(cell/2-1)*.046f);
                slot.transform.localRotation=Quaternion.identity;item.ConfigureHome(slot);item.transform.SetPositionAndRotation(slot.WorldPose.position,slot.WorldPose.rotation);
            }
            oldRack.SetParent(archive,true);oldRack.gameObject.SetActive(false);
            // A container-sized legacy collider was obscuring the individual notebook fronts. Only real frame members should collide.
            var tn=fixtures.Find("TravelersBookcase");foreach(var c in tn.GetComponents<Collider>())c.enabled=false;
            var rootCollider=tn.GetComponent<BoxCollider>();if(rootCollider)Object.DestroyImmediate(rootCollider);
            foreach(var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(i=>i.itemType==PhysicalItemType.TravelersNotebook))
            {
                if(item.homeSlot){var p=item.homeSlot.transform.localPosition;p.z=-.008f;item.homeSlot.transform.localPosition=p;item.ConfigureHome(item.homeSlot);}
                item.interactionCollider.enabled=true;
            }
            PrefabUtility.SaveAsPrefabAsset(bag.gameObject,Root+"Prefabs/ClothNoteOrganizer.prefab");
        }
        static PhysicalInteractable NewSticker(DecorationDefinition definition,Transform parent)
        {
            var go=Child(parent,definition.assetId,Vector3.zero).gameObject;var item=go.AddComponent<PhysicalInteractable>();
            item.itemInstanceId="physical-"+definition.assetId+"-001";item.assetId=definition.assetId;item.displayName=definition.displayName;item.itemType=PhysicalItemType.Sticker;
            var size=new Vector3(.038f,.0006f,.038f);var dimensions=ScriptableObject.CreateInstance<PhysicalDimensionProfile>();dimensions.size=size;dimensions.colliderSize=size;
            AssetDatabase.CreateAsset(dimensions,Root+"Models/"+definition.assetId+"_dimensions.asset");item.dimensions=dimensions;item.interactionCollider=go.AddComponent<BoxCollider>();item.interactionCollider.size=size;
            item.visualRoot=Child(go.transform,"Artwork",Vector3.zero);item.grabAnchor=Child(go.transform,"Grab anchor",Vector3.zero);
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name=definition.assetId;mat.SetTexture("_BaseMap",definition.texture);mat.SetFloat("_AlphaClip",1);mat.EnableKeyword("_ALPHATEST_ON");mat.SetFloat("_Cutoff",.1f);mat.SetFloat("_Cull",0);mat.SetFloat("_Smoothness",.05f);
            AssetDatabase.CreateAsset(mat,Root+"Materials/"+definition.assetId+".mat");
            Piece(item.visualRoot,"Individual paper sticker",PrimitiveType.Quad,new Vector3(0,.0004f,0),new Vector3(size.x,size.z,1),mat,new Vector3(90,0,0));
            return item;
        }
        static void Link(Transform p,string name,Vector3 a,Vector3 b,float radius,Material m)
        {var t=Piece(p,name,PrimitiveType.Cylinder,(a+b)*.5f,new Vector3(radius*2,(b-a).magnitude*.5f,radius*2),m);t.rotation=Quaternion.FromToRotation(Vector3.up,p.TransformDirection((b-a).normalized));}
        static void Passers(Transform revision)
        {
            string[] names={"Cloud teacup","Ribbon kite","Floating suitcase","Star mobile","Paper windmill","Moon swing"};
            var sky=Child(revision,"Distant floating curiosities",Vector3.zero);
            for(int i=0;i<names.Length;i++)
            {
                var t=Child(sky,names[i],Vector3.zero);var body=Child(t,"Decorative visual",Vector3.zero);
                var accent=new[]{pink,mint,blue,lilac,pink,cream}[i];
                if(i==0){Piece(body,"Cup",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.22f,.10f,.22f),cream);Piece(body,"Tea",PrimitiveType.Cylinder,new Vector3(0,.102f,0),new Vector3(.18f,.002f,.18f),pink);Piece(body,"Saucer",PrimitiveType.Cylinder,new Vector3(0,-.107f,0),new Vector3(.34f,.015f,.34f),mint);Ring(body,new Vector3(.15f,0,0),.085f,.012f,cream);}
                if(i==1){var kite=Box(body,"Folded kite",Vector3.zero,new Vector3(.25f,.25f,.012f),pink);kite.localEulerAngles=new Vector3(0,0,45);for(int j=0;j<5;j++){Link(body,"String",new Vector3(.03f*Mathf.Sin(j),-.17f-j*.09f,0),new Vector3(.03f*Mathf.Sin(j+1),-.26f-j*.09f,0),.002f,cream);Box(body,"Ribbon bow",new Vector3(.03f*Mathf.Sin(j),-.25f-j*.09f,0),new Vector3(.07f,.018f,.012f),mint);}}
                if(i==2){Box(body,"Weekend case",Vector3.zero,new Vector3(.34f,.23f,.12f),blue);for(int s=-1;s<=1;s+=2)Box(body,"Case strap",new Vector3(s*.09f,0,-.062f),new Vector3(.028f,.23f,.007f),cream);Ring(body,new Vector3(0,.13f,0),.055f,.009f,pink);Box(body,"Travel stamp",new Vector3(.055f,.02f,-.067f),new Vector3(.09f,.06f,.004f),pink);}
                if(i==3){Link(body,"Mobile beam",new Vector3(-.25f,.13f,0),new Vector3(.25f,.13f,0),.006f,gold);for(int j=0;j<3;j++){Link(body,"Thread",new Vector3((j-1)*.19f,.13f,0),new Vector3((j-1)*.19f,-.08f-j*.045f,0),.0015f,cream);Star(body,new Vector3((j-1)*.19f,-.12f-j*.045f,0),.075f,j==1?pink:cream);}}
                if(i==4){Link(body,"Stem",new Vector3(0,-.25f,0),Vector3.zero,.007f,cream);for(int j=0;j<4;j++){var wing=Box(body,"Paper vane",new Vector3(Mathf.Cos(j*Mathf.PI/2)*.07f,Mathf.Sin(j*Mathf.PI/2)*.07f,0),new Vector3(.16f,.07f,.006f),j%2==0?mint:pink);wing.localEulerAngles=new Vector3(0,0,25+j*90);}Piece(body,"Pin",PrimitiveType.Sphere,Vector3.back*.014f,Vector3.one*.035f,gold);}
                if(i==5){Ring(body,Vector3.zero,.20f,.021f,cream);Box(body,"Swing seat",new Vector3(0,-.12f,-.02f),new Vector3(.22f,.025f,.12f),lilac);for(int s=-1;s<=1;s+=2)Link(body,"Swing cord",new Vector3(s*.095f,-.10f,-.02f),new Vector3(s*.095f,.17f,0),.0025f,cream);Star(body,new Vector3(.02f,.035f,0),.06f,pink);}
                var passer=t.gameObject.AddComponent<DreamSkyPasser>();passer.timeOfDay=Need<DreamTimeController>();passer.visual=body;
                passer.from=new Vector3(i%2==0?-3.6f:3.6f,1.6f+(i%3)*.23f,3.0f+i*.18f);passer.to=new Vector3(-passer.from.x,passer.from.y+.15f,passer.from.z);
                passer.period=150;passer.duration=32;passer.phaseSeconds=i*25;passer.bob=.07f;passer.nightOnly=i==5;
                foreach(var r in body.GetComponentsInChildren<Renderer>())r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                PrefabUtility.SaveAsPrefabAsset(t.gameObject,Root+"Prefabs/"+names[i]+".prefab");
            }
        }
        static void Ring(Transform p,Vector3 center,float radius,float thickness,Material mat)
        {for(int i=0;i<32;i++){float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;Link(p,"Curved trim",center+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius,center+new Vector3(Mathf.Cos(b),Mathf.Sin(b),0)*radius,thickness,mat);}}
        static void Star(Transform p,Vector3 center,float radius,Material mat)
        {for(int i=0;i<5;i++){float a=i*Mathf.PI*2/5;Link(p,"Star ray",center,center+new Vector3(Mathf.Sin(a),Mathf.Cos(a),0)*radius,.012f,mat);}}
    }
}
