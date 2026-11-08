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
    /// <summary>Replace only the left fixtures. Slot/device IDs remain compatible with existing saves.</summary>
    public static class WoodTapeRackSetup
    {
        const string Root = "Assets/Daydream/WoodTapeRack/";
        const string Scene = "Assets/Scenes/VintageJournalDesk.unity";
        static Material maple, endGrain, lining, brass, ink;
        static Transform cabinet, archive;
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        [MenuItem("Vintage Journal/Daydream/Replace left racks with wooden cabinet")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(Scene);
            if (GameObject.Find("Wooden washi atelier")) throw new InvalidOperationException("Already installed. Restore the recorded scene backup before re-authoring.");
            Directory.CreateDirectory(Root + "Materials"); Directory.CreateDirectory(Root + "Models"); Directory.CreateDirectory(Root + "Prefabs");
            AssetDatabase.Refresh();
            maple = Mat("Natural maple", "E6CDA8", .22f, "Assets/Daydream/Textures/Maple.png");
            endGrain = Mat("Maple end grain", "D2B48C", .18f, "Assets/Daydream/Textures/Maple.png");
            lining = Mat("Cream cotton lining", "F5ECDC", .08f);
            brass = Mat("Small satin fittings", "C5A96B", .34f); brass.SetFloat("_Metallic", .55f);
            ink = Mat("Engraved warm ink", "68564C", .12f);
            var fixtures = GameObject.Find("WorkspaceStage4B").transform;
            var shafts = Object.FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None).OrderBy(s => s.shaftId).ToArray();
            var drawers = Object.FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None);
            if (shafts.Length != 15 || drawers.Length != 3) throw new InvalidOperationException("Unexpected current workspace devices.");
            var oldCabinet = drawers[0].transform.parent;
            var groups = shafts.Select(s => s.transform.parent).Distinct().OrderBy(s => s.position.x).ToArray();
            foreach (var fixture in groups.Concat(new[] { oldCabinet }))
            {
                var prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(fixture.gameObject);
                if (prefab) PrefabUtility.UnpackPrefabInstance(prefab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            archive = Child(fixtures, "Archived previous left rack appearance", Vector3.zero);
            cabinet = Child(fixtures, "Wooden washi atelier", new Vector3(-.490f, .76f, .148f));
            var visual = Child(cabinet, "Replaceable maple cabinet", Vector3.zero);
            // One wide frame, rather than three narrow animal towers. The open front exposes the patterned stock.
            Box(visual, "Left solid maple side", new Vector3(-.183f,.185f,.005f), new Vector3(.012f,.370f,.190f),maple,true);
            Box(visual, "Right solid maple side", new Vector3(.183f,.185f,.005f), new Vector3(.012f,.370f,.190f),maple,true);
            Box(visual, "Recessed slatted back", new Vector3(0,.185f,.098f), new Vector3(.354f,.352f,.006f),endGrain,true);
            Box(visual, "Top crown", new Vector3(0,.369f,.004f), new Vector3(.388f,.011f,.204f),maple,true);
            Box(visual, "Low wooden plinth", new Vector3(0,.006f,.004f), new Vector3(.383f,.012f,.202f),endGrain,true);
            for(int row=0;row<3;row++)
            {
                float y=.184f+row*.060f;
                Box(visual,"Continuous work shelf "+row,new Vector3(0,y,.019f),new Vector3(.354f,.006f,.145f),maple,true);
                Box(visual,"Shelf lip "+row,new Vector3(0,y+.004f,-.056f),new Vector3(.354f,.010f,.005f),endGrain,false);
            }
            Label(visual,"W A S H I   A T E L I E R",new Vector3(0,.366f,-.100f),.00165f);
            // Keep three stable five-slot containers for legacy ownership; lay them out as horizontal rows.
            for(int row=0;row<groups.Length;row++)
            {
                var group=groups[row];
                foreach(Transform child in group.Cast<Transform>().ToArray())
                    if(!child.GetComponent<WorkspaceShaft>())child.SetParent(archive,true);
                group.SetParent(cabinet,false);group.localPosition=Vector3.zero;group.localRotation=Quaternion.identity;
                group.name="Wooden work row "+(row+1);
                group.GetComponent<PhysicalContainer>().displayName="木制胶带架 · 工作横轴 "+(row+1);
                int col=0;
                foreach(var shaft in group.GetComponentsInChildren<WorkspaceShaft>().OrderBy(s=>s.shaftId))
                {
                    shaft.transform.localPosition=new Vector3(-.142f+col*.071f,.212f+row*.060f,-.012f);
                    shaft.transform.localRotation=Quaternion.identity;
                    // Pull towards the player, so neighbouring short spindles never intersect while extended.
                    shaft.slideAxis=Vector3.back;shaft.travel=.13f;shaft.SetOpenAmount(0);
                    foreach(var renderer in shaft.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=brass;
                    var handle=shaft.rod.Find("Shaft pull handle");
                    handle.localPosition=new Vector3(.027f,0,-.085f);handle.localScale=new Vector3(.012f,.013f,.020f);
                    var handleRenderer=handle.GetComponent<Renderer>();handleRenderer.sharedMaterial=endGrain;
                    Neck(shaft.rod, brass);
                    col++;
                }
            }
            foreach(var drawer in drawers)
            {
                bool books=drawer.drawerId=="drawer-release-books";
                bool top=drawer.drawerId=="drawer-tape-top";
                drawer.transform.SetParent(cabinet,false);drawer.transform.localPosition=new Vector3(0,books?.017f:top?.131f:.073f,0);
                drawer.transform.localRotation=Quaternion.identity;
                drawer.container.displayName=books?"木架底部 · 离型本抽屉":top?"木制胶带托盘 · 上层":"木制胶带托盘 · 下层";
                drawer.container.contentsVisibleWhenClosed=true;
                drawer.travel=books?.34f:.17f;drawer.closedLocalPosition=Vector3.zero;drawer.SetOpenAmount(0);
                foreach(Transform child in drawer.slide.Cast<Transform>().ToArray())
                    if(!child.GetComponent<PhysicalStorageSlot>())child.SetParent(archive,true);
                Box(drawer.slide,"Wooden drawer floor",Vector3.zero,new Vector3(.330f,.004f,.172f),maple,true);
                for(int s=-1;s<=1;s+=2)Box(drawer.slide,"Tray side",new Vector3(s*.163f,.013f,0),new Vector3(.004f,.024f,.172f),maple,true);
                Box(drawer.slide,"Low wooden tray front",new Vector3(0,.008f,-.088f),new Vector3(.330f,.014f,.005f),maple,true);
                Box(drawer.slide,"Tray rear",new Vector3(0,.009f,.084f),new Vector3(.330f,.018f,.004f),maple,true);
                Box(drawer.slide,"Independent brass handle",new Vector3(0,.009f,-.101f),new Vector3(.044f,.008f,.016f),brass,true);
                Box(drawer.slide,"Cotton tray liner",new Vector3(0,.0021f,0),new Vector3(.317f,.0004f,.160f),lining,false);
                Label(drawer.slide,books?"RELEASE BOOKS":top?"WASHI / 01":"WASHI / 02",new Vector3(-.095f,.009f,-.091f),.00135f);
                var slots=drawer.slide.GetComponentsInChildren<PhysicalStorageSlot>().OrderBy(s=>s.slotId).ToArray();
                if(!books)
                    for(int i=0;i<slots.Length;i++)
                    {
                        slots[i].transform.localPosition=new Vector3((i-(slots.Length-1)*.5f)*.037f,.027f,-.040f);
                        slots[i].transform.localRotation=Quaternion.Euler(0,0,90);
                    }
            }
            oldCabinet.SetParent(archive,true);
            var bridge=fixtures.Find("Washi display bridge");if(bridge)bridge.SetParent(archive,true);
            archive.gameObject.SetActive(false);
            foreach(var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None))
                if(item.homeSlot && item.homeSlot.transform.IsChildOf(cabinet))
                {item.ConfigureHome(item.homeSlot);item.transform.SetPositionAndRotation(item.homeSlot.WorldPose.position,item.homeSlot.WorldPose.rotation);EditorUtility.SetDirty(item);}
            PrefabUtility.SaveAsPrefabAsset(visual.gameObject,Root+"Prefabs/WoodenTapeRackFrame.prefab");
            foreach(var component in cabinet.GetComponentsInChildren<Component>(true))if(component)EditorUtility.SetDirty(component);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(PrefabUtility.IsPartOfPrefabInstance(t))foreach(var c in t.GetComponents<Component>())if(c)PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("docs/wood-rack");
            File.WriteAllText("docs/wood-rack/authoring.txt","One 388 x 380 x 204 mm wooden cabinet; 15 existing shaft IDs and all home slots retained. Previous appearance inactive in archive. Original source assets untouched.");
            Debug.Log("[WoodRack] Installed one open wooden cabinet, 15 spindles, 2 upright tape trays and a release-book drawer.");
        }
        public static void CalibrateAccess()
        {
            var scene=EditorSceneManager.OpenScene(Scene);
            GameObject.Find("Wooden washi atelier").transform.position=new Vector3(-.490f,.76f,.148f);
            var brass=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Small satin fittings.mat");
            foreach(var drawer in Object.FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None)) if(drawer.drawerId=="drawer-release-books") {drawer.travel=.34f;drawer.SetOpenAmount(0);}
            foreach(var shaft in Object.FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None))
            {
                shaft.travel=.13f;shaft.SetOpenAmount(0);
                var h=shaft.rod.Find("Shaft pull handle");h.localPosition=new Vector3(.027f,0,-.085f);h.localScale=new Vector3(.012f,.013f,.020f);
                Neck(shaft.rod,brass);
            }
            var rack=GameObject.Find("Wooden washi atelier").transform;
            foreach(var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None))
                if(item.homeSlot && item.homeSlot.transform.IsChildOf(rack))
                {item.ConfigureHome(item.homeSlot);item.transform.SetPositionAndRotation(item.homeSlot.WorldPose.position,item.homeSlot.WorldPose.rotation);EditorUtility.SetDirty(item);}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        static void Neck(Transform rod,Material mat)
        {
            if(rod.Find("Pull loop stem"))return;
            var t=GameObject.CreatePrimitive(PrimitiveType.Cube).transform;t.name="Pull loop stem";t.SetParent(rod,false);
            t.localPosition=new Vector3(.027f,0,-.039f);t.localScale=new Vector3(.003f,.003f,.079f);
            Object.DestroyImmediate(t.GetComponent<Collider>());t.GetComponent<Renderer>().sharedMaterial=mat;
        }
        static Material Mat(string name,string hex,float smooth,string texture=null)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};ColorUtility.TryParseHtmlString("#"+hex,out var c);
            m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",smooth);if(texture!=null)m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
            AssetDatabase.CreateAsset(m,Root+"Materials/"+name+".mat");return m;
        }
        static Transform Child(Transform p,string name,Vector3 pos){var t=new GameObject(name).transform;t.SetParent(p,false);t.localPosition=pos;return t;}
        static void Box(Transform p,string name,Vector3 pos,Vector3 size,Material mat,bool collide)
        {
            var t=Child(p,name,pos);
            string key=size.ToString("F4");
            if(!meshes.TryGetValue(key,out var mesh))
            {
                mesh=RoundedBox(size,Mathf.Min(.0012f,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.22f));
                AssetDatabase.CreateAsset(mesh,Root+"Models/Panel_"+meshes.Count+".asset");meshes[key]=mesh;
            }
            t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;
            if(collide)t.gameObject.AddComponent<BoxCollider>().size=size;
        }
        static Mesh RoundedBox(Vector3 size,float bevel)
        {
            var v=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
            Vector3 inner=size*.5f-Vector3.one*bevel;
            foreach(var n in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back})
            {
                var u=Mathf.Abs(n.y)>.5f?Vector3.right:Vector3.up;var w=Vector3.Cross(n,u);int start=v.Count;
                // Closely spaced edge strips keep large wooden faces planar and give highlights a rounded edge.
                float[] ts={-1,-.98f,.98f,1};
                for(int y=0;y<4;y++)for(int x=0;x<4;x++)
                {
                    var raw=Vector3.Scale(n+u*ts[x]+w*ts[y],size*.5f);
                    var core=new Vector3(Mathf.Clamp(raw.x,-inner.x,inner.x),Mathf.Clamp(raw.y,-inner.y,inner.y),Mathf.Clamp(raw.z,-inner.z,inner.z));
                    var normal=(raw-core).normalized;v.Add(core+normal*bevel);normals.Add(normal);uv.Add(new Vector2((ts[x]+1)*.5f,(ts[y]+1)*.5f));
                }
                for(int y=0;y<3;y++)for(int x=0;x<3;x++){int a=start+y*4+x;tri.AddRange(new[]{a,a+1,a+4,a+1,a+5,a+4});}
            }
            var mesh=new Mesh{name="Rounded maple panel"};mesh.SetVertices(v);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateBounds();return mesh;
        }
        static void Label(Transform p,string text,Vector3 pos,float size)
        {
            var t=Child(p,"Wood rack label",pos);var label=t.gameObject.AddComponent<TextMesh>();label.text=text;
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.characterSize=size;label.fontSize=64;
            label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=new Color(.30f,.25f,.20f);
            label.GetComponent<Renderer>().sharedMaterial=label.font.material;
        }
    }
}
