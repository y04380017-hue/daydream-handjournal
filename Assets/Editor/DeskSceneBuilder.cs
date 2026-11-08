using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VintageJournal.Editor
{
    /// <summary>Reproducible source for the first-stage furniture, meshes, lighting and saved scene.</summary>
    public static class DeskSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly List<string> prefabs = new List<string>();
        static Material wood, darkWood, brass, cream, paper, sage, leather, linen, glass, green, ink, rose;
        const float TableY = .76f;

        [MenuItem("Vintage Journal/Build first-stage desk")]
        public static void Build()
        {
            foreach (string p in new[] { "Scenes", "Prefabs", "Models", "Materials", "Data", "UI" })
                Directory.CreateDirectory("Assets/" + p);
            Directory.CreateDirectory("docs/logs");
            materials.Clear(); meshes.Clear(); prefabs.Clear();
            AssetDatabase.Refresh();
            SetupPipeline();
            PrepareTextures();
            wood = Mat("Walnut", "#f0e5d5", "Walnut", 0, .27f);
            darkWood = Mat("DarkWalnut", "#aba08f", "Walnut", 0, .24f);
            brass = Mat("AgedBrass", "#f4ead4", "Brass", .78f, .39f);
            cream = Mat("Porcelain", "#eee3ca", null, 0, .53f);
            paper = Mat("Paper", "#fff9ed", "Paper", 0, .12f);
            sage = Mat("SageEnamel", "#5f7c62", null, .17f, .45f);
            leather = Mat("OxbloodLeather", "#f2dbd3", "Leather", 0, .24f);
            linen = Mat("Linen", "#f0e5d5", "Linen", 0, .08f);
            green = Mat("LeafGreen", "#5d744c", null, 0, .19f);
            ink = Mat("Ink", "#443d34", null, 0, .2f);
            rose = Mat("DustyRose", "#aa7470", null, 0, .25f);
            glass = Mat("AmberGlass", "#dce9c8", null, .08f, .85f, .23f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#e8dac1") * .78f;
            RenderSettings.ambientEquatorColor = Hex("#b7b298") * .65f;
            RenderSettings.ambientGroundColor = Hex("#777460") * .55f;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .65f;
            RenderSettings.fog = false;
            var sky = MatSky();
            RenderSettings.skybox = sky;
            CreateLighting();
            Place(Desk(), "Desk", Vector3.zero);
            Place(Room(), "RoomAndWindow", Vector3.zero);
            Place(MatAndLace(), "LinenMat", new Vector3(0, TableY + .001f, -.23f));
            Place(OpenJournal(), "OpenJournal", new Vector3(0, TableY + .012f, -.26f));
            Place(TapeRack(), "TapeRack", new Vector3(-1.01f, TableY, .53f));
            Place(StickerBox(), "StickerBox", new Vector3(-1.04f, TableY, -.25f), new Vector3(0, 8, 0));
            Place(PageHolder(), "PageHolder", new Vector3(.55f, TableY, .65f));
            Place(Drawers(), "FourDrawers", new Vector3(-.17f, TableY, .72f));
            Place(NoteBox(), "NoteBox", new Vector3(.47f, TableY, .3f));
            Place(PenPot(), "BrassPenPot", new Vector3(.79f, TableY, .35f));
            Place(Lamp(), "GreenBankersLamp", new Vector3(1.13f, TableY, .57f));
            Place(Tea(), "TeacupAndSaucer", new Vector3(.94f, TableY, -.37f));
            Place(Vase(), "DaisyVase", new Vector3(1.35f, TableY, .03f));
            Place(Tray(), "MetalTray", new Vector3(-.6f, TableY, -.48f), new Vector3(0, -12, 0));
            Place(ClipBox(), "GlassClipBox", new Vector3(-.66f, TableY, .1f));
            Place(ClosedJournal(), "ClosedJournal", new Vector3(-1.30f, TableY, -.60f));
            for (int i = 1; i <= 12; i++)
            {
                var roll = TapeRoll(i);
                SavePrefab(roll, "TapeRoll_" + i.ToString("00"));
                if (i < 3)
                {
                    roll.name = "LooseTape_" + i;
                    roll.transform.position = new Vector3(-.62f + i * .13f, TableY, -.02f);
                }
                else UnityEngine.Object.DestroyImmediate(roll);
            }
            var camObject = new GameObject("MainCamera");
            camObject.tag = "MainCamera";
            var cam = camObject.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 2.25f, -2.25f);
            cam.transform.LookAt(new Vector3(0, .83f, .22f));
            cam.fieldOfView = 45;
            cam.nearClipPlane = .02f; cam.farClipPlane = 40;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true; cam.allowMSAA = true;
            camObject.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var rig = camObject.AddComponent<DeskCamera>();
            var experience = new GameObject("DeskExperience");
            var ambience=experience.AddComponent<AudioSource>();
            ambience.clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/AfternoonRoom.wav");
            ambience.loop=true; ambience.playOnAwake=true; ambience.volume=.3f; ambience.spatialBlend=0;
            var ui = experience.AddComponent<DeskUI>(); ui.cameraRig = rig;
            var interaction = experience.AddComponent<DeskInteraction>();
            interaction.deskCamera = cam; interaction.cameraRig = rig; interaction.ui = ui;
            PlayerSettings.companyName = "Afternoon Paper Studio";
            PlayerSettings.productName = "午后手账 · Vintage Journal Desk";
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.WriteAllText("docs/logs/scene-build.txt", "Scene: " + ScenePath + "\nURP: 17.3.0\nPrefab assets: " + prefabs.Count + "\n" + string.Join("\n", prefabs) + "\nNOTE: creation is not Play Mode acceptance.\n");
            Debug.Log("[DeskBuild] Saved " + ScenePath + " with " + prefabs.Count + " reusable prefab assets.");
        }

        static void SetupPipeline()
        {
            const string rpPath = "Assets/Data/VintageURP.asset";
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            if (!rp)
            {
                var data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, "Assets/Data/VintageRenderer.asset");
                rp = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(rp, rpPath);
            }
            rp.renderScale = 1; rp.msaaSampleCount = 4;
            rp.supportsHDR = true;
            var settings=new SerializedObject(rp);
            settings.FindProperty("m_MainLightShadowsSupported").boolValue=true;
            settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;
            settings.FindProperty("m_SoftShadowsSupported").boolValue=true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            rp.mainLightShadowmapResolution = 2048; rp.shadowDistance = 10;
            rp.shadowCascadeCount = 2;
            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.renderPipeline = rp;
            EditorUtility.SetDirty(rp);
        }

        static void PrepareTextures()
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures", "Assets/Sprites", "Assets/Resources" });
            foreach (var guid in paths)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                if (!importer) continue;
                bool sprite = p.Contains("Sprites/") || p.Contains("Resources/Catalog/");
                importer.textureType = p.Contains("/Normals/") ? TextureImporterType.NormalMap : (sprite ? TextureImporterType.Sprite : TextureImporterType.Default);
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = !p.Contains("Resources/Catalog/");
                importer.wrapMode = p.Contains("Textures/") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        static void CreateLighting()
        {
            var sun = new GameObject("AfternoonSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(42, 148, 0);
            sun.color = Hex("#ffe0ad"); sun.intensity = 1.65f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .8f;
            sun.shadowBias = .018f; sun.shadowNormalBias = .025f;
            RenderSettings.sun = sun;
            var fill = new GameObject("WindowBounce").AddComponent<Light>();
            fill.type = LightType.Point; fill.color = Hex("#e5e8d3"); fill.intensity = .75f;
            fill.range = 4; fill.transform.position = new Vector3(-1.0f, 2.3f, -.2f);
            var vol = new GameObject("WarmColorGrade").AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Data/AfternoonGrade.asset");
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, "Assets/Data/AfternoonGrade.asset");
                var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
                var adjust = profile.Add<ColorAdjustments>(true);
                adjust.postExposure.Override(.18f); adjust.contrast.Override(4); adjust.saturation.Override(-7);
                var balance = profile.Add<WhiteBalance>(true); balance.temperature.Override(5);
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            }
            vol.sharedProfile = profile;
        }

        static Material MatSky()
        {
            const string path = "Assets/Materials/AfternoonSky.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Skybox/Procedural"));
                mat.SetColor("_SkyTint", Hex("#d8e2dc")); mat.SetColor("_GroundColor", Hex("#aaa480"));
                mat.SetFloat("_AtmosphereThickness", .7f); mat.SetFloat("_Exposure", .8f);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static Material Mat(string name, string color, string texture = null, float metallic = 0, float smooth = .2f, float alpha = 1)
        {
            if (materials.TryGetValue(name, out var cached)) return cached;
            string path = "Assets/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            var c = Hex(color); c.a = alpha;
            mat.SetColor("_BaseColor", c); mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", smooth);
            if (!string.IsNullOrEmpty(texture))
            {
                string tp = texture.StartsWith("Assets/") ? texture : "Assets/Textures/" + texture + ".png";
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
                string normalPath="Assets/Textures/Normals/"+Path.GetFileNameWithoutExtension(tp)+"_Normal.png";
                var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if(normal)
                {
                    mat.SetTexture("_BumpMap",normal);mat.SetFloat("_BumpScale",.35f);mat.EnableKeyword("_NORMALMAP");
                }
            }
            if (alpha < 1)
            {
                mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetFloat("_ZWrite", 0);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
                mat.SetFloat("_Cull", 0);
            }
            EditorUtility.SetDirty(mat); materials[name] = mat;
            return mat;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static GameObject Root(string name) { return new GameObject(name); }
        static void Item(GameObject root, DeskItemKind kind, string label)
        { var item = root.AddComponent<DeskItem>(); item.kind = kind; item.displayName = label; }
        static void Place(GameObject item, string name, Vector3 position, Vector3 rotation = default)
        { item.name = name; item.transform.position = position; item.transform.eulerAngles = rotation; SavePrefab(item, name); }
        static void SavePrefab(GameObject item, string name)
        {
            // Generated assets are isolated in this new project; source user images are never edited.
            string path = "Assets/Prefabs/" + name + ".prefab";
            var position = item.transform.position; var rotation = item.transform.rotation;
            item.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.SaveAsPrefabAsset(item, path);
            item.transform.SetPositionAndRotation(position, rotation);
            prefabs.Add(path);
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material material, float bevel = .006f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = BevelBox(size, Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .35f));
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        static GameObject Shape(Transform parent, string name, Vector3 pos, Mesh mesh, Material material, bool collider = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static GameObject Lathe(Transform parent, string name, Vector3 pos, Vector2[] profile, Material mat, int segments = 48)
        { return Shape(parent, name, pos, LatheMesh(name, profile, segments), mat); }
        static GameObject Ball(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static void Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
        {
            float h = Vector3.Distance(a, b);
            var go = Lathe(parent, name, (a + b) * .5f,
                new[] { new Vector2(0, -h / 2), new Vector2(radius, -h / 2), new Vector2(radius, h / 2), new Vector2(0, h / 2) }, mat, 12);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        }

        static GameObject Desk()
        {
            var g = Root("Desk");
            Box(g.transform, "Solid walnut top", new Vector3(0, TableY - .038f, .1f), new Vector3(3.12f, .076f, 1.8f), wood, .017f);
            Box(g.transform, "Front apron", new Vector3(0, .59f, -.7f), new Vector3(2.9f, .2f, .045f), darkWood, .009f);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Box(g.transform, "Tapered leg", new Vector3(x * 1.38f, .31f, .1f + z * .72f), new Vector3(.075f, .62f, .075f), darkWood);
                Lathe(g.transform, "Brass foot", new Vector3(x * 1.38f, .015f, .1f + z * .72f), new[] {new Vector2(.04f,0),new Vector2(.038f,.06f)}, brass, 16);
            }
            return g;
        }

        static GameObject Room()
        {
            var g = Root("RoomAndWindow");
            var wall = Mat("WarmPlaster", "#fff9ef", "Wall", 0, .08f);
            var sky = Mat("WindowSky", "#d9e2de", null, 0, .05f);
            sky.shader=Shader.Find("Universal Render Pipeline/Unlit");
            sky.SetColor("_BaseColor",Color.white);
            sky.SetTexture("_BaseMap",WindowAtmosphere());
            EditorUtility.SetDirty(sky);
            // The opening is real geometry, not a single scene-background image.
            Box(g.transform, "Left plaster", new Vector3(-2.76f, 1.55f, 1.19f), new Vector3(2.4f, 3.1f, .1f), wall);
            Box(g.transform, "Right plaster", new Vector3(1.5f, 1.55f, 1.19f), new Vector3(3.2f, 3.1f, .1f), wall);
            Box(g.transform, "Under window", new Vector3(-.85f, .42f, 1.19f), new Vector3(1.35f, .84f, .1f), wall);
            Box(g.transform, "Above window", new Vector3(-.85f, 2.65f, 1.19f), new Vector3(1.35f, .9f, .1f), wall);
            var backdrop=Box(g.transform, "Soft outdoor sky", new Vector3(-.85f, 1.6f, 1.6f), new Vector3(1.45f, 1.6f, .02f), sky, 0);
            backdrop.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            backdrop.GetComponent<Renderer>().receiveShadows=false;
            var landscape = Mat("DistantFoliage", "#9ca989", null, 0, 0);
            landscape.shader=Shader.Find("Universal Render Pipeline/Unlit");
            landscape.SetColor("_BaseColor",Hex("#a6b396"));
            EditorUtility.SetDirty(landscape);
            for (int i = 0; i < 9; i++)
            {
                var foliage=Ball(g.transform, "Soft garden foliage", new Vector3(-1.45f + i * .16f, .91f + .06f * Mathf.Sin(i * 1.7f), 1.52f), new Vector3(.27f,.24f,.035f), landscape);
                foliage.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                foliage.GetComponent<Renderer>().receiveShadows=false;
            }
            for (int i = 0; i < 2; i++)
                Box(g.transform, "Window vertical frame", new Vector3(-1.54f + i * 1.38f, 1.57f, 1.08f), new Vector3(.06f,1.53f,.13f), darkWood);
            for (int i = 0; i < 2; i++)
                Box(g.transform, "Window horizontal frame", new Vector3(-.85f,.82f + i * 1.5f,1.08f),new Vector3(1.45f,.065f,.13f),darkWood);
            Box(g.transform,"Window mullion", new Vector3(-.85f,1.57f,1.07f),new Vector3(.035f,1.45f,.12f),wood);
            Box(g.transform,"Window crossbar",new Vector3(-.85f,1.6f,1.07f),new Vector3(1.35f,.035f,.12f),wood);
            Box(g.transform,"Deep window sill",new Vector3(-.85f,.825f,1.02f),new Vector3(1.61f,.07f,.34f),wood,.013f);
            // Back wainscot and floor are useful when orbiting in the editor.
            Box(g.transform,"Wainscot rail",new Vector3(0,.65f,1.12f),new Vector3(6,.045f,.1f),darkWood);
            Box(g.transform,"Wood floor",new Vector3(0,-.045f,0),new Vector3(8,.08f,8),wood,.001f);
            var plant = Plant(); plant.transform.SetParent(g.transform,false); plant.transform.localPosition=new Vector3(-1.2f,.865f,1.04f);
            SavePrefab(plant,"PottedPlant");
            var books = OldBooks(); books.transform.SetParent(g.transform,false); books.transform.localPosition=new Vector3(.5f,TableY,.93f);
            SavePrefab(books,"OldBooks");
            var frame = Frame(); frame.transform.SetParent(g.transform,false); frame.transform.localPosition=new Vector3(.66f,1.62f,1.105f);
            SavePrefab(frame,"BotanicalFrame");
            return g;
        }

        static GameObject MatAndLace()
        {
            var g=Root("LinenMat");
            Box(g.transform,"Woven linen",Vector3.zero,new Vector3(1.12f,.004f,.78f),linen,.001f);
            var thread=Mat("LaceThread","#e7dac2",null,0,.05f);
            for(int i=0;i<37;i++)
            for(int side=-1;side<=1;side+=2)
                Lathe(g.transform,"Lace scallop",new Vector3(-.54f+i*.03f,.0025f,side*.397f),new[]{new Vector2(.008f,0),new Vector2(.015f,0),new Vector2(.015f,.001f),new Vector2(.008f,.001f)},thread,12);
            for(int i=0;i<25;i++)
            for(int side=-1;side<=1;side+=2)
                Lathe(g.transform,"Side lace",new Vector3(side*.566f,.0025f,-.36f+i*.03f),new[]{new Vector2(.008f,0),new Vector2(.015f,0),new Vector2(.015f,.001f),new Vector2(.008f,.001f)},thread,12);
            return g;
        }

        static GameObject OpenJournal()
        {
            var g=Root("OpenJournal");
            Item(g,DeskItemKind.Journal,"复古皮革手账本");
            Box(g.transform,"Leather covers",new Vector3(0,.014f,0),new Vector3(.85f,.027f,.56f),leather,.012f);
            Box(g.transform,"Left paper block",new Vector3(-.21f,.034f,0),new Vector3(.397f,.032f,.526f),paper,.004f);
            Box(g.transform,"Right paper block",new Vector3(.21f,.034f,0),new Vector3(.397f,.032f,.526f),paper,.004f);
            var left=Mat("JournalPageLeft","#fff8e8","Assets/Sprites/JournalPages/PAGE_01.png",0,.07f);
            var right=Mat("JournalPageRight","#fff8e8","Assets/Sprites/JournalPages/PAGE_02.png",0,.07f);
            TopQuad(g.transform,"Left decorated page",new Vector3(-.209f,.051f,0),.393f,.52f,left);
            TopQuad(g.transform,"Right decorated page",new Vector3(.209f,.051f,0),.393f,.52f,right);
            Box(g.transform,"Leather spine",new Vector3(0,.035f,0),new Vector3(.018f,.033f,.54f),darkWood,.007f);
            for(int side=-1;side<=1;side+=2)
                for(int i=0;i<6;i++)
                    Box(g.transform,"Layered paper edge",new Vector3(side*.213f,.021f+i*.004f,-.265f),new Vector3(.386f,.0007f,.001f),paper,.0001f);
            Box(g.transform,"Silk bookmark",new Vector3(.32f,.053f,-.015f),new Vector3(.012f,.0012f,.49f),sage,.0002f);
            // A real brass binder clip and rivets.
            Box(g.transform,"Brass clip plate",new Vector3(.31f,.057f,.232f),new Vector3(.09f,.012f,.026f),brass,.004f);
            for(int i=-1;i<=1;i+=2)
                Ball(g.transform,"Clip rivet",new Vector3(.31f+i*.032f,.064f,.232f),new Vector3(.007f,.002f,.007f),brass);
            Rod(g.transform,"Clip loop",new Vector3(.277f,.065f,.23f),new Vector3(.277f,.082f,.263f),.0015f,brass);
            Rod(g.transform,"Clip loop",new Vector3(.343f,.065f,.23f),new Vector3(.343f,.082f,.263f),.0015f,brass);
            Rod(g.transform,"Clip handle",new Vector3(.277f,.082f,.263f),new Vector3(.343f,.082f,.263f),.0015f,brass);
            return g;
        }

        static GameObject ClosedJournal()
        {
            var g=Root("ClosedJournal");
            Box(g.transform,"Leather back",new Vector3(0,.009f,0),new Vector3(.27f,.018f,.36f),leather,.009f);
            Box(g.transform,"Paper edge",new Vector3(0,.025f,0),new Vector3(.25f,.022f,.34f),paper,.003f);
            Box(g.transform,"Leather front",new Vector3(0,.045f,0),new Vector3(.27f,.018f,.36f),leather,.009f);
            Box(g.transform,"Brass title frame",new Vector3(0,.055f,.025f),new Vector3(.19f,.001f,.09f),brass,.001f);
            Box(g.transform,"Title leather inset",new Vector3(0,.056f,.025f),new Vector3(.18f,.001f,.08f),leather,.001f);
            Box(g.transform,"Elastic band",new Vector3(.09f,.057f,0),new Vector3(.009f,.002f,.36f),darkWood,.001f);
            return g;
        }

        static GameObject TapeRack()
        {
            var g=Root("TapeRack"); Item(g,DeskItemKind.Tape,"和纸胶带 · 十二款纹样");
            for(int i=-1;i<=1;i+=2) Box(g.transform,"Rack side",new Vector3(i*.31f,.215f,0),new Vector3(.026f,.43f,.31f),wood,.01f);
            Box(g.transform,"Rack back",new Vector3(0,.21f,.145f),new Vector3(.62f,.42f,.018f),darkWood,.004f);
            for(int tier=0;tier<3;tier++)
            {
                float y=.015f+tier*.14f;
                Box(g.transform,"Tier shelf",new Vector3(0,y,0),new Vector3(.62f,.022f,.31f),wood,.005f);
                Box(g.transform,"Shelf lip",new Vector3(0,y+.02f,-.147f),new Vector3(.62f,.028f,.012f),brass,.003f);
                for(int j=0;j<4;j++)
                {
                    int index=tier*4+j+1;
                    var roll=TapeRoll(index); roll.name="ShelfTape_"+index.ToString("00");
                    roll.transform.SetParent(g.transform,false);
                    roll.transform.localRotation=Quaternion.Euler(90,0,0);
                    roll.transform.localPosition=new Vector3(-.223f+j*.148f,y+.068f,-.105f);
                }
            }
            Box(g.transform,"Catalog label",new Vector3(0,.442f,-.02f),new Vector3(.35f,.002f,.07f),paper,.002f);
            return g;
        }

        static GameObject TapeRoll(int index)
        {
            var g=Root("TapeRoll_"+index.ToString("00"));
            string id="tape_"+index.ToString("00");
            var pattern=Mat("Washi_"+id,"#fff8e8","Assets/Textures/Tapes/"+id+"_pattern.png",0,.26f,.94f);
            Lathe(g.transform,"Patterned washi wrap",new Vector3(0,.026f,0),new[]{new Vector2(.025f,-.025f),new Vector2(.057f,-.025f),new Vector2(.057f,.025f),new Vector2(.025f,.025f)},pattern,48);
            Lathe(g.transform,"Paper core",new Vector3(0,.026f,0),new[]{new Vector2(.0225f,-.025f),new Vector2(.0255f,-.025f),new Vector2(.0255f,.025f),new Vector2(.0225f,.025f),new Vector2(.0225f,-.025f)},paper,32);
            // Concentric paper edges suggest many fine windings rather than a plastic cylinder.
            var edge=Mat("WashiEdge_"+id,"#eadcc4",null,0,.1f);
            for(int i=0;i<5;i++)
            {
                float r=.031f+i*.005f;
                Lathe(g.transform,"Wound paper edge",new Vector3(0,.0512f,0),new[]{new Vector2(r,0),new Vector2(r+.0006f,0)},edge,48);
            }
            g.AddComponent<BoxCollider>().size=new Vector3(.114f,.052f,.114f);
            g.GetComponent<BoxCollider>().center=new Vector3(0,.026f,0);
            return g;
        }

        static GameObject StickerBox()
        {
            var g=Root("StickerBox"); Item(g,DeskItemKind.Sticker,"复古贴纸收纳盒");
            Box(g.transform,"Base",new Vector3(0,.012f,0),new Vector3(.44f,.024f,.31f),wood);
            for(int side=-1;side<=1;side+=2)
            {
                Box(g.transform,"Side",new Vector3(side*.21f,.07f,0),new Vector3(.02f,.13f,.31f),wood);
                Box(g.transform,"Front and back",new Vector3(0,.066f,side*.145f),new Vector3(.44f,.12f,.02f),wood);
            }
            for(int i=0;i<6;i++)
            {
                var card=Box(g.transform,"Sticker envelope",new Vector3(0,.08f,-.105f+i*.04f),new Vector3(.36f,.12f,.004f),i%2==0?paper:linen,.001f);
                card.transform.localRotation=Quaternion.Euler(-12,0,i%2==0?-3:3);
            }
            Box(g.transform,"Brass label holder",new Vector3(0,.07f,-.157f),new Vector3(.17f,.047f,.003f),brass,.003f);
            Box(g.transform,"Label insert",new Vector3(0,.071f,-.159f),new Vector3(.15f,.033f,.001f),paper,.001f);
            return g;
        }

        static GameObject PageHolder()
        {
            var g=Root("PageHolder"); Item(g,DeskItemKind.Pages,"内页档案 · 二十四款");
            Box(g.transform,"Holder base",new Vector3(0,.012f,0),new Vector3(.27f,.024f,.27f),wood);
            Box(g.transform,"Holder back",new Vector3(0,.17f,.126f),new Vector3(.27f,.34f,.018f),wood);
            for(int side=-1;side<=1;side+=2) Box(g.transform,"Upright side",new Vector3(side*.126f,.12f,0),new Vector3(.018f,.24f,.27f),wood);
            for(int i=0;i<6;i++)
            {
                var page=Box(g.transform,"Stored page "+(i+1),new Vector3(0,.18f,-.055f+i*.026f),new Vector3(.22f,.31f,.004f),paper,.001f);
                page.transform.localRotation=Quaternion.Euler(-8,0,0);
                var tab=Box(g.transform,"Divider tab",new Vector3(-.08f+i*.029f,.342f,-.055f+i*.026f),new Vector3(.029f,.022f,.005f),i%2==0?sage:rose,.003f);
            }
            Box(g.transform,"Front slat",new Vector3(0,.06f,-.127f),new Vector3(.27f,.1f,.016f),wood);
            return g;
        }

        static GameObject NoteBox()
        {
            var g=Root("NoteBox");
            Box(g.transform,"Base",new Vector3(0,.012f,0),new Vector3(.23f,.024f,.2f),wood);
            for(int side=-1;side<=1;side+=2)
            {
                Box(g.transform,"Side",new Vector3(side*.108f,.04f,0),new Vector3(.014f,.06f,.2f),wood);
                Box(g.transform,"End",new Vector3(0,.04f,side*.093f),new Vector3(.23f,.06f,.014f),wood);
            }
            for(int i=0;i<8;i++) Box(g.transform,"Memo sheet",new Vector3(0,.027f+i*.003f,0),new Vector3(.19f,.002f,.16f),i%3==0?sage:paper,.0005f);
            return g;
        }

        static GameObject Drawers()
        {
            var g=Root("FourDrawers");
            Box(g.transform,"Cabinet body",new Vector3(0,.185f,0),new Vector3(.49f,.37f,.29f),darkWood,.01f);
            Box(g.transform,"Cabinet cornice",new Vector3(0,.385f,0),new Vector3(.53f,.035f,.32f),wood,.008f);
            for(int i=0;i<4;i++)
            {
                float y=.055f+i*.084f;
                Box(g.transform,"Drawer front "+(i+1),new Vector3(0,y,-.151f),new Vector3(.447f,.075f,.025f),wood,.004f);
                Box(g.transform,"Label frame",new Vector3(0,y,-.167f),new Vector3(.104f,.032f,.005f),brass,.003f);
                Box(g.transform,"Paper label",new Vector3(0,y+.001f,-.17f),new Vector3(.087f,.021f,.001f),paper,.001f);
                for(int side=-1;side<=1;side+=2) Ball(g.transform,"Drawer knob",new Vector3(side*.15f,y,-.181f),new Vector3(.019f,.019f,.019f),brass);
            }
            return g;
        }

        static GameObject PenPot()
        {
            var g=Root("BrassPenPot");
            Lathe(g.transform,"Brass cup",Vector3.zero,new[]{new Vector2(0,0),new Vector2(.052f,0),new Vector2(.056f,.012f),new Vector2(.057f,.15f),new Vector2(.053f,.154f),new Vector2(.05f,.148f),new Vector2(.049f,.014f),new Vector2(0,.014f)},brass,40);
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI*2/6; var b=new Vector3(Mathf.Cos(a)*.03f,.016f,Mathf.Sin(a)*.03f);
                var e=new Vector3(b.x*1.7f,.245f+(i%3)*.018f,b.z*1.6f);
                Rod(g.transform,"Pencil",b,e,.004f,i%2==0?sage:wood);
                Rod(g.transform,"Graphite tip",e,e+(e-b).normalized*.011f,.002f,ink);
                Rod(g.transform,"Brass ferrule",Vector3.Lerp(b,e,.76f),Vector3.Lerp(b,e,.82f),.0047f,brass);
            }
            return g;
        }

        static GameObject Lamp()
        {
            var g=Root("GreenBankersLamp");
            Lathe(g.transform,"Weighted brass base",Vector3.zero,new[]{new Vector2(0,0),new Vector2(.115f,0),new Vector2(.127f,.012f),new Vector2(.12f,.025f),new Vector2(.08f,.035f),new Vector2(0,.037f)},brass);
            Lathe(g.transform,"Stem fitting",new Vector3(0,.034f,0),new[]{new Vector2(.029f,0),new Vector2(.025f,.04f),new Vector2(.012f,.055f)},brass,24);
            Rod(g.transform,"Upright stem",new Vector3(0,.07f,0),new Vector3(0,.36f,.01f),.012f,brass);
            Rod(g.transform,"Bent neck",new Vector3(0,.36f,.01f),new Vector3(0,.41f,-.08f),.012f,brass);
            var shade=Shape(g.transform,"Curved green enamel shade",new Vector3(0,.365f,-.1f),ShadeMesh(),sage,true);
            var lining=Mat("LampLining","#f2e4c7",null,0,.2f);
            lining.SetFloat("_Cull",1);
            var inside=Shape(g.transform,"Cream shade lining",new Vector3(0,.364f,-.1f),ShadeMesh(),lining);
            inside.transform.localScale=new Vector3(.98f,.96f,.96f);
            for(int side=-1;side<=1;side+=2)
            {
                Box(g.transform,"Shade end cap",new Vector3(side*.18f,.39f,-.1f),new Vector3(.008f,.06f,.152f),sage,.017f);
                Rod(g.transform,"Brass shade rim",new Vector3(-.18f,.365f,-.1f+side*.075f),new Vector3(.18f,.365f,-.1f+side*.075f),.0035f,brass);
            }
            var bulbMat=Mat("LampBulb","#fff0b2",null,0,.2f);
            bulbMat.EnableKeyword("_EMISSION"); bulbMat.SetColor("_EmissionColor",Hex("#ffd18e")*1.5f);
            Ball(g.transform,"Warm bulb",new Vector3(0,.39f,-.1f),new Vector3(.04f,.025f,.025f),bulbMat);
            var light=new GameObject("Lamp warm pool").AddComponent<Light>(); light.transform.SetParent(g.transform,false);
            light.transform.localPosition=new Vector3(0,.36f,-.1f); light.transform.localRotation=Quaternion.Euler(90,0,0);
            light.type=LightType.Spot; light.spotAngle=104; light.innerSpotAngle=62; light.range=1.2f; light.intensity=1.6f;
            light.color=Hex("#ffd29b"); light.shadows=LightShadows.Soft; light.shadowBias=.01f; light.shadowNormalBias=.01f;
            Rod(g.transform,"Pull chain",new Vector3(.13f,.37f,-.13f),new Vector3(.13f,.25f,-.13f),.001f,brass);
            Ball(g.transform,"Pull switch",new Vector3(.13f,.25f,-.13f),new Vector3(.009f,.016f,.009f),brass);
            return g;
        }

        static GameObject Tea()
        {
            var g=Root("TeacupAndSaucer");
            Lathe(g.transform,"Porcelain saucer",Vector3.zero,new[]{new Vector2(0,.004f),new Vector2(.05f,.004f),new Vector2(.108f,.009f),new Vector2(.121f,.016f),new Vector2(.119f,.021f),new Vector2(.1f,.018f),new Vector2(.045f,.014f),new Vector2(0,.014f)},cream);
            Lathe(g.transform,"Gold saucer rim",new Vector3(0,.02f,0),new[]{new Vector2(.115f,0),new Vector2(.119f,0),new Vector2(.119f,.001f),new Vector2(.115f,.001f)},brass);
            Lathe(g.transform,"Tea cup",new Vector3(0,.014f,0),new[]{new Vector2(0,0),new Vector2(.034f,0),new Vector2(.038f,.012f),new Vector2(.047f,.017f),new Vector2(.064f,.075f),new Vector2(.06f,.078f),new Vector2(.056f,.073f),new Vector2(.041f,.019f),new Vector2(0,.019f)},cream);
            Lathe(g.transform,"Gold cup rim",new Vector3(0,.09f,0),new[]{new Vector2(.059f,0),new Vector2(.064f,0),new Vector2(.064f,.002f),new Vector2(.059f,.002f)},brass);
            var tea=Mat("BlackTea","#70432b",null,0,.86f);
            Lathe(g.transform,"Tea surface",new Vector3(0,.079f,0),new[]{new Vector2(0,0),new Vector2(.056f,0)},tea);
            // A thin circular ceramic handle lies in the vertical XY plane.
            var handle=Lathe(g.transform,"Cup handle",new Vector3(.071f,.057f,0),new[]{new Vector2(.019f,-.003f),new Vector2(.025f,-.003f),new Vector2(.027f,0),new Vector2(.025f,.003f),new Vector2(.019f,.003f),new Vector2(.017f,0),new Vector2(.019f,-.003f)},cream,32);
            handle.transform.localRotation=Quaternion.Euler(90,0,0);
            Rod(g.transform,"Teaspoon",new Vector3(.055f,.025f,-.09f),new Vector3(.105f,.025f,.038f),.002f,brass);
            Ball(g.transform,"Spoon bowl",new Vector3(.105f,.025f,.038f),new Vector3(.017f,.003f,.024f),brass);
            return g;
        }

        static GameObject Vase()
        {
            var g=Root("DaisyVase");
            Lathe(g.transform,"Blown glass vase",Vector3.zero,new[]{new Vector2(0,0),new Vector2(.049f,0),new Vector2(.066f,.023f),new Vector2(.072f,.10f),new Vector2(.058f,.16f),new Vector2(.03f,.21f),new Vector2(.027f,.29f),new Vector2(.032f,.298f),new Vector2(.027f,.301f),new Vector2(.022f,.292f),new Vector2(.025f,.21f),new Vector2(.053f,.16f),new Vector2(.067f,.1f),new Vector2(.061f,.025f),new Vector2(0,.008f)},glass,48);
            var water=Mat("VaseWater","#b3c9a5",null,0,.82f,.24f);
            Lathe(g.transform,"Water",new Vector3(0,.012f,0),new[]{new Vector2(0,0),new Vector2(.06f,.015f),new Vector2(.065f,.10f),new Vector2(0,.10f)},water,32);
            for(int i=0;i<6;i++)
            {
                float a=i*2.4f;
                var top=new Vector3(Mathf.Sin(a)*.095f,.37f+(i%3)*.035f,Mathf.Cos(a)*.067f);
                Rod(g.transform,"Daisy stem",new Vector3(Mathf.Sin(a)*.025f,.024f,Mathf.Cos(a)*.022f),top,.0025f,green);
                Daisy(g.transform,top,i);
            }
            return g;
        }

        static void Daisy(Transform parent,Vector3 p,int index)
        {
            var petal=Mat("DaisyIvory","#fff7dc",null,0,.15f);
            var center=Mat("DaisyGold","#cba754",null,0,.15f);
            for(int j=0;j<11;j++)
            {
                float a=j*Mathf.PI*2/11;
                var go=Ball(parent,"Daisy petal",p+new Vector3(Mathf.Sin(a)*.023f,0,Mathf.Cos(a)*.023f),new Vector3(.011f,.005f,.037f),petal);
                go.transform.localRotation=Quaternion.Euler(0,a*Mathf.Rad2Deg,0);
            }
            Ball(parent,"Daisy pollen",p+Vector3.up*.003f,new Vector3(.025f,.01f,.025f),center);
            var leaf=Ball(parent,"Daisy leaf",Vector3.Lerp(new Vector3(0,.1f,0),p,.65f)+Vector3.right*.018f,new Vector3(.05f,.007f,.02f),green);
            leaf.transform.localRotation=Quaternion.Euler(0,index*52,25);
        }

        static GameObject Plant()
        {
            var g=Root("PottedPlant");
            var terracotta=Mat("Terracotta","#ac7860",null,0,.14f);
            Lathe(g.transform,"Terracotta pot",Vector3.zero,new[]{new Vector2(0,0),new Vector2(.06f,0),new Vector2(.077f,.11f),new Vector2(.083f,.112f),new Vector2(.083f,.13f),new Vector2(.073f,.13f),new Vector2(.069f,.019f),new Vector2(0,.019f)},terracotta,32);
            Lathe(g.transform,"Pot soil",new Vector3(0,.114f,0),new[]{new Vector2(0,0),new Vector2(.07f,0)},darkWood,24);
            for(int i=0;i<13;i++)
            {
                float a=i*2.399f; var end=new Vector3(Mathf.Sin(a)*.12f,.22f+(i%4)*.045f,Mathf.Cos(a)*.12f);
                Rod(g.transform,"Plant stem",new Vector3(0,.11f,0),end,.0025f,green);
                var leaf=Ball(g.transform,"Plant leaf",end,new Vector3(.08f,.012f,.034f),i%3==0?sage:green);
                leaf.transform.localRotation=Quaternion.Euler(22,a*Mathf.Rad2Deg,20);
            }
            return g;
        }

        static GameObject OldBooks()
        {
            var g=Root("OldBooks");
            var bookColors=new[]{sage,leather,Mat("BookOchre","#aa9160",null,0,.15f)};
            for(int i=0;i<3;i++)
            {
                float y=.018f+i*.043f;
                var book=Root("Old book "+i); book.transform.SetParent(g.transform,false);
                book.transform.localPosition=new Vector3((i%2)*.025f,0,0); book.transform.localRotation=Quaternion.Euler(0,i*4-4,0);
                Box(book.transform,"Paper block",new Vector3(0,y+.01f,0),new Vector3(.3f,.03f,.16f),paper,.001f);
                for(int side=-1;side<=1;side+=2) Box(book.transform,"Cloth cover",new Vector3(0,y+.01f+side*.018f,0),new Vector3(.316f,.006f,.18f),bookColors[i],.002f);
                Box(book.transform,"Cloth spine",new Vector3(0,y+.011f,-.087f),new Vector3(.316f,.04f,.007f),bookColors[i],.003f);
                for(int side=-1;side<=1;side+=2) Box(book.transform,"Spine gilding",new Vector3(side*.113f,y+.011f,-.091f),new Vector3(.005f,.029f,.001f),brass,.0001f);
            }
            return g;
        }

        static GameObject Frame()
        {
            var g=Root("BotanicalFrame");
            Box(g.transform,"Frame backing",Vector3.zero,new Vector3(.43f,.53f,.02f),darkWood,.002f);
            Box(g.transform,"Botanical paper",new Vector3(0,0,-.014f),new Vector3(.365f,.465f,.004f),paper,.001f);
            for(int side=-1;side<=1;side+=2)
            {
                Box(g.transform,"Upright molding",new Vector3(side*.207f,0,-.025f),new Vector3(.025f,.53f,.025f),brass,.005f);
                Box(g.transform,"Cross molding",new Vector3(0,side*.257f,-.025f),new Vector3(.43f,.025f,.025f),brass,.005f);
            }
            // Actual small relief botanical artwork; no dependency on a placeholder image.
            Rod(g.transform,"Illustrated stem",new Vector3(0,-.18f,-.021f),new Vector3(.012f,.17f,-.021f),.002f,green);
            for(int i=0;i<6;i++)
            {
                float side=i%2==0?-1:1;
                var leaf=Ball(g.transform,"Botanical leaf",new Vector3(side*.04f,-.11f+i*.048f,-.024f),new Vector3(.085f,.035f,.002f),sage);
                leaf.transform.localRotation=Quaternion.Euler(0,0,side*28);
            }
            return g;
        }

        static GameObject Tray()
        {
            var g=Root("MetalTray");
            Box(g.transform,"Brass tray base",new Vector3(0,.006f,0),new Vector3(.26f,.012f,.15f),brass,.014f);
            for(int side=-1;side<=1;side+=2)
            {
                Box(g.transform,"Long rim",new Vector3(0,.017f,side*.072f),new Vector3(.25f,.025f,.005f),brass,.002f);
                Box(g.transform,"Short rim",new Vector3(side*.126f,.017f,0),new Vector3(.005f,.025f,.14f),brass,.002f);
            }
            Box(g.transform,"Small label",new Vector3(-.04f,.015f,0),new Vector3(.11f,.001f,.07f),paper,.003f);
            var seal=Mat("WaxSeal","#984f49",null,0,.32f);
            Lathe(g.transform,"Wax seal",new Vector3(.07f,.013f,-.015f),new[]{new Vector2(0,0),new Vector2(.025f,.001f),new Vector2(.026f,.005f),new Vector2(.02f,.009f),new Vector2(0,.009f)},seal,24);
            return g;
        }

        static GameObject ClipBox()
        {
            var g=Root("GlassClipBox");
            Box(g.transform,"Glass bottom",new Vector3(0,.005f,0),new Vector3(.2f,.01f,.13f),glass,.004f);
            for(int side=-1;side<=1;side+=2)
            {
                Box(g.transform,"Glass long wall",new Vector3(0,.037f,side*.062f),new Vector3(.2f,.064f,.005f),glass,.002f);
                Box(g.transform,"Glass end wall",new Vector3(side*.097f,.037f,0),new Vector3(.005f,.064f,.13f),glass,.002f);
                Rod(g.transform,"Brass edging",new Vector3(-.1f,.07f,side*.065f),new Vector3(.1f,.07f,side*.065f),.002f,brass);
            }
            for(int i=0;i<7;i++)
            {
                float x=-.068f+i*.022f; float z=(i%3-1)*.025f;
                Rod(g.transform,"Paper clip",new Vector3(x,.014f,z-.018f),new Vector3(x,.014f,z+.018f),.0012f,brass);
                Rod(g.transform,"Paper clip",new Vector3(x+.01f,.014f,z-.018f),new Vector3(x+.01f,.014f,z+.018f),.0012f,brass);
                Rod(g.transform,"Paper clip bend",new Vector3(x,.014f,z+.018f),new Vector3(x+.01f,.014f,z+.018f),.0012f,brass);
            }
            return g;
        }

        static void TopQuad(Transform parent,string name,Vector3 pos,float width,float depth,Material material)
        {
            string key="Top_"+width.ToString("F4",CultureInfo.InvariantCulture)+"_"+depth.ToString("F4",CultureInfo.InvariantCulture);
            if(!meshes.TryGetValue(key,out var mesh))
            {
                mesh=new Mesh { name=key };
                mesh.vertices=new[]{new Vector3(-width/2,0,-depth/2),new Vector3(-width/2,0,depth/2),new Vector3(width/2,0,depth/2),new Vector3(width/2,0,-depth/2)};
                mesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
                mesh.triangles=new[]{0,1,2,0,2,3}; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                mesh=SaveMesh(mesh,key); meshes[key]=mesh;
            }
            Shape(parent,name,pos,mesh,material);
        }

        static Mesh BevelBox(Vector3 size,float bevel)
        {
            string key="Bevel_"+size.x.ToString("F4",CultureInfo.InvariantCulture)+"_"+size.y.ToString("F4",CultureInfo.InvariantCulture)+"_"+size.z.ToString("F4",CultureInfo.InvariantCulture)+"_"+bevel.ToString("F4",CultureInfo.InvariantCulture);
            if(meshes.TryGetValue(key,out var existing)) return existing;
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var tri=new List<int>();
            Vector3 h=size*.5f; Vector3 inner=h-Vector3.one*bevel;
            // Subdivided cube surfaces projected onto the rounded-box Minkowski surface.
            // Dense edge samples give small bevels a real silhouette and usable flat face UVs.
            for(int face=0;face<6;face++)
            {
                int axis=face/2; float sign=face%2==0?1:-1;
                int ax1=(axis+1)%3,ax2=(axis+2)%3;
                float[] xs={-h[ax1],-inner[ax1],inner[ax1],h[ax1]};
                float[] ys={-h[ax2],-inner[ax2],inner[ax2],h[ax2]};
                for(int y=0;y<3;y++) for(int x=0;x<3;x++)
                {
                    int start=v.Count;
                    foreach(var xy in new[]{new Vector2(xs[x],ys[y]),new Vector2(xs[x+1],ys[y]),new Vector2(xs[x+1],ys[y+1]),new Vector2(xs[x],ys[y+1])})
                    {
                        Vector3 p=Vector3.zero; p[axis]=h[axis]*sign;p[ax1]=xy.x;p[ax2]=xy.y;
                        Vector3 c=new Vector3(Mathf.Clamp(p.x,-inner.x,inner.x),Mathf.Clamp(p.y,-inner.y,inner.y),Mathf.Clamp(p.z,-inner.z,inner.z));
                        p=c+(p-c).normalized*bevel;
                        if(bevel==0) {p[axis]=h[axis]*sign;p[ax1]=xy.x;p[ax2]=xy.y;}
                        v.Add(p); uv.Add(new Vector2((xy.x/h[ax1]+1)*.5f,(xy.y/h[ax2]+1)*.5f));
                    }
                    if(sign>0) tri.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                    else tri.AddRange(new[]{start,start+2,start+1,start,start+3,start+2});
                }
            }
            var mesh=new Mesh {name=key};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,key);meshes[key]=mesh;return mesh;
        }

        static Mesh LatheMesh(string name,Vector2[] profile,int segments)
        {
            string key="Lathe_"+segments;
            foreach(var p in profile) key+="_"+p.x.ToString("F4",CultureInfo.InvariantCulture)+"x"+p.y.ToString("F4",CultureInfo.InvariantCulture);
            // A stable hash keeps file names below Windows path limits.
            key="Lathe_"+Hash128.Compute(key).ToString();
            if(meshes.TryGetValue(key,out var existing))return existing;
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            float min=profile[0].y,max=min;foreach(var p in profile){min=Mathf.Min(min,p.y);max=Mathf.Max(max,p.y);}
            for(int i=0;i<profile.Length;i++) for(int j=0;j<=segments;j++)
            {
                float a=j*Mathf.PI*2/segments;v.Add(new Vector3(Mathf.Sin(a)*profile[i].x,profile[i].y,Mathf.Cos(a)*profile[i].x));
                uv.Add(new Vector2((float)j/segments,Mathf.Abs(max-min)<.0001f?profile[i].x/.06f:(profile[i].y-min)/(max-min)));
            }
            for(int i=0;i<profile.Length-1;i++) for(int j=0;j<segments;j++)
            {
                int a=i*(segments+1)+j,b=a+segments+1;
                t.AddRange(new[]{a,a+1,b,a+1,b+1,b});
            }
            var mesh=new Mesh {name=name};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,key);meshes[key]=mesh;return mesh;
        }

        static Mesh ShadeMesh()
        {
            const string key="BankersLampShade";
            if(meshes.TryGetValue(key,out var existing))return existing;
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();int n=32;
            for(int side=0;side<2;side++)for(int i=0;i<=n;i++)
            {
                float a=i*Mathf.PI/n;v.Add(new Vector3((side-.5f)*.36f,Mathf.Sin(a)*.075f,Mathf.Cos(a)*.075f));uv.Add(new Vector2(side,(float)i/n));
            }
            for(int i=0;i<n;i++){int a=i,b=i+n+1;t.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
            var mesh=new Mesh{name=key};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=SaveMesh(mesh,key);meshes[key]=mesh;return mesh;
        }

        static Mesh SaveMesh(Mesh mesh,string key)
        {
            string p="Assets/Models/"+key+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if(existing) {UnityEngine.Object.DestroyImmediate(mesh);return existing;}
            AssetDatabase.CreateAsset(mesh,p);return mesh;
        }

        static Texture2D WindowAtmosphere()
        {
            const string path="Assets/Textures/WindowAtmosphere.png";
            if(!File.Exists(path))
            {
                const int n=1024;
                var image=new Texture2D(n,n,TextureFormat.RGB24,false);
                var pixels=new Color[n*n];
                var low=Hex("#f0e8d1");var high=Hex("#b9d0d0");
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    float t=(float)y/(n-1);
                    var c=Color.Lerp(low,high,t);
                    float cloud=Mathf.Exp(-Mathf.Pow((t-.65f)/.12f,2))*.16f*(.6f+.4f*Mathf.Sin(x*.004f));
                    pixels[y*n+x]=Color.Lerp(c,Color.white,cloud);
                }
                image.SetPixels(pixels);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [MenuItem("Vintage Journal/Build Windows demo")]
        public static void BuildWindows()
        {
            if(!File.Exists(ScenePath)) Build();
            Directory.CreateDirectory("Build/Windows"); Directory.CreateDirectory("docs/logs");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{ScenePath}, locationPathName="Build/Windows/VintageJournalDesk.exe",
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.None });
            var summary=report.summary;
            File.WriteAllText("docs/logs/windows-build.txt", "Result: "+summary.result+"\nErrors: "+summary.totalErrors+"\nWarnings: "+summary.totalWarnings+"\nBytes: "+summary.totalSize+"\nDuration: "+summary.totalTime+"\n");
            Debug.Log("[DeskBuild] Windows "+summary.result+", errors="+summary.totalErrors);
            if(summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Windows build failed. See build log.");
        }
    }
}
