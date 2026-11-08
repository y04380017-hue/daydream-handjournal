using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>Incremental authoring on the saved bright/commission scene. No physical IDs are replaced.</summary>
    public static class FloatingAtelierSetup
    {
        const string Root = "Assets/Daydream/FloatingV2/";
        const string Scene = "Assets/Scenes/VintageJournalDesk.unity";
        [Serializable] public sealed class PaperInfo { public string id, name, texture; }
        [Serializable] public sealed class PaperList { public PaperInfo[] items; }
        static Material wood, paper, mint, gold, pink, blue, ink;
        static Color ColorHex(string s) { ColorUtility.TryParseHtmlString("#" + s, out var color); return color; }

        static Material Material(string name, string hex, bool unlit = false)
        {
            string path = Root + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/" + (unlit ? "Unlit" : "Lit")));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", ColorHex(hex));
            if (!unlit) { material.SetFloat("_Smoothness", .22f); material.SetFloat("_Metallic", 0); }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Transform Child(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false); t.localPosition = position;
            return t;
        }

        static Transform Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        {
            var t = GameObject.CreatePrimitive(type).transform;
            t.name = name; t.SetParent(parent, false); t.localPosition = position; t.localScale = size;
            Object.DestroyImmediate(t.GetComponent<Collider>());
            t.GetComponent<Renderer>().sharedMaterial = material;
            return t;
        }

        static Transform Box(Transform p, string n, Vector3 pos, Vector3 size, Material m) => Shape(p,n,PrimitiveType.Cube,pos,size,m);

        [MenuItem("Vintage Journal/Daydream/Install floating day and night")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(Scene);
            if (GameObject.Find("Floating atelier cycle")) throw new InvalidOperationException("Floating V2 is already installed. Use a backed-up scene to re-author.");
            Directory.CreateDirectory(Root + "Materials"); Directory.CreateDirectory(Root + "Meshes");
            Directory.CreateDirectory(Root + "Prefabs"); AssetDatabase.Refresh();
            wood = Material("Pale maple", "DFCAAD");
            wood.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Daydream/Textures/Maple.png"));
            paper = Material("Sail cotton", "FFF4DB"); mint = Material("Mint lining", "A9DACC");
            gold = Material("Honey ribbon", "E7C782"); pink = Material("Blush lining", "EAB9B0");
            blue = Material("Sky lining", "A6CCE3"); ink = Material("Ink edge", "596583");
            Shelves(); Papers(); Sky();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if (PrefabUtility.IsPartOfPrefabInstance(t))
                    foreach (var component in t.GetComponents<Component>())
                        if (component) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Calibrate();
            Debug.Log("[FloatingV2] Three wooden modules, 15 preserved shafts, 24 papers and day/night cycle installed.");
        }

        static void Shelves()
        {
            var stands = Object.FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None)
                .Select(s => s.transform.parent).Distinct().OrderBy(t=>t.position.x).ToArray();
            for (int index = 0; index < stands.Length; index++)
            {
                var stand = stands[index];
                // Only hide previous presentation parts. Shafts, handles, colliders and slots retain their poses.
                foreach (Transform t in stand)
                    if (t.name == "Independent acrylic animal silhouette" || t.name == "Slim polished edge" || t.name == "Walnut foot")
                        foreach (var renderer in t.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                var frame = Child(stand,"Maple cabinet visual",Vector3.zero);
                var lining = new[] { mint, pink, blue }[index];
                Box(frame,"Recessed coloured back",new Vector3(0,.137f,.039f),new Vector3(.088f,.274f,.005f),lining);
                for (int side = -1; side <= 1; side += 2)
                    Box(frame,"Maple side stile",new Vector3(side*.045f,.137f,.039f),new Vector3(.008f,.282f,.028f),wood);
                for (int row = 0; row < 5; row++)
                {
                    float y = .0035f + row * .05f;
                    Box(frame,"Open shelf " + (row+1),new Vector3(0,y,.006f),new Vector3(.090f,.005f,.053f),wood);
                    Box(frame,"Slim front lip " + (row+1),new Vector3(0,y-.001f,-.022f),new Vector3(.090f,.006f,.003f),lining);
                }
                Box(frame,"Maple cornice",new Vector3(0,.272f,.012f),new Vector3(.099f,.009f,.068f),wood);
                // The old independent 3D animal heads now read as cabinet emblems above the cornice.
                PrefabUtility.SaveAsPrefabAsset(frame.gameObject,Root+"Prefabs/MapleCabinetVisual_"+(index+1)+".prefab");
            }
        }

        static void Papers()
        {
            var infos = JsonUtility.FromJson<PaperList>(File.ReadAllText(Root+"papers.json"));
            var catalog = JournalPaperCatalog.Load();
            foreach (var info in infos.items)
            {
                var definition = catalog.Find(info.id);
                if (definition == null) throw new InvalidOperationException("Missing original page ID " + info.id);
                definition.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/"+info.texture+".png");
                definition.displayName = info.name;
            }
            EditorUtility.SetDirty(catalog);
            foreach (var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(p=>p.itemType==PhysicalItemType.LoosePage))
            {
                var info = infos.items.FirstOrDefault(p=>p.id==item.assetId);
                if (info == null) continue;
                var art = item.visualRoot.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.sharedMaterial &&
                    (r.name=="Replaceable artwork" || r.sharedMaterial.name.EndsWith("_art") || r.sharedMaterial.name.StartsWith("Bright page art")));
                if (!art) throw new InvalidOperationException("Page artwork missing on " + item.itemInstanceId);
                var material = Material("Paper " + info.id, "EDEDED");
                material.SetTexture("_BaseMap",catalog.Find(info.id).texture);material.SetFloat("_Smoothness",.06f);
                art.sharedMaterial = material; item.displayName = info.name;
                var binding=item.GetComponent<LoosePageBinding>(); if(binding)binding.style=info.name;
            }
        }

        static void Sky()
        {
            var root = new GameObject("Floating atelier cycle").transform;
            var controller = root.gameObject.AddComponent<DreamTimeController>();
            controller.sun = GameObject.Find("AfternoonSun").GetComponent<Light>();
            controller.fill = GameObject.Find("RoomAndWindow/North skylight").GetComponent<Light>();
            controller.bounce = GameObject.Find("WindowBounce").GetComponent<Light>();
            controller.breath = Object.FindFirstObjectByType<DaydreamBreath>();
            controller.sky = GameObject.Find("Sky dome").GetComponent<Renderer>();
            var sky = new Material(Shader.Find("Daydream/FloatingSky"));
            sky.SetTexture("_DayTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/SkyDay.png"));
            sky.SetTexture("_NightTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/SkyNight.png"));
            AssetDatabase.CreateAsset(sky,Root+"Materials/Day and night sky.mat");controller.sky.sharedMaterial=sky;
            var lamp=Child(root,"Paper reading pool",new Vector3(.42f,1.04f,-.09f)).gameObject.AddComponent<Light>();
            lamp.type=LightType.Point;lamp.color=ColorHex("FFE2AB");lamp.range=.95f;lamp.intensity=.09f;lamp.shadows=LightShadows.Soft;
            controller.workLamp=lamp;
            var moon=Child(root,"Moon over the cloud sea",new Vector3(-2.2f,1.5f,4));
            var moonMaterial=Material("Moonlight", "FFF0C1", true);
            Mesh moonMesh = new Mesh { name="Crescent silhouette" };
            const int segments=40;var vertices=new Vector3[(segments+1)*2];var triangles=new int[segments*6];
            for(int i=0;i<=segments;i++)
            {
                float angle=Mathf.Lerp(-Mathf.PI*.5f,Mathf.PI*.5f,i/(float)segments);
                float y=Mathf.Sin(angle)*.32f;
                vertices[i*2]=new Vector3(-Mathf.Cos(angle)*.32f,y,0);
                vertices[i*2+1]=new Vector3(-Mathf.Cos(angle)*.12f,y,0);
                if(i<segments){int a=i*2,k=i*6;triangles[k]=a;triangles[k+1]=a+2;triangles[k+2]=a+1;triangles[k+3]=a+1;triangles[k+4]=a+2;triangles[k+5]=a+3;}
            }
            moonMesh.vertices=vertices;moonMesh.triangles=triangles;moonMesh.RecalculateNormals();moonMesh.RecalculateBounds();
            AssetDatabase.CreateAsset(moonMesh,Root+"Meshes/Crescent.asset");
            moon.gameObject.AddComponent<MeshFilter>().sharedMesh=moonMesh;moon.gameObject.AddComponent<MeshRenderer>().sharedMaterial=moonMaterial;
            moon.localEulerAngles=new Vector3(0,0,-18);controller.moon=moon.gameObject;moon.gameObject.SetActive(false);
            Balloon(root,controller); PaperBoat(root,controller); Meteor(root,controller);
        }

        static (DreamSkyPasser pass,Transform visual) Passage(Transform root,string name,DreamTimeController time)
        {
            var t=Child(root,name,Vector3.zero);
            var pass=t.gameObject.AddComponent<DreamSkyPasser>();pass.timeOfDay=time;
            pass.visual=Child(t,"Drifting visual",Vector3.zero);
            return (pass,pass.visual);
        }

        static void Balloon(Transform root,DreamTimeController time)
        {
            var (pass,v)=Passage(root,"Passing striped balloon",time);
            pass.from=new Vector3(-5,1.25f,4.8f);pass.to=new Vector3(5,1.8f,4.8f);pass.duration=45;pass.period=104;pass.phaseSeconds=12;
            Shape(v,"Silk envelope",PrimitiveType.Sphere,Vector3.zero,new Vector3(.51f,.66f,.51f),pink);
            Shape(v,"Pale central gore",PrimitiveType.Sphere,new Vector3(0,0,-.012f),new Vector3(.24f,.66f,.52f),paper);
            Shape(v,"Envelope neck",PrimitiveType.Cylinder,new Vector3(0,-.31f,0),new Vector3(.14f,.04f,.14f),gold);
            Box(v,"Wicker basket",new Vector3(0,-.52f,0),new Vector3(.16f,.12f,.13f),wood);
            for(int side=-1;side<=1;side+=2)Box(v,"Suspension cord",new Vector3(side*.062f,-.41f,0),new Vector3(.008f,.18f,.008f),ink);
            PrefabUtility.SaveAsPrefabAsset(v.gameObject,Root+"Prefabs/PassingBalloon.prefab");
        }

        static void PaperBoat(Transform root,DreamTimeController time)
        {
            var (pass,v)=Passage(root,"Passing folded sailboat",time);
            pass.from=new Vector3(5,.65f,3.7f);pass.to=new Vector3(-5,1.25f,3.7f);pass.duration=39;pass.period=91;pass.phaseSeconds=51;pass.bob=.10f;
            var hull=Shape(v,"Folded hull",PrimitiveType.Cube,Vector3.zero,new Vector3(.43f,.085f,.14f),mint);
            hull.localEulerAngles=new Vector3(0,0,-5);
            Box(v,"Paper mast",new Vector3(0,.16f,0),new Vector3(.008f,.38f,.008f),gold);
            var mesh=new Mesh{name="Folded sail"};
            mesh.vertices=new[]{new Vector3(0,.36f,0),new Vector3(-.23f,.03f,0),new Vector3(0,.03f,-.07f),new Vector3(.19f,.03f,0)};
            mesh.triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0};mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Root+"Meshes/Paper sail.asset");
            var sail=Child(v,"Folded cotton sail",Vector3.zero);sail.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;sail.gameObject.AddComponent<MeshRenderer>().sharedMaterial=paper;
            PrefabUtility.SaveAsPrefabAsset(v.gameObject,Root+"Prefabs/PassingPaperBoat.prefab");
        }

        static void Meteor(Transform root,DreamTimeController time)
        {
            var (pass,v)=Passage(root,"Occasional shooting star",time);
            pass.from=new Vector3(6,3.3f,7);pass.to=new Vector3(-6,1.8f,7);pass.duration=8;pass.period=57;pass.phaseSeconds=18;pass.nightOnly=true;pass.bob=0;
            var glow=Material("Star gold", "FFE3A5",true);
            Shape(v,"Star tip",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.075f,glow);
            var trail=v.gameObject.AddComponent<LineRenderer>();trail.useWorldSpace=false;trail.positionCount=2;
            trail.SetPositions(new[]{Vector3.zero,new Vector3(.85f,.11f,0)});trail.startWidth=.035f;trail.endWidth=0;trail.sharedMaterial=glow;
            trail.shadowCastingMode=ShadowCastingMode.Off;
        }

        public static void Calibrate()
        {
            var scene=EditorSceneManager.OpenScene(Scene);
            var cycle=Object.FindFirstObjectByType<DreamTimeController>();
            Object.FindFirstObjectByType<JournalPageView>().paperAlbedo=new Color(.87f,.87f,.87f,1);
            cycle.workLamp.transform.position=new Vector3(.30f,1.20f,-.18f);
            cycle.workLamp.range=1.4f;cycle.workLamp.shadows=LightShadows.None;
            cycle.moon.transform.position=new Vector3(-1.6f,.48f,4);
            foreach(var mover in Object.FindObjectsByType<DreamSkyPasser>(FindObjectsSortMode.None))
            {
                if(mover.name.Contains("balloon")){mover.from=new Vector3(-4,.20f,3.2f);mover.to=new Vector3(4,.65f,3.2f);}
                else if(mover.nightOnly){mover.from=new Vector3(6,1.0f,7);mover.to=new Vector3(-6,-.35f,7);}
                else{mover.from=new Vector3(4,.25f,4);mover.to=new Vector3(-4,.05f,4);}
            }
            var sail=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"Meshes/Paper sail.asset");
            sail.triangles=new[]{0,2,1,0,3,2};sail.RecalculateNormals();EditorUtility.SetDirty(sail);
            var sailMaterial=Material("Two sided sail", "FFF4DB");sailMaterial.SetInt("_Cull",(int)CullMode.Off);
            GameObject.Find("Folded cotton sail").GetComponent<Renderer>().sharedMaterial=sailMaterial;
            var sailing=GameObject.Find("Passing folded sailboat").GetComponent<DreamSkyPasser>();
            PrefabUtility.SaveAsPrefabAsset(sailing.visual.gameObject,Root+"Prefabs/PassingPaperBoat.prefab");
            cycle.SetNight(false,false,true);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
