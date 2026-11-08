using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>Adds a real-scale physical workspace to the saved Stage 3 scene, retaining its journal and data.</summary>
    public static class PhysicalWorkspaceSetup
    {
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        const string Folder = "Assets/PhysicalWorkspace";
        const float TableY = .76f;
        static float workspaceHeight = TableY;
        static Material walnut, brass, paper, ink, green;
        static Transform root;
        static readonly List<PhysicalInteractable> items = new List<PhysicalInteractable>();

        [Serializable] sealed class ArtAdaptationSettings
        {
            public Vector2 deskSize = new Vector2(1.35f, .72f);
            public float tableHeight = TableY;
            public float legacyObjectScale = .4f;
            public Vector2 journalPageSize = new Vector2(.148f, .210f);
            public Vector3 journalPosition = new Vector3(-.085f, TableY + .002f, -.075f);
        }
        [Serializable] sealed class Manifest
        {
            public string scene;
            public string units = "1 Unity Unit = 1 meter";
            public int tapeCount, stickerCount, noteCount, loosePageCount, travelersNotebookCount;
            public List<ItemRecord> items = new List<ItemRecord>();
        }
        [Serializable] sealed class ItemRecord
        {
            public string instanceId, assetId, type, prefab;
            public Vector3 metres;
        }

        [MenuItem("Vintage Journal/Stage 4/Install physical workspace into existing scene")]
        public static void Build()
        {
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Stage 3 scene must already exist.", ScenePath);
            foreach (var sub in new[] { "", "/Prefabs", "/Materials", "/Dimensions", "/Meshes" })
                Directory.CreateDirectory(Folder + sub);
            Directory.CreateDirectory("docs/stage4-logs");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // Only our generated additions are replaced on a repeat install. Source furniture and assets remain intact.
            var previous = GameObject.Find("PhysicalWorkspace");
            if (previous) Object.DestroyImmediate(previous);
            var settings = LoadAdaptation();
            workspaceHeight = settings.tableHeight;
            AdaptExistingScene(settings);
            walnut = ExistingMaterial("Walnut");
            brass = ExistingMaterial("AgedBrass");
            paper = ExistingMaterial("Paper");
            ink = ExistingMaterial("Ink");
            green = ExistingMaterial("SageEnamel");
            root = new GameObject("PhysicalWorkspace").transform;
            items.Clear();
            var operation = Child(root, "DeskOperationSurface");
            operation.position = new Vector3(0, settings.tableHeight, 0);
            var surface = operation.gameObject.AddComponent<DeskPlacementSurface>();
            surface.size = settings.deskSize;
            surface.liftHeight = .075f;
            var ignored = new List<Collider>();
            foreach (var name in new[] { "Desk", "LinenMat" })
            {
                var fixture = GameObject.Find(name);
                if (fixture) ignored.AddRange(fixture.GetComponentsInChildren<Collider>(true));
            }
            surface.ignoredColliders = ignored.ToArray();
            var inspect = Child(root, "SharedInspectAnchor");
            inspect.position = new Vector3(.10f, 1.03f, -.08f);
            inspect.rotation = Quaternion.identity;
            var controller = root.gameObject.AddComponent<PhysicalInteractionController>();
            controller.surface = surface;
            controller.inspectAnchor = inspect;
            BuildTapeRack();
            BuildStickerBox();
            BuildNoteBox();
            BuildPaperRack();
            BuildNotebookShelf();
            // Reserve slots only after all prefab references have been assigned.
            foreach (var item in items) item.Initialize();
            foreach (var container in root.GetComponentsInChildren<PhysicalContainer>()) container.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var manifest = new Manifest { scene = ScenePath };
            foreach (var item in items)
            {
                if (item.itemType == PhysicalItemType.Tape) manifest.tapeCount++;
                if (item.itemType == PhysicalItemType.Sticker) manifest.stickerCount++;
                if (item.itemType == PhysicalItemType.Note) manifest.noteCount++;
                if (item.itemType == PhysicalItemType.LoosePage) manifest.loosePageCount++;
                if (item.itemType == PhysicalItemType.TravelersNotebook) manifest.travelersNotebookCount++;
                manifest.items.Add(new ItemRecord { instanceId = item.itemInstanceId, assetId = item.assetId,
                    type = item.itemType.ToString(), metres = item.dimensions.size,
                    prefab = Folder + "/Prefabs/" + item.assetId + ".prefab" });
            }
            File.WriteAllText("docs/stage4-logs/asset-manifest.json", JsonUtility.ToJson(manifest, true));
            Debug.Log("[PhysicalSetup] Existing scene updated; physical items=" + items.Count + "; source assets retained.");
        }

        static ArtAdaptationSettings LoadAdaptation()
        {
            string path = Folder + "/LegacyArtAdaptation.json";
            var settings = File.Exists(path) ? JsonUtility.FromJson<ArtAdaptationSettings>(File.ReadAllText(path)) : new ArtAdaptationSettings();
            if (settings == null || settings.deskSize.x < 1 || settings.deskSize.y < .5f)
                throw new InvalidOperationException("Invalid physical workspace art-adaptation settings.");
            if (!File.Exists(path)) File.WriteAllText(path, JsonUtility.ToJson(settings, true));
            return settings;
        }

        static void AdaptExistingScene(ArtAdaptationSettings s)
        {
            var desk = GameObject.Find("Desk");
            if (!desk) throw new InvalidOperationException("The original desk is missing.");
            desk.transform.localScale = new Vector3(s.deskSize.x / 3.12f, s.tableHeight / TableY, s.deskSize.y / 1.8f);
            desk.transform.position = new Vector3(0, 0, -.1f * s.deskSize.y / 1.8f);
            var journal = Object.FindFirstObjectByType<JournalController>();
            if (!journal) throw new InvalidOperationException("The original Stage 3 journal is missing.");
            journal.transform.position = s.journalPosition;
            journal.transform.localScale = new Vector3(s.journalPageSize.x / .393f, s.legacyObjectScale, s.journalPageSize.y / .52f);
            // Keep the original prop objects so the installer is reversible. New physical containers replace their visual role.
            foreach (var name in new[] { "TapeRack", "StickerBox", "NoteBox", "PageHolder", "LooseTape_1", "LooseTape_2", "FourDrawers", "ClosedJournal" })
            {
                var old = GameObject.Find(name);
                if (old) old.SetActive(false);
            }
            AdaptProp("LinenMat", new Vector3(-.085f, s.tableHeight + .0002f, -.075f), new Vector3(.40f, .4f, .4f));
            AdaptProp("GreenBankersLamp", new Vector3(.60f, s.tableHeight, .245f), Vector3.one * .36f);
            AdaptProp("TeacupAndSaucer", new Vector3(.58f, s.tableHeight, -.31f), Vector3.one * .36f);
            AdaptProp("DaisyVase", new Vector3(.61f, s.tableHeight, .10f), Vector3.one * .30f);
            AdaptProp("BrassPenPot", new Vector3(.61f, s.tableHeight, -.005f), Vector3.one * .32f);
            AdaptProp("MetalTray", new Vector3(-.245f, s.tableHeight, -.27f), Vector3.one * .4f);
            AdaptProp("GlassClipBox", new Vector3(-.61f, s.tableHeight, -.325f), Vector3.one * .35f);
        }

        static void AdaptProp(string name, Vector3 position, Vector3 scale)
        {
            var prop = GameObject.Find(name);
            if (!prop) return;
            prop.transform.position = position;
            prop.transform.localScale = scale;
        }

        static void BuildTapeRack()
        {
            var rack = Container("WashiTray", "和纸胶带架 · 12 款", new Vector3(-.465f, workspaceHeight, .265f), false);
            Box(rack.transform, "Walnut base", new Vector3(0, .004f, 0), new Vector3(.308f, .008f, .144f), walnut);
            Rim(rack.transform, new Vector2(.308f, .144f), .016f);
            Label(rack.transform, "WASHI  /  12", new Vector3(0, .015f, -.075f), .0035f);
            var catalog = DecorationCatalog.Load();
            int n = 0;
            foreach (var definition in catalog.assets)
            {
                if (definition.category != DecorationCategory.Tape) continue;
                int col = n % 6, row = n / 6;
                var slot = Slot(rack, PhysicalItemType.Tape, definition.assetId, new Vector3(-.125f + col * .05f, .0155f, -.035f + row * .07f));
                var template = ItemTemplate(definition.assetId, definition.displayName, PhysicalItemType.Tape, new Vector3(.045f, .015f, .045f));
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TapeRoll_" + (n + 1).ToString("D2") + ".prefab");
                if (!source) throw new FileNotFoundException("Original tape roll prefab missing: " + definition.assetId);
                var art = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(art, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                art.transform.SetParent(template.visualRoot, false);
                foreach (var collider in art.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                FitModel(art.transform, template.dimensions.size);
                CopyArtMaterials(art);
                PlaceTemplate(template, slot);
                n++;
            }
            SaveFixture(rack.gameObject, "WashiTray");
        }

        static void BuildStickerBox()
        {
            var box = Container("StickerChest", "贴纸收纳盒 · 点击盒盖开合", new Vector3(-.465f, workspaceHeight, -.110f), true);
            BuildBox(box, new Vector2(.305f, .295f));
            Label(box.transform, "STICKERS", new Vector3(0, .014f, -.15f), .004f);
            int n = 0;
            foreach (var definition in DecorationCatalog.Load().assets)
            {
                if (definition.category != DecorationCategory.Sticker || n >= 12) continue;
                var slot = Slot(box, PhysicalItemType.Sticker, definition.assetId, new Vector3(-.108f + (n % 4) * .072f, .0063f, -.110f + (n / 4) * .073f));
                var item = ItemTemplate(definition.assetId, definition.displayName, PhysicalItemType.Sticker, new Vector3(.050f, .0006f, .050f));
                PaperVisual(item.visualRoot, item.dimensions.size, definition.texture, definition.assetId, false);
                PlaceTemplate(item, slot);
                n++;
            }
            // Reuse a definition/prefab while assigning a separate entity identity and exclusive home slot.
            var definitionCopy = DecorationCatalog.Load().Find("sticker_01");
            if (definitionCopy != null)
            {
                var extraSlot = Slot(box, PhysicalItemType.Sticker, definitionCopy.assetId + "_second", new Vector3(-.108f, .0063f, .109f));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/" + definitionCopy.assetId + ".prefab");
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                copy.transform.SetParent(root, true);
                copy.transform.SetPositionAndRotation(extraSlot.transform.position, extraSlot.transform.rotation);
                var entity = copy.GetComponent<PhysicalInteractable>();
                entity.itemInstanceId = "physical-" + definitionCopy.assetId + "-002";
                entity.homeSlot = extraSlot;
                entity.homeAnchor = extraSlot.transform;
                entity.inspectAnchor = root.Find("SharedInspectAnchor");
                items.Add(entity);
            }
            SaveFixture(box.gameObject, "StickerChest");
        }

        static void BuildNoteBox()
        {
            var box = Container("NoteChest", "便签盒 · 点击盒盖开合", new Vector3(.475f, workspaceHeight, -.105f), true);
            BuildBox(box, new Vector2(.19f, .279f));
            Label(box.transform, "NOTES", new Vector3(0, .014f, -.142f), .004f);
            int n = 0;
            foreach (var definition in DecorationCatalog.Load().assets)
            {
                if (definition.category != DecorationCategory.Note || n >= 6) continue;
                var position = new Vector3(-.044f + (n % 2) * .088f, .0113f, -.087f + (n / 2) * .087f);
                // The stationary stack remains while its top, independent sheet is carried away.
                Box(box.transform, "Stationary note stack " + n, position - Vector3.up * .003f, new Vector3(.075f, .005f, .075f), paper, false);
                var slot = Slot(box, PhysicalItemType.Note, definition.assetId, position);
                var item = ItemTemplate(definition.assetId, definition.displayName, PhysicalItemType.Note, new Vector3(.075f, .0006f, .075f));
                PaperVisual(item.visualRoot, item.dimensions.size, definition.texture, definition.assetId, true);
                PlaceTemplate(item, slot);
                n++;
            }
            SaveFixture(box.gameObject, "NoteChest");
        }

        static void BuildPaperRack()
        {
            var rack = Container("RefillRack", "内页架 · A5 / A6", new Vector3(-.08f, workspaceHeight, .255f), false);
            Box(rack.transform, "Holder base", new Vector3(0, .005f, 0), new Vector3(.325f, .01f, .13f), walnut);
            Box(rack.transform, "Holder back", new Vector3(0, .055f, .061f), new Vector3(.325f, .11f, .008f), walnut);
            Label(rack.transform, "REFILLS  /  A5 + A6", new Vector3(0, .018f, -.067f), .0033f);
            var catalog = JournalPaperCatalog.Load();
            for (int i = 0; i < 2; i++)
            {
                var definition = catalog.papers[i];
                var dimensions = i == 0 ? new Vector3(.148f, .0006f, .210f) : new Vector3(.105f, .0006f, .148f);
                float height = Mathf.Sin(70 * Mathf.Deg2Rad) * dimensions.z * .5f + .0102f;
                var slot = Slot(rack, PhysicalItemType.LoosePage, definition.id, new Vector3(i == 0 ? -.075f : .072f, height, -.008f));
                slot.transform.localRotation = Quaternion.Euler(-70, 0, 0);
                var item = ItemTemplate(definition.id, (i == 0 ? "A5 · " : "A6 · ") + definition.displayName, PhysicalItemType.LoosePage, dimensions);
                item.dimensions.pageSize = new Vector2(dimensions.x, dimensions.z);
                PaperVisual(item.visualRoot, dimensions, definition.texture, definition.id, true);
                PlaceTemplate(item, slot);
            }
            SaveFixture(rack.gameObject, "RefillRack");
        }

        static void BuildNotebookShelf()
        {
            var shelf = Container("TravelersShelf", "TN 旅行者手账架", new Vector3(.365f, workspaceHeight, .255f), false);
            Box(shelf.transform, "Shelf base", new Vector3(0, .006f, 0), new Vector3(.45f, .012f, .10f), walnut);
            Box(shelf.transform, "Shelf back", new Vector3(0, .055f, .045f), new Vector3(.45f, .11f, .008f), walnut);
            Label(shelf.transform, "TRAVELER'S NOTEBOOK", new Vector3(0, .017f, -.053f), .003f);
            string[] colors = { "#503527", "#AD713E", "#304C3A", "#713845" };
            string[] names = { "深棕皮革 TN · Standard", "焦糖皮革 TN · Standard", "墨绿皮革 TN · Passport", "酒红皮革 TN · Passport" };
            float[] xs = { -.165f, -.05f, .055f, .15f };
            for (int i = 0; i < 4; i++)
            {
                bool passport = i >= 2;
                var size = new Vector3(passport ? .090f : .110f, passport ? .134f : .210f, .018f);
                string id = "tn_" + (i + 1).ToString("D2");
                var slot = Slot(shelf, PhysicalItemType.TravelersNotebook, id, new Vector3(xs[i], .012f + size.y * .5f, 0));
                var item = ItemTemplate(id, names[i], PhysicalItemType.TravelersNotebook, size);
                item.dimensions.notebookFormat = passport ? TravelersNotebookFormat.Passport : TravelersNotebookFormat.Standard;
                item.dimensions.pageSize = new Vector2(size.x, size.y);
                var binding = item.gameObject.AddComponent<TravelersNotebookBinding>();
                binding.sizeType = passport ? TravelersNotebookSize.Passport : TravelersNotebookSize.Standard;
                var leather = Material("TN_Leather_" + i, colors[i], AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Leather.png"));
                binding.frontCover = Box(item.visualRoot, "Front cover", new Vector3(0, 0, -size.z * .5f + .001f), new Vector3(size.x, size.y, .002f), leather, false).transform;
                binding.backCover = Box(item.visualRoot, "Back cover", new Vector3(0, 0, size.z * .5f - .001f), new Vector3(size.x, size.y, .002f), leather, false).transform;
                binding.spine = Box(item.visualRoot, "Leather spine", new Vector3(-size.x * .5f + .001f, 0, 0), new Vector3(.002f, size.y, size.z), leather, false).transform;
                Box(item.visualRoot, "Insert paper block", Vector3.zero, new Vector3(size.x - .008f, size.y - .006f, size.z - .004f), paper, false);
                binding.elastic = Box(item.visualRoot, "Independent elastic", new Vector3(size.x * .29f, 0, -size.z * .5f - .0003f), new Vector3(.0016f, size.y, .0006f), ink, false).transform;
                binding.insertAnchor = Child(item.transform, "InsertAnchor");
                binding.openingHinge = Child(item.transform, "OpeningHinge");
                binding.openingHinge.localPosition = new Vector3(-size.x * .5f, 0, 0);
                binding.unfoldedAnchor = Child(item.transform, "UnfoldedAnchor");
                binding.unfoldedAnchor.localPosition = new Vector3(-size.x, 0, 0);
                PlaceTemplate(item, slot);
            }
            SaveFixture(shelf.gameObject, "TravelersShelf");
        }

        static PhysicalContainer Container(string name, string label, Vector3 position, bool closable)
        {
            var transform = Child(root, name);
            transform.position = position;
            var container = transform.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = label;
            container.canClose = closable;
            container.isOpen = true;
            container.openLidEuler = new Vector3(180, 0, 0);
            return container;
        }

        static void BuildBox(PhysicalContainer container, Vector2 size)
        {
            Box(container.transform, "Box base", new Vector3(0, .003f, 0), new Vector3(size.x, .006f, size.y), walnut);
            Rim(container.transform, size, .019f);
            var hinge = Child(container.transform, "LidHinge");
            hinge.localPosition = new Vector3(0, .020f, size.y * .5f);
            // Two rigid leaves close to one continuous lid. Opening folds them into a low stack
            // behind the box, leaving rear storage visible instead of erecting a full-height screen.
            Box(hinge, "Lid rear leaf", new Vector3(0, 0, -size.y * .25f), new Vector3(size.x, .006f, size.y * .5f), walnut);
            var fold = Child(hinge, "LidFoldHinge");
            fold.localPosition = new Vector3(0, .003f, -size.y * .5f);
            Box(fold, "Lid front leaf", new Vector3(0, -.003f, -size.y * .25f), new Vector3(size.x, .006f, size.y * .5f), walnut);
            Box(fold, "Brass pull tab", new Vector3(0, -.003f, -size.y * .5f - .003f), new Vector3(.034f, .002f, .014f), brass);
            container.lid = hinge;
            container.foldingLid = fold;
            container.closedFoldEuler = Vector3.zero;
            container.openFoldEuler = new Vector3(180, 0, 0);
            container.Refresh();
        }

        static void Rim(Transform parent, Vector2 size, float height)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Box(parent, "Side rim", new Vector3(sign * (size.x * .5f - .003f), height * .5f, 0), new Vector3(.006f, height, size.y), walnut);
                Box(parent, "Front/back rim", new Vector3(0, height * .5f, sign * (size.y * .5f - .003f)), new Vector3(size.x, height, .006f), walnut);
            }
        }

        static PhysicalStorageSlot Slot(PhysicalContainer container, PhysicalItemType type, string id, Vector3 localPosition)
        {
            var anchor = Child(container.transform, "Home_" + id);
            anchor.localPosition = localPosition;
            var slot = anchor.gameObject.AddComponent<PhysicalStorageSlot>();
            slot.container = container;
            slot.acceptedType = type;
            slot.snapRadius = type == PhysicalItemType.Tape ? .025f : .04f;
            return slot;
        }

        static PhysicalInteractable ItemTemplate(string id, string label, PhysicalItemType type, Vector3 size)
        {
            var go = new GameObject(id);
            var item = go.AddComponent<PhysicalInteractable>();
            item.itemInstanceId = "physical-" + id + "-001";
            item.assetId = id;
            item.displayName = label;
            item.itemType = type;
            item.dimensions = Dimensions(id, size);
            item.interactionCollider = go.AddComponent<BoxCollider>();
            item.interactionCollider.size = size;
            item.visualRoot = Child(go.transform, "Visual");
            item.grabAnchor = Child(go.transform, "GrabAnchor");
            item.inspectAnchor = Child(go.transform, "InspectAnchor");
            item.placementAnchor = Child(go.transform, "PlacementAnchor");
            item.placementAnchor.localPosition = Vector3.down * size.y * .5f;
            return item;
        }

        static PhysicalDimensionProfile Dimensions(string id, Vector3 size)
        {
            string path = Folder + "/Dimensions/" + id + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<PhysicalDimensionProfile>(path);
            if (!profile) { profile = ScriptableObject.CreateInstance<PhysicalDimensionProfile>(); AssetDatabase.CreateAsset(profile, path); }
            profile.size = size;
            profile.colliderSize = size;
            profile.grabOffset = Vector3.zero;
            profile.minDistance = .1f;
            profile.maxDistance = 2f;
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void PlaceTemplate(PhysicalInteractable template, PhysicalStorageSlot slot)
        {
            string path = Folder + "/Prefabs/" + template.assetId + ".prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(template.gameObject, path);
            Object.DestroyImmediate(template.gameObject);
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            placed.transform.SetParent(root, true);
            placed.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
            var item = placed.GetComponent<PhysicalInteractable>();
            item.homeSlot = slot;
            item.homeAnchor = slot.transform;
            item.inspectAnchor = root.Find("SharedInspectAnchor");
            items.Add(item);
        }

        static void SaveFixture(GameObject fixture, string name)
        {
            // Fixtures own independent slot anchors; items remain separate entity prefab instances in the scene.
            var position = fixture.transform.position;
            fixture.transform.position = Vector3.zero;
            PrefabUtility.SaveAsPrefabAsset(fixture, Folder + "/Prefabs/" + name + ".prefab");
            fixture.transform.position = position;
        }

        static void PaperVisual(Transform parent, Vector3 dimensions, Texture2D texture, string id, bool backing)
        {
            if (backing) Box(parent, "Independent paper backing", Vector3.zero, dimensions, paper, false);
            var art = Child(parent, "Replaceable artwork");
            var mesh = TopMesh(id, dimensions.x, dimensions.z);
            art.localPosition = Vector3.up * (dimensions.y * .5f + .00001f);
            art.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = art.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(id + "_art", "#FFFFFF", texture, true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Mesh TopMesh(string id, float width, float depth)
        {
            string path = Folder + "/Meshes/" + id + "_surface.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) return mesh;
            mesh = new Mesh { name = id + " independent surface" };
            mesh.vertices = new[] { new Vector3(-width / 2, 0, -depth / 2), new Vector3(-width / 2, 0, depth / 2), new Vector3(width / 2, 0, depth / 2), new Vector3(width / 2, 0, -depth / 2) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void FitModel(Transform model, Vector3 desiredSize)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            var bound = new Bounds(); bool any = false;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    point = model.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (!any) { bound = new Bounds(point, Vector3.zero); any = true; } else bound.Encapsulate(point);
                }
            }
            if (!any) throw new InvalidOperationException("A source model contains no renderable meshes.");
            var scale = new Vector3(desiredSize.x / bound.size.x, desiredSize.y / bound.size.y, desiredSize.z / bound.size.z);
            model.localScale = scale;
            model.localPosition = -Vector3.Scale(bound.center, scale);
        }

        static void CopyArtMaterials(GameObject art)
        {
            foreach (var renderer in art.GetComponentsInChildren<Renderer>())
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (!shared[i]) continue;
                    string path = Folder + "/Materials/Reuse_" + shared[i].name + ".mat";
                    var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!copy) { copy = new Material(shared[i]); AssetDatabase.CreateAsset(copy, path); }
                    shared[i] = copy;
                }
                renderer.sharedMaterials = shared;
            }
        }

        static Material ExistingMaterial(string name)
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + name + ".mat");
            string path = Folder + "/Materials/" + name + ".mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!copy)
            {
                copy = original ? new Material(original) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(copy, path);
            }
            return copy;
        }

        static Material Material(string name, string hex, Texture2D texture, bool alpha = false)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            ColorUtility.TryParseHtmlString(hex, out var color);
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", .13f);
            material.SetFloat("_Cull", 0);
            if (alpha) { material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .1f); material.EnableKeyword("_ALPHATEST_ON"); }
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        static void Label(Transform parent, string text, Vector3 position, float characterSize)
        {
            var label = Child(parent, "Label");
            label.localPosition = position;
            var mesh = label.gameObject.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 64;
            mesh.characterSize = characterSize;
            mesh.color = new Color(.9f, .77f, .50f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font) { mesh.font = font; label.GetComponent<Renderer>().sharedMaterial = font.material; }
        }
    }
}
