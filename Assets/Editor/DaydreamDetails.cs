using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    public static partial class DaydreamRoom
    {
        static Material Finish(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/"+name+".mat");
        static void InkLabel(Transform parent, string text, Vector3 position, float height, Color color)
        {
            var t=Child(parent,"Letterpress",position);var label=t.gameObject.AddComponent<TextMesh>();
            label.text=text;label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize=72;label.characterSize=height;label.color=color;
            label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;
            t.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
        }
        static void Details()
        {
            var ink=C("425650");var canvas=Finish("Milk paint");var gold=Finish("Satin brass");
            var study=Object.FindFirstObjectByType<JournalPageView>();study.continuousPaperStock=Tex("CottonIvory");
            // Contrast at the narrow front edge makes the handle/stored stock distinguishable through four transparent layers.
            var fixture=GameObject.Find("WorkspaceStage4B").transform;
            foreach(var drawer in fixture.GetComponentsInChildren<WorkspaceDrawer>()){
                var label=drawer.slide.GetComponentInChildren<TextMesh>();label.gameObject.SetActive(false);
                string word=drawer.drawerId.Contains("top")?"01  /  WASHI":drawer.drawerId.Contains("middle")?"02  /  WASHI":"03  /  RELEASE";
                Box(drawer.slide,"Lettered index tab",new Vector3(-.103f,.022f,-.089f),new Vector3(.067f,.015f,.002f),canvas);
                InkLabel(drawer.slide,word,new Vector3(-.103f,.022f,-.0902f),.00165f,ink);
            }
            foreach(var holder in fixture.GetComponentsInChildren<WorkspaceShaft>().Select(s=>s.transform.parent).Distinct()){
                var t=holder.Find("Independent acrylic animal silhouette");var vertices=t.GetComponent<MeshFilter>().sharedMesh.vertices;
                var rim=Child(t,"Polished contour",Vector3.zero).gameObject.AddComponent<LineRenderer>();
                rim.sharedMaterial=Finish(holder.name.StartsWith("Black")?"Wisteria":holder.name.StartsWith("Puppy")?"Apricot":"Pistachio");
                rim.loop=true;rim.useWorldSpace=false;rim.widthMultiplier=.0016f;rim.numCornerVertices=3;
                rim.positionCount=vertices.Length/2;rim.SetPositions(vertices.Take(rim.positionCount).ToArray());rim.shadowCastingMode=ShadowCastingMode.Off;
            }
            foreach(var b in Object.FindObjectsByType<TravelersNotebookBinding>(FindObjectsSortMode.None)){
                var item=b.GetComponentInParent<PhysicalInteractable>();if(!item)continue;
                var size=item.dimensions.size;var p=new Vector3(-.005f,size.y*.15f,-size.z*.5f-.0008f);
                var seal=Child(item.visualRoot,"Bookplate",p);
                Box(seal,"Foil keyline",Vector3.zero,new Vector3(size.x*.61f,size.x*.32f,.0003f),gold);
                Box(seal,"Cotton label",Vector3.back*.0002f,new Vector3(size.x*.58f,size.x*.29f,.0003f),canvas);
                InkLabel(seal,"FIELD NOTES",Vector3.back*.00042f,size.x*.019f,ink);
                InkLabel(seal,"N o .   "+item.assetId.Substring(3),new Vector3(0,-size.x*.085f,-.00042f),size.x*.010f,ink);
            }
            var rack=fixture.Find("TieredPaperArchive");var title=rack.Find("Label");title.gameObject.SetActive(false);
            var tab=Child(rack,"Index card",new Vector3(-.079f,.048f,-.128f));tab.localRotation=Quaternion.Euler(-13,0,0);
            Box(tab,"Mint index mount",Vector3.zero,new Vector3(.132f,.043f,.003f),Finish("Pistachio"));
            Box(tab,"Stock label",Vector3.back*.0018f,new Vector3(.126f,.037f,.001f),canvas);
            InkLabel(tab,"P A P E R   L I B R A R Y",new Vector3(0,.004f,-.0025f),.00225f,ink);
            InkLabel(tab,"REFILL  SAMPLES    /    A5",new Vector3(0,-.009f,-.0025f),.0013f,ink);
            // Matte headstock tint and low-contrast blue wall bounce; avoid additive highlights on layered acrylic.
            var clear=Finish("Sea glass");clear.SetInt("_Cull",(int)CullMode.Back);EditorUtility.SetDirty(clear);
            FitLettering();
            RaiseStands();
        }
        static void RaiseStands()
        {
            var fixtures=GameObject.Find("WorkspaceStage4B").transform;
            if(fixtures.Find("Washi display bridge"))return;
            var bridge=Child(fixtures,"Washi display bridge",new Vector3(-.482f,.76f,.268f));
            var top=Box(bridge,"Maple display shelf",new Vector3(0,.072f,0),new Vector3(.359f,.009f,.081f),Finish("Honey maple"));
            top.gameObject.AddComponent<BoxCollider>();
            for(int edge=-1;edge<=1;edge+=2){
                var end=Box(bridge,"Mint trestle",new Vector3(edge*.163f,.0338f,0),new Vector3(.009f,.0676f,.069f),Finish("Pistachio"));
                end.gameObject.AddComponent<BoxCollider>();
            }
            foreach(var stand in fixtures.GetComponentsInChildren<WorkspaceShaft>().Select(s=>s.transform.parent).Distinct())
                stand.position+=Vector3.up*.0765f;
        }
        public static void CalibrateShaftSightlines()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            RaiseStands();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        static void FitLettering()
        {
            foreach(var label in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
                if(label.name=="Letterpress")label.transform.localScale=Vector3.one*.68f;
            var journal=Object.FindFirstObjectByType<JournalController>();
            journal.GetComponent<DeskItem>().displayName="米色六环活页本";
        }
        public static void FitInstalledLettering()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            if(!GameObject.Find("Index card"))throw new System.InvalidOperationException("Expected the completed Daydream layout.");
            FitLettering();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
    }
}
