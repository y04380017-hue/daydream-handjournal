using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Night collectibles are independent 3D objects, dispatched by the shared workspace input router.</summary>
    public sealed class NightCollectionController : MonoBehaviour
    {
        public static NightCollectionController Instance { get; private set; }
        public WorkspaceGameplayController workspace;
        public DreamTimeController timeOfDay;
        [Header("World-space locations behind and above the stationary work surface")]
        public Vector3[] positions = {
            new Vector3(-.23f, 1.00f, .43f), new Vector3(-.04f, 1.04f, .46f), new Vector3(.15f, 1.00f, .43f),
            new Vector3(.34f, 1.04f, .46f), new Vector3(.53f, 1.00f, .43f), new Vector3(0, 1.12f, .46f)
        };
        readonly Dictionary<string, NightCollectible> views = new Dictionary<string, NightCollectible>();
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<Spark> sparks = new List<Spark>();
        Transform root;
        Material glow, gold, paper, trim, wing, lilac;
        AudioSource audioSource;
        AudioClip catchSound;
        Mesh starMesh;
        float elapsed;
        float nextSkyBinding;
        public string LastCatch { get; private set; }
        public int VisibleCount => views.Values.Count(v => v && v.gameObject.activeInHierarchy);
        public IEnumerable<NightCollectible> Collectibles => views.Values;
        public bool TryGetTokenPosition(string id,out Vector3 point)
        {point=default;if(string.IsNullOrEmpty(id)||!views.TryGetValue(id,out var view)||!view)return false;point=view.transform.position;return true;}
        public bool IsNightVisible => timeOfDay && (!DreamGameplayController.Instance || !DreamGameplayController.Instance.LegacyNightSlice || timeOfDay.IsNight && timeOfDay.NightAmount > .65f);

        sealed class Spark
        {
            public Transform view;
            public Vector3 origin, direction;
            public float age;
        }

        void Awake() { Instance = this; }
        void Start()
        {
            if (!workspace) workspace = FindFirstObjectByType<WorkspaceGameplayController>();
            if (!timeOfDay) timeOfDay = FindFirstObjectByType<DreamTimeController>();
        }

        void EnsureVisuals()
        {
            if (root) return;
            root = new GameObject("Night collection views").transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
            // Keep the owned views discoverable below the controller while preserving their world-space scale.
            root.SetParent(transform, true);
            glow = MaterialFor("Firefly glow", new Color(.62f, 1, .75f), 1.4f);
            gold = MaterialFor("Near star", new Color(1, .87f, .55f), .9f);
            paper = MaterialFor("Lantern paper", new Color(1, .85f, .64f), .65f);
            trim = MaterialFor("Lantern binding", new Color(.58f, .43f, .49f), .06f);
            wing = MaterialFor("Pearl wings", new Color(.80f, .87f, .95f), .18f);
            lilac = MaterialFor("Rare dream", new Color(.79f, .70f, 1), 1.1f);
            starMesh = MakeStar(); ownedMeshes.Add(starMesh);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0; audioSource.volume = .19f;
            catchSound = MakeCatchSound();
        }

        Material MaterialFor(string label, Color color, float emission)
        {
            // The desk already uses URP/Lit; this avoids adding a Shader.Find-only build dependency.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
            {
                var catalog = DecorationCatalog.Load();
                if (catalog) shader = catalog.paperShader;
            }
            if (!shader) shader = Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = label, hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .30f);
            if (material.HasProperty("_EmissionColor"))
            { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * emission); }
            ownedMaterials.Add(material); return material;
        }

        void Update()
        {
            var gameplay = DreamGameplayController.Instance;
            bool show = gameplay && gameplay.Ready && workspace && workspace.Ready && workspace.desk &&
                workspace.desk.HasBegun && IsNightVisible;
            if (!show)
            {
                if (root) root.gameObject.SetActive(false);
                return;
            }
            EnsureVisuals(); root.gameObject.SetActive(true);
            if(Time.unscaledTime>=nextSkyBinding){
                nextSkyBinding=Time.unscaledTime+3;
                foreach(var passer in FindObjectsByType<DreamSkyPasser>(FindObjectsInactive.Include,FindObjectsSortMode.None))FloatingMaterialTarget.Bind(passer);
            }
            elapsed += Time.unscaledDeltaTime;
            var tokens = gameplay.Data?.pending?.Where(t=>t.setting==gameplay.CurrentSetting).ToList();
            if (tokens == null) return;
            var ids = new HashSet<string>(tokens.Select(t => t.tokenId));
            foreach (var id in views.Keys.Where(id => !ids.Contains(id)).ToArray())
            { if (views[id]) Destroy(views[id].gameObject); views.Remove(id); }
            foreach (var token in tokens)
            {
                if (views.ContainsKey(token.tokenId)) continue;
                int slot = 0;
                for (; slot < positions.Length; slot++)
                    if (!views.Values.Any(v => v && (v.Home - positions[slot]).sqrMagnitude < .0001f)) break;
                // Inventory capacity is enforced by the data service. Extra migrated tokens wait safely in data.
                if (slot >= positions.Length) break;
                var view = Build(token.visualKind, token.rarity,token.setting);
                view.Bind(token.tokenId, token.visualKind, token.rarity, positions[slot], slot);
                views.Add(token.tokenId, view);
                Debug.Log("[NightCollection] Appeared " + token.tokenId + " " + token.assetId);
            }
            foreach (var view in views.Values) if (view) view.Sample(elapsed);
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var spark = sparks[i]; spark.age += Time.unscaledDeltaTime;
                if (spark.age > .65f) { if (spark.view) Destroy(spark.view.gameObject); sparks.RemoveAt(i); continue; }
                if (spark.view)
                {
                    spark.view.position = spark.origin + spark.direction * spark.age + Vector3.up * spark.age * .025f;
                    spark.view.localScale = Vector3.one * (.005f * (1 - spark.age / .65f));
                }
            }
        }

        /// <summary>No raw input here: the shared router calls this only after panels, held items and mechanisms.</summary>
        public bool TryClick(Ray ray)
        {
            var gameplay = DreamGameplayController.Instance;
            if (!gameplay || !gameplay.Ready || !gameplay.CanCollect || !IsNightVisible || !root || !root.gameObject.activeInHierarchy)
                return false;
            if (!Physics.Raycast(ray, out var hit, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            if(hit.collider.GetComponentInParent<ScenicOnlyMarker>()||!gameplay.CanCollectDirection(hit.point)||!gameplay.CanCollectDirection(hit.collider.bounds.center))return false;
            var skyTarget=hit.collider.GetComponentInParent<FloatingMaterialTarget>();
            if(skyTarget){
                if(!skyTarget.CanClaim(gameplay.CurrentSetting)){gameplay.ShowCollectionFeedback("这趟漂浮的礼物已经收好，等它下一次经过吧。");return true;}
                var token=gameplay.Data.pending.FirstOrDefault(t=>t.setting==gameplay.CurrentSetting);
                if(token==null){gameplay.ShowCollectionFeedback(AtelierProgressCatalog.SettingName(gameplay.CurrentSetting)+"的素材已经收齐；约两小时后会有新的礼物。");return true;}
                if(gameplay.TryCatch(token.tokenId,hit.point)){
                    skyTarget.MarkClaimed(gameplay.CurrentSetting,token.tokenId,token.assetId);LastCatch="收到了「"+NightMaterialCatalog.NameFor(token.assetId)+"」";PlayCatch(hit.point);
                    if(gameplay.discovery)gameplay.discovery.ShowReward(token,skyTarget.hitCollider);
                    Debug.Log("[NightCollection] SkyCaught "+token.assetId);
                }
                return true;
            }
            var view = hit.collider.GetComponentInParent<NightCollectible>();
            if (!view || !views.TryGetValue(view.TokenId, out var bound) || bound != view) return false;
            var pending=gameplay.Data.pending.FirstOrDefault(t => t.tokenId == view.TokenId);
            string asset = pending?.assetId;
            Vector3 point = view.transform.position;
            if (!gameplay.TryCatch(view.TokenId,point)) return true; // It still owns this click, including a rejected save.
            if(gameplay.discovery)gameplay.discovery.ShowReward(pending,view.HitCollider);
            LastCatch = "收到了「" + NightMaterialCatalog.NameFor(asset) + "」";
            views.Remove(view.TokenId);
            if(view.HitCollider)view.HitCollider.enabled=false;
            // Keep the actual caught object inside its ring briefly. Its disabled collider and
            // removed token cannot award again; this is a retiring view, never a second inventory item.
            Destroy(view.gameObject,1.5f);
            PlayCatch(point);
            Debug.Log("[NightCollection] Caught " + asset);
            return true;
        }

        NightCollectible Build(int kind, int rarity,int setting)
        {
            var item = new GameObject("Floating night material"); item.transform.SetParent(root, false);
            var target = item.AddComponent<NightCollectible>();
            var hit = item.AddComponent<SphereCollider>(); hit.radius = .042f;
            Material accent = rarity > 0 ? lilac : gold;
            if(kind==4||kind==5)
            {
                Primitive(item.transform,"Dream door frame",PrimitiveType.Cube,Vector3.zero,new Vector3(.04f,.058f,.008f),trim);
                Primitive(item.transform,"Dream doorway",PrimitiveType.Cube,new Vector3(0,0,-.006f),new Vector3(.031f,.049f,.003f),lilac);
                for(int i=0;i<3;i++)Primitive(item.transform,"Impossible step",PrimitiveType.Cube,new Vector3(0,-.029f-i*.008f,-.01f-i*.009f),new Vector3(.045f+i*.008f,.006f,.022f),wing);
            }
            else if(setting==1)
            {
                if(kind==0)
                {
                    Primitive(item.transform,"Iridescent bubble",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.038f,wing);
                    Primitive(item.transform,"Bubble shine",PrimitiveType.Sphere,new Vector3(-.008f,.009f,-.016f),Vector3.one*.01f,glow);
                }
                else if(kind==1)
                {
                    var left=Primitive(item.transform,"Paper crane left wing",PrimitiveType.Cube,new Vector3(-.015f,.009f,0),new Vector3(.029f,.002f,.025f),wing);left.localRotation=Quaternion.Euler(0,0,-32);
                    var right=Primitive(item.transform,"Paper crane right wing",PrimitiveType.Cube,new Vector3(.015f,.009f,0),new Vector3(.029f,.002f,.025f),paper);right.localRotation=Quaternion.Euler(0,0,32);
                    Primitive(item.transform,"Folded crane",PrimitiveType.Cube,Vector3.zero,new Vector3(.009f,.018f,.021f),wing);
                }
                else if(kind==2)for(int i=0;i<3;i++)Primitive(item.transform,"Small cloud puff",PrimitiveType.Sphere,new Vector3((i-1)*.014f,i%2*.006f,0),new Vector3(.027f,.024f,.024f),wing);
                else
                {
                    Primitive(item.transform,"Loose lined page",PrimitiveType.Cube,Vector3.zero,new Vector3(.035f,.047f,.0015f),paper);
                    for(int i=0;i<5;i++)Primitive(item.transform,"Notebook line",PrimitiveType.Cube,new Vector3(0,.015f-i*.007f,-.001f),new Vector3(.029f,.0007f,.0004f),wing);
                }
            }
            else if(setting==2)
            {
                if(kind==0)
                {
                    Primitive(item.transform,"Dandelion stem",PrimitiveType.Cube,new Vector3(0,-.012f,0),new Vector3(.0015f,.034f,.0015f),trim);
                    for(int i=0;i<7;i++){float a=i*Mathf.PI*2/7;Primitive(item.transform,"Dandelion seed",PrimitiveType.Sphere,new Vector3(Mathf.Cos(a)*.015f,.008f+Mathf.Sin(a)*.015f,0),Vector3.one*.006f,wing);}
                }
                else if(kind==1)
                {
                    Primitive(item.transform,"Pastel balloon",PrimitiveType.Sphere,new Vector3(0,.009f,0),new Vector3(.031f,.039f,.031f),lilac);
                    Primitive(item.transform,"Balloon thread",PrimitiveType.Cube,new Vector3(0,-.027f,0),new Vector3(.001f,.035f,.001f),trim);
                }
                else if(kind==2)
                {
                    var leaf=Primitive(item.transform,"Pressed leaf",PrimitiveType.Sphere,Vector3.zero,new Vector3(.024f,.044f,.004f),gold);leaf.localRotation=Quaternion.Euler(0,0,28);
                    Primitive(item.transform,"Leaf vein",PrimitiveType.Cube,Vector3.zero,new Vector3(.001f,.038f,.005f),trim);
                }
                else for(int i=0;i<5;i++)Primitive(item.transform,"Golden dust",PrimitiveType.Sphere,new Vector3(Mathf.Sin(i*2)*.018f,Mathf.Cos(i*2)*.018f,0),Vector3.one*.007f,gold);
            }
            else if (kind == 0)
            {
                Primitive(item.transform, "Firefly light", PrimitiveType.Sphere, new Vector3(0, -.007f, 0), new Vector3(.021f, .026f, .022f), rarity > 0 ? lilac : glow);
                Primitive(item.transform, "Firefly body", PrimitiveType.Sphere, new Vector3(0, .010f, 0), new Vector3(.011f, .024f, .012f), trim);
                Primitive(item.transform, "Left wing", PrimitiveType.Sphere, new Vector3(-.015f, .005f, -.004f), new Vector3(.024f, .006f, .020f), wing);
                Primitive(item.transform, "Right wing", PrimitiveType.Sphere, new Vector3(.015f, .005f, -.004f), new Vector3(.024f, .006f, .020f), wing);
            }
            else if (kind == 1)
            {
                var star = new GameObject("Extruded near star"); star.transform.SetParent(item.transform, false);
                star.transform.localRotation = Quaternion.Euler(-24, 0, 0);
                star.AddComponent<MeshFilter>().sharedMesh = starMesh;
                var renderer = star.AddComponent<MeshRenderer>(); renderer.sharedMaterial = accent; Quiet(renderer);
            }
            else
            {
                Primitive(item.transform, "Paper lantern", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.031f, .022f, .031f), rarity > 0 ? lilac : paper);
                Primitive(item.transform, "Upper rim", PrimitiveType.Cylinder, new Vector3(0, .022f, 0), new Vector3(.026f, .002f, .026f), trim);
                Primitive(item.transform, "Lower rim", PrimitiveType.Cylinder, new Vector3(0, -.022f, 0), new Vector3(.026f, .002f, .026f), trim);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3;
                    Primitive(item.transform, "Paper rib", PrimitiveType.Cube, new Vector3(Mathf.Sin(angle) * .0157f, 0, Mathf.Cos(angle) * .0157f),
                        new Vector3(.001f, .042f, .001f), trim);
                }
                Primitive(item.transform, "Handle left", PrimitiveType.Cube, new Vector3(-.009f, .030f, 0), new Vector3(.0015f, .015f, .0015f), trim);
                Primitive(item.transform, "Handle right", PrimitiveType.Cube, new Vector3(.009f, .030f, 0), new Vector3(.0015f, .015f, .0015f), trim);
                Primitive(item.transform, "Handle top", PrimitiveType.Cube, new Vector3(0, .037f, 0), new Vector3(.019f, .0015f, .0015f), trim);
                Primitive(item.transform, "Paper tassel", PrimitiveType.Cube, new Vector3(0, -.031f, 0), new Vector3(.002f, .018f, .002f), accent);
            }
            return target;
        }

        static Transform Primitive(Transform parent, string label, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = label; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            var collider = go.GetComponent<Collider>(); if (collider) { collider.enabled = false; Destroy(collider); }
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material; Quiet(renderer);
            return go.transform;
        }
        static void Quiet(Renderer renderer) { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }

        static Mesh MakeStar()
        {
            var vertices = new Vector3[22];
            vertices[0] = new Vector3(0, 0, -.006f); vertices[11] = new Vector3(0, 0, .006f);
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI * .5f + i * Mathf.PI / 5, radius = i % 2 == 0 ? .029f : .013f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -.006f);
                vertices[i + 12] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, .006f);
            }
            var triangles = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                int a = i + 1, b = (i + 1) % 10 + 1;
                triangles.AddRange(new[] { 0, b, a, 11, a + 11, b + 11, a, b, b + 11, a, b + 11, a + 11 });
            }
            var mesh = new Mesh { name = "Collectible extruded five-point star", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        void PlayCatch(Vector3 point)
        {
            if (audioSource && catchSound) audioSource.PlayOneShot(catchSound);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2 / 9;
                var spark = Primitive(root, "Catch sparkle", PrimitiveType.Sphere, Vector3.zero, Vector3.one * .005f, i % 2 == 0 ? gold : glow);
                spark.position = point;
                sparks.Add(new Spark { view = spark, origin = point, direction = new Vector3(Mathf.Cos(a) * .045f, Mathf.Sin(a) * .045f, -.005f) });
            }
        }

        static AudioClip MakeCatchSound()
        {
            const int rate = 22050, count = 6615;
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate, envelope = Mathf.Min(t * 100, 1) * Mathf.Exp(-t * 13);
                samples[i] = envelope * (.45f * Mathf.Sin(2 * Mathf.PI * 784 * t) + .22f * Mathf.Sin(2 * Mathf.PI * 1176 * t));
            }
            var clip = AudioClip.Create("Soft night catch", count, 1, rate, false); clip.SetData(samples, 0); return clip;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (root) Destroy(root.gameObject);
            foreach (var material in ownedMaterials) if (material) Destroy(material);
            foreach (var mesh in ownedMeshes) if (mesh) Destroy(mesh);
            if (catchSound) Destroy(catchSound);
            if (audioSource) Destroy(audioSource);
        }
    }
}
