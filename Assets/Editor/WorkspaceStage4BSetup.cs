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
    /// <summary>Incremental Stage 4B furniture and physical-stock installation into the existing saved desk.</summary>
    public static class WorkspaceStage4BSetup
    {
        const string Folder = "Assets/WorkspaceStage4B";
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        const float TableY = .76f;
        static Transform root;
        static Material wood, darkWood, brass, paper, acrylic, edge, ink;
        static readonly Dictionary<string, PhysicalInteractable> stock = new Dictionary<string, PhysicalInteractable>();
        static readonly List<PhysicalStorageSlot> slots = new List<PhysicalStorageSlot>();
        static readonly List<WorkspaceDrawer> drawers = new List<WorkspaceDrawer>();
        static readonly List<string> prefabPaths = new List<string>();
        static Transform inspect;

        [Serializable] sealed class Manifest
        {
            public string scene = ScenePath;
            public string units = "meters";
            public int tapeRolls, stickers, notes, loosePages, travelersNotebooks, releaseBooks, drawers, shafts;
            public List<Record> items = new List<Record>();
            public string[] prefabs;
        }
        [Serializable] sealed class Record
        {
            public string instanceId, assetId, type, home, prefab;
            public Vector3 dimensions;
        }

        [MenuItem("Vintage Journal/Stage 4B/Install workspace furniture and stock")]
        public static void Build()
        {
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("The existing Stage 4 desk is required.", ScenePath);
            foreach (string path in new[] { Folder, Folder + "/Prefabs", Folder + "/Materials", Folder + "/Meshes", Folder + "/Dimensions" })
                Directory.CreateDirectory(path);
            Directory.CreateDirectory("docs/stage4b-logs");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<PhysicalInteractionController>();
            if (!controller) throw new InvalidOperationException("The saved Stage 4 physical system is missing.");
            var previousContainers = Object.FindObjectsByType<PhysicalContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            stock.Clear(); slots.Clear(); drawers.Clear(); prefabPaths.Clear();
            // Preserve every existing entity and its persistent ID before replacing only our generated fixture group.
            foreach (var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!item.gameObject.scene.IsValid()) continue;
                if (string.IsNullOrEmpty(item.itemInstanceId) || stock.ContainsKey(item.itemInstanceId))
                    throw new InvalidOperationException("Existing physical stock has a missing/duplicate entity ID.");
                // Scene bindings and poses must survive import/save. Retain the reusable prefab asset,
                // while making this entity an explicit scene instance instead of unrecorded prefab overrides.
                if (PrefabUtility.IsPartOfPrefabInstance(item.gameObject))
                    PrefabUtility.UnpackPrefabInstance(item.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                stock.Add(item.itemInstanceId, item);
                item.transform.SetParent(controller.transform, true);
            }
            var old = GameObject.Find("WorkspaceStage4B");
            if (old) Object.DestroyImmediate(old);
            root = new GameObject("WorkspaceStage4B").transform;
            foreach (var item in stock.Values) { item.transform.SetParent(root, true); item.gameObject.SetActive(true); }
            inspect = controller.inspectAnchor;
            // Original fixture names/parents can change after prefab saves. Component references identify
            // the old storage structures even when they are independent scene roots.
            foreach (var previous in previousContainers)
            {
                if (!previous || previous.transform.IsChildOf(root)) continue;
                previous.gameObject.SetActive(false);
                EditorUtility.SetDirty(previous.gameObject);
                if (PrefabUtility.IsPartOfPrefabInstance(previous.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(previous.gameObject);
            }
            wood = CopyMaterial("Walnut", "Assets/Materials/Walnut.mat");
            darkWood = CopyMaterial("DarkWalnut", "Assets/Materials/DarkWalnut.mat");
            brass = CopyMaterial("AgedBrass", "Assets/Materials/AgedBrass.mat");
            paper = CopyMaterial("Paper", "Assets/Materials/Paper.mat");
            ink = MakeMaterial("AnimalInk", new Color(.075f, .070f, .065f), null);
            acrylic = MakeMaterial("ClearAcrylic", new Color(.72f, .91f, .89f, .13f), null, true);
            edge = MakeMaterial("AcrylicEdges", new Color(.53f, .76f, .73f, .48f), null, true);
            BuildCabinet();
            BuildAnimalStand("BlackCatTapeStand", "小黑猫胶带架", new Vector3(-.620f, TableY, -.310f), 0);
            BuildAnimalStand("PuppyTapeStand", "小狗胶带架", new Vector3(-.500f, TableY, -.310f), 1);
            BuildAnimalStand("BirdTapeStand", "小鸟胶带架", new Vector3(-.380f, TableY, -.310f), 2);
            BuildHangingStickers();
            BuildPaperTiers();
            BuildNoteBoxes();
            BuildTravelersBookcase();
            MoveLegacyProp("BrassPenPot", new Vector3(.615f, TableY, -.05f));
            MoveLegacyProp("DaisyVase", new Vector3(.615f, TableY, .08f));
            MoveLegacyProp("GlassClipBox", new Vector3(-.120f, TableY, -.325f));
            MoveLegacyProp("MetalTray", new Vector3(-.015f, TableY, -.300f));
            MoveLegacyProp("LinenMat", new Vector3(-.085f, TableY + .0002f, -.125f));
            var journal = Object.FindFirstObjectByType<JournalController>();
            if (journal)
            {
                Vector3 position = journal.transform.position;
                position.z = -.125f;
                journal.transform.position = position;
                EditorUtility.SetDirty(journal.transform);
                if (PrefabUtility.IsPartOfPrefabInstance(journal.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(journal.transform);
            }
            ConfigureSeatedCamera();
            foreach (var item in stock.Values)
            {
                item.Initialize();
                EditorUtility.SetDirty(item);
                EditorUtility.SetDirty(item.transform);
                EditorUtility.SetDirty(item.interactionCollider);
                if (PrefabUtility.IsPartOfPrefabInstance(item.gameObject))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.interactionCollider);
                    if (item.visualRoot) PrefabUtility.RecordPrefabInstancePropertyModifications(item.visualRoot);
                }
            }
            foreach (var drawer in drawers) drawer.SetOpenAmount(0);
            foreach (var container in root.GetComponentsInChildren<PhysicalContainer>(true)) container.Refresh();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WriteManifest();
            Debug.Log("[Workspace4BSetup] Incremental scene saved: " + stock.Count + " entities, " + slots.Count + " slots; 3 drawers, 15 releasable shafts.");
        }

        static void BuildCabinet()
        {
            var cabinet = Child(root, "AcrylicTapeCabinet");
            cabinet.position = new Vector3(-.475f, TableY, .025f);
            const float w = .320f, d = .180f, h = .195f;
            Box(cabinet, "Walnut plinth", new Vector3(0, .003f, 0), new Vector3(w + .008f, .006f, d + .006f), darkWood);
            Box(cabinet, "Clear top", new Vector3(0, h, 0), new Vector3(w, .003f, d), acrylic);
            Box(cabinet, "Clear back", new Vector3(0, h * .5f, d * .5f), new Vector3(w, h, .003f), acrylic);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Box(cabinet, "Clear side", new Vector3(sign * w * .5f, h * .5f, 0), new Vector3(.003f, h, d), acrylic);
                Box(cabinet, "Polished front edge", new Vector3(sign * w * .5f, h * .5f, -d * .5f), new Vector3(.002f, h, .002f), edge, false);
            }
            var top = MakeDrawer(cabinet, "drawer-tape-top", "胶带抽屉 · 上层", .132f);
            var middle = MakeDrawer(cabinet, "drawer-tape-middle", "胶带抽屉 · 中层", .073f);
            var lower = MakeDrawer(cabinet, "drawer-release-books", "离型本抽屉", .012f);
            for (int i = 0; i < 15; i++)
            {
                string assetId = "tape_" + (i % 12 + 1).ToString("D2");
                string instance = "physical-" + assetId + (i < 12 ? "-001" : "-002");
                var item = ExistingOrClone(instance, assetId, PhysicalItemType.Tape);
                var drawer = i < 8 ? top : middle;
                int local = i < 8 ? i : i - 8;
                var slot = Slot(drawer.slide, drawer.container, "home-" + instance, PhysicalItemType.Tape,
                    new Vector3(-.090f + local % 4 * .060f, .009f, -.040f + local / 4 * .080f));
                BindHome(item, slot);
            }
            for (int i = 0; i < 2; i++)
            {
                string id = i == 0 ? "ReleaseBook_A" : "ReleaseBook_B";
                var book = CreateReleaseBook(id, i);
                var slot = Slot(lower.slide, lower.container, "home-" + id, PhysicalItemType.ReleaseBook,
                    new Vector3(i == 0 ? -.072f : .072f, .0075f, 0));
                BindHome(book, slot);
            }
            SaveFixture(cabinet.gameObject, "AcrylicTapeCabinet");
        }

        static WorkspaceDrawer MakeDrawer(Transform parent, string id, string label, float y)
        {
            var drawerRoot = Child(parent, id);
            drawerRoot.localPosition = new Vector3(0, y, 0);
            var container = drawerRoot.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = label;
            container.canClose = true;
            container.isOpen = false;
            container.contentsVisibleWhenClosed = true;
            var slide = Child(drawerRoot, "Slide");
            Box(slide, "Clear drawer floor", Vector3.zero, new Vector3(.310f, .003f, .172f), acrylic);
            for (int sign = -1; sign <= 1; sign += 2)
                Box(slide, "Drawer side rail", new Vector3(sign * .153f, .019f, 0), new Vector3(.003f, .038f, .172f), acrylic);
            Box(slide, "Drawer front", new Vector3(0, .020f, -.087f), new Vector3(.310f, .040f, .003f), acrylic);
            Box(slide, "Drawer rear", new Vector3(0, .020f, .087f), new Vector3(.310f, .040f, .003f), acrylic);
            Box(slide, "Independent brass handle", new Vector3(0, .023f, -.098f), new Vector3(.067f, .014f, .012f), brass);
            Label(slide, label.Contains("离型") ? "RELEASE BOOKS" : label.Contains("上") ? "WASHI  I" : "WASHI  II", new Vector3(0, .043f, -.090f), .0028f);
            var drawer = drawerRoot.gameObject.AddComponent<WorkspaceDrawer>();
            drawer.drawerId = id;
            drawer.slide = slide;
            drawer.closedLocalPosition = Vector3.zero;
            drawer.slideAxis = Vector3.back;
            // Full extension on the book drawer clears the rear edge of an A6 book
            // from the middle drawer's protruding solid handle before a vertical lift.
            drawer.travel = id == "drawer-release-books" ? .19f : .17f;
            drawer.container = container;
            drawers.Add(drawer);
            return drawer;
        }

        static void BuildAnimalStand(string name, string label, Vector3 position, int animal)
        {
            var stand = Child(root, name);
            stand.position = position;
            var container = stand.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = label;
            container.canClose = false;
            container.isOpen = true;
            Box(stand, "Walnut foot", new Vector3(0, .004f, 0), new Vector3(.070f, .008f, .055f), wood);
            var silhouette = Child(stand, "Independent acrylic animal silhouette");
            silhouette.localPosition = new Vector3(0, .008f, .012f);
            silhouette.gameObject.AddComponent<MeshFilter>().sharedMesh = AnimalBoard(name, animal);
            var acrylicRenderer = silhouette.gameObject.AddComponent<MeshRenderer>();
            acrylicRenderer.sharedMaterial = acrylic;
            acrylicRenderer.shadowCastingMode = ShadowCastingMode.Off;
            acrylicRenderer.receiveShadows = false;
            // No opaque/full-plane hitbox: individual rail fittings and handles remain the input targets.
            for (int sign = -1; sign <= 1; sign += 2)
                Box(stand, "Slim polished edge", new Vector3(sign * .023f, .137f, .012f), new Vector3(.0015f, .260f, .0035f), edge, false);
            AnimalFace(stand, animal);
            for (int i = 0; i < 5; i++)
            {
                var shaftRoot = Child(stand, "Shaft_" + (i + 1));
                shaftRoot.localPosition = new Vector3(0, .035f + .050f * i, -.006f);
                Box(shaftRoot, "Fixed left socket", new Vector3(-.025f, 0, .009f), new Vector3(.006f, .009f, .025f), brass);
                Box(shaftRoot, "Releasable right socket", new Vector3(.025f, 0, .009f), new Vector3(.006f, .009f, .025f), brass);
                var rod = Child(shaftRoot, "RemovableRod");
                Cylinder(rod, "Horizontal support rod", Vector3.zero, .0022f, .059f, brass, Vector3.right, false);
                Box(rod, "Shaft pull handle", new Vector3(.034f, 0, 0), new Vector3(.009f, .012f, .012f), brass);
                var slot = Slot(rod, container, name + "-slot-" + (i + 1), PhysicalItemType.Tape, Vector3.zero);
                slot.transform.localRotation = Quaternion.Euler(0, 0, 90);
                slot.permanentReservation = false;
                slot.snapRadius = .025f;
                var shaft = shaftRoot.gameObject.AddComponent<WorkspaceShaft>();
                shaft.shaftId = name + "-shaft-" + (i + 1);
                shaft.rod = rod;
                shaft.closedLocalPosition = Vector3.zero;
                shaft.slideAxis = Vector3.right;
                shaft.travel = .045f;
                shaft.slot = slot;
                slot.accessShaft = shaft;
            }
            Label(stand, animal == 0 ? "BLACK CAT" : animal == 1 ? "PUPPY" : "BIRD", new Vector3(0, .009f, -.029f), .0018f);
            SaveFixture(stand.gameObject, name);
        }

        static void BuildHangingStickers()
        {
            var rack = Child(root, "HangingStickerRack");
            rack.position = new Vector3(-.475f, TableY, .278f);
            var container = rack.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = "悬挂贴纸架 · 全部可粘贴";
            container.canClose = false;
            Box(rack, "Walnut rack foot", new Vector3(0, .005f, 0), new Vector3(.342f, .010f, .070f), wood);
            for (int sign = -1; sign <= 1; sign += 2)
                Cylinder(rack, "Fine brass upright", new Vector3(sign * .166f, .210f, .018f), .0022f, .416f, brass, Vector3.up, true);
            int index = 0;
            foreach (var definition in DecorationCatalog.Load().assets.Where(d => d.category == DecorationCategory.Sticker))
            {
                int row = index / 5, col = index % 5;
                Vector3 position = new Vector3(-.128f + col * .064f, .205f + row * .054f, -.006f);
                if (col == 0) Cylinder(rack, "Hanging crossbar " + row, new Vector3(0, position.y + .030f, .002f), .0017f, .33f, brass, Vector3.right, false);
                var item = ExistingOrClone("physical-" + definition.assetId + "-001", definition.assetId, PhysicalItemType.Sticker);
                var slot = Slot(rack, container, "hang-" + definition.assetId, PhysicalItemType.Sticker, position);
                slot.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                BindHome(item, slot);
                Box(rack, "Individual brass clip", position + new Vector3(0, .025f, -.001f), new Vector3(.008f, .008f, .003f), brass, false);
                index++;
            }
            var duplicate = stock.Values.FirstOrDefault(i => i.itemInstanceId == "physical-sticker_01-002");
            if (duplicate)
            {
                var slot = Slot(rack, container, "hang-sticker-01-second", PhysicalItemType.Sticker, new Vector3(-.128f, .139f, -.006f));
                slot.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                BindHome(duplicate, slot);
                Cylinder(rack, "Spare clipping crossbar", new Vector3(-.128f, .169f, .002f), .0016f, .056f, brass, Vector3.right, false);
            }
            Label(rack, "STICKER ATELIER", new Vector3(0, .410f, -.003f), .0034f);
            SaveFixture(rack.gameObject, "HangingStickerRack");
        }

        static void BuildPaperTiers()
        {
            var rack = Child(root, "TieredPaperArchive");
            rack.position = new Vector3(-.080f, TableY, .170f);
            var container = rack.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = "分层内页档案 · 八种风格";
            container.canClose = false;
            Box(rack, "Archive base", new Vector3(0, .006f, 0), new Vector3(.331f, .012f, .332f), wood);
            Box(rack, "Archive rear", new Vector3(0, .143f, .163f), new Vector3(.331f, .286f, .006f), wood);
            string[] styles = { "复古花卉", "植物图鉴", "旧书信纸", "古地图", "天文星象", "复古建筑", "简约素色", "季节主题" };
            // Match the existing artwork seen in the source contact sheet while retaining both original sheet IDs.
            int[] paperIndexes = { 0, 2, 11, 9, 3, 1, 7, 16 };
            for (int i = 0; i < 8; i++)
            {
                var definition = JournalPaperCatalog.Load().papers[paperIndexes[i]];
                var item = ExistingOrClone("physical-" + definition.id + "-001", definition.id, PhysicalItemType.LoosePage);
                // The archive uses full A5 refills; fit an old A6 presentation through its visual child only.
                var targetSize = new Vector3(.148f, .0006f, .210f);
                Vector3 previousSize = item.dimensions.size;
                if (Vector3.Distance(previousSize, targetSize) > .00001f)
                {
                    item.dimensions = DimensionProfile(definition.id, targetSize);
                    item.interactionCollider.size = targetSize;
                }
                FitPaperArtwork(item, targetSize);
                var size = item.dimensions.size;
                int tier = i / 2;
                // Separate each angled sheet's world-Z footprint. A vertical pickup/return
                // corridor then remains clear of every other physical page collider.
                Vector3 position = new Vector3(i % 2 == 0 ? -.078f : .078f, .112f + tier * .037f, -.120f + tier * .080f);
                var slot = Slot(rack, container, "paper-" + definition.id, PhysicalItemType.LoosePage, position);
                slot.transform.localRotation = Quaternion.Euler(-70, 0, 0);
                BindHome(item, slot);
                Box(rack, "Independent paper tier support", position + new Vector3(0, -.098f, -.024f), new Vector3(.150f, .006f, .032f), darkWood);
                var binding = item.GetComponent<LoosePageBinding>() ?? item.gameObject.AddComponent<LoosePageBinding>();
                binding.paperId = definition.id;
                binding.style = styles[i];
                binding.pageSize = new Vector2(size.x, size.z);
                binding.bindingType = size.x < .12f ? "SixRing_A6" : "SixRing_A5";
                binding.holeAnchors = new Transform[6];
                for (int hole = 0; hole < 6; hole++)
                {
                    var anchor = item.transform.Find("BindingHole_" + hole) ?? Child(item.transform, "BindingHole_" + hole);
                    anchor.localPosition = new Vector3(-size.x * .5f + .006f, 0, (hole - 2.5f) * size.z * .12f);
                    binding.holeAnchors[hole] = anchor;
                }
                SaveItem(item);
            }
            Label(rack, "PAPER ARCHIVE  /  8 STYLES", new Vector3(0, .020f, -.169f), .0028f);
            SaveFixture(rack.gameObject, "TieredPaperArchive");
        }

        static void BuildNoteBoxes()
        {
            string[] labels = { "复古书信", "自然花园", "简约日常" };
            int[] first = { 1, 4, 7 }, count = { 3, 3, 2 };
            for (int boxIndex = 0; boxIndex < 3; boxIndex++)
            {
                var box = Child(root, "SmallNoteBox_" + (boxIndex + 1));
                box.position = new Vector3(.475f, TableY, .105f - boxIndex * .118f);
                var container = box.gameObject.AddComponent<PhysicalContainer>();
                container.displayName = "便签 · " + labels[boxIndex];
                container.canClose = false;
                Box(box, "Small walnut box floor", new Vector3(0, .003f, 0), new Vector3(.102f, .006f, .102f), wood);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Box(box, "Box side", new Vector3(sign * .049f, .012f, 0), new Vector3(.004f, .024f, .102f), wood);
                    Box(box, "Box front/back", new Vector3(0, .012f, sign * .049f), new Vector3(.102f, .024f, .004f), wood);
                }
                Box(box, "Stationary continuous note pad", new Vector3(0, .010f, 0), new Vector3(.075f, .008f, .075f), paper, false);
                for (int j = 0; j < count[boxIndex]; j++)
                {
                    string id = "note_" + (first[boxIndex] + j).ToString("D2");
                    var item = ExistingOrClone("physical-" + id + "-001", id, PhysicalItemType.Note);
                    var slot = Slot(box, container, id + "-home", PhysicalItemType.Note, new Vector3((j - 1) * .004f, .0143f + j * .0006f, (j - 1) * .004f));
                    BindHome(item, slot);
                }
                // Independent sticker-style exterior label uses an existing note pattern.
                var label = Child(box, "Style sticker label");
                label.localPosition = new Vector3(0, .012f, -.052f);
                label.localRotation = Quaternion.Euler(90, 0, 0);
                var texture = DecorationCatalog.Load().Find("note_" + first[boxIndex].ToString("D2")).texture;
                Surface(label, "StyleArtwork", .070f, .016f, MakeMaterial("NoteStyleLabel_" + boxIndex, Color.white, texture));
                Label(box, boxIndex == 0 ? "LETTERS" : boxIndex == 1 ? "GARDEN" : "DAILY", new Vector3(0, .028f, -.053f), .0020f);
                SaveFixture(box.gameObject, "SmallNoteBox_" + (boxIndex + 1));
            }
        }

        static void BuildTravelersBookcase()
        {
            var shelf = Child(root, "TravelersBookcase");
            shelf.position = new Vector3(.365f, TableY, .262f);
            var container = shelf.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = "TN 旅行者手账书架";
            container.canClose = false;
            Box(shelf, "Thick bottom shelf", new Vector3(0, .006f, 0), new Vector3(.450f, .012f, .110f), wood);
            Box(shelf, "Walnut back", new Vector3(0, .126f, .054f), new Vector3(.450f, .252f, .006f), darkWood);
            for (int sign = -1; sign <= 1; sign += 2)
                Box(shelf, "Bookcase side panel", new Vector3(sign * .221f, .128f, 0), new Vector3(.008f, .256f, .110f), wood);
            // A shallow rear cornice preserves the bookcase silhouette without covering the lift column.
            Box(shelf, "Rear cornice shelf", new Vector3(0, .255f, .052f), new Vector3(.466f, .010f, .025f), wood);
            float[] xs = { -.159f, -.044f, .061f, .156f };
            for (int i = 0; i < 4; i++)
            {
                string id = "tn_" + (i + 1).ToString("D2");
                var item = ExistingOrClone("physical-" + id + "-001", id, PhysicalItemType.TravelersNotebook);
                var slot = Slot(shelf, container, "home-" + id, PhysicalItemType.TravelersNotebook, new Vector3(xs[i], .012f + item.dimensions.size.y * .5f, -.008f));
                BindHome(item, slot);
            }
            Label(shelf, "TRAVELER'S LIBRARY", new Vector3(0, .016f, -.058f), .003f);
            SaveFixture(shelf.gameObject, "TravelersBookcase");
        }

        static PhysicalInteractable CreateReleaseBook(string id, int index)
        {
            string instance = "physical-" + id + "-001";
            if (stock.TryGetValue(instance, out var existing)) return existing;
            var item = NewItem(id, instance, index == 0 ? "离型本 A · 花园档案" : "离型本 B · 书信收藏", PhysicalItemType.ReleaseBook, new Vector3(.115f, .012f, .158f));
            item.dimensions.pageSize = new Vector2(.105f, .148f);
            var coverMaterial = MakeMaterial(id + "_linen", index == 0 ? new Color(.32f, .45f, .36f) : new Color(.62f, .36f, .32f), AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Linen.png"));
            Box(item.visualRoot, "Back cover", new Vector3(0, -.005f, 0), new Vector3(.115f, .002f, .158f), coverMaterial, false);
            Box(item.visualRoot, "Closed wax paper block", Vector3.zero, new Vector3(.108f, .008f, .151f), paper, false);
            var hinge = Child(item.visualRoot, "CoverHinge");
            hinge.localPosition = new Vector3(-.0575f, .005f, 0);
            var cover = Box(hinge, "Front cover", new Vector3(.0575f, 0, 0), new Vector3(.115f, .002f, .158f), coverMaterial, false).transform;
            Box(cover, "Brass cover label", new Vector3(0, .0015f / .002f, 0), new Vector3(.052f / .115f, .001f / .002f, .023f / .158f), brass, false);
            var spread = Child(item.visualRoot, "OpenSpread");
            // The closed root is the cover center; the opened spread is centered on the left-edge spine.
            spread.localPosition = new Vector3(-.0575f, 0, 0);
            var anchors = new Transform[2];
            var pages = new MeshRenderer[2];
            for (int side = 0; side < 2; side++)
            {
                var page = Child(spread, side == 0 ? "Left release page" : "Right release page");
                page.localPosition = new Vector3((side == 0 ? -1 : 1) * .054f, .0065f, 0);
                pages[side] = Surface(page, id + "_page_" + side, .105f, .148f, paper);
                var anchor = Child(spread, side == 0 ? "LeftPageCoordinates" : "RightPageCoordinates");
                anchor.localPosition = page.localPosition + new Vector3(-.0525f, .0001f, -.074f);
                anchor.localRotation = Quaternion.Euler(90, 0, 0);
                anchors[side] = anchor;
            }
            spread.gameObject.SetActive(false);
            var view = item.gameObject.AddComponent<ReleaseBookView>();
            view.bookId = index == 0 ? "release-a" : "release-b";
            view.physical = item;
            view.cover = cover;
            view.hinge = hinge;
            view.spreadRoot = spread;
            view.pageAnchors = anchors;
            view.pageSize = new Vector2(.105f, .148f);
            view.pages = pages;
            SaveItem(item);
            return item;
        }

        static PhysicalInteractable ExistingOrClone(string instanceId, string assetId, PhysicalItemType type)
        {
            if (stock.TryGetValue(instanceId, out var existing)) return existing;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PhysicalWorkspace/Prefabs/" + assetId + ".prefab");
            PhysicalInteractable item;
            if (prefab)
            {
                var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                clone.transform.SetParent(root, true);
                item = clone.GetComponent<PhysicalInteractable>();
                item.itemInstanceId = instanceId;
                item.homeSlot = null;
                item.homeAnchor = null;
                item.inspectAnchor = inspect;
                stock.Add(instanceId, item);
            }
            else
            {
                bool page = type == PhysicalItemType.LoosePage;
                Vector3 size = page ? new Vector3(.148f, .0006f, .210f) : type == PhysicalItemType.Note ? new Vector3(.075f, .0006f, .075f) : new Vector3(.050f, .0006f, .050f);
                var definition = page ? null : DecorationCatalog.Load().Find(assetId);
                var texture = page ? JournalPaperCatalog.Load().Find(assetId).texture : definition.texture;
                item = NewItem(assetId, instanceId, page ? "内页 · " + assetId : definition.displayName, type, size);
                if (type != PhysicalItemType.Sticker) Box(item.visualRoot, "Paper backing", Vector3.zero, size, paper, false);
                var artwork = Child(item.visualRoot, "Replaceable artwork");
                artwork.localPosition = Vector3.up * (size.y * .5f + .00001f);
                Surface(artwork, assetId + "_art", size.x, size.z, MakeMaterial(assetId + "_art", Color.white, texture, false, true));
                SaveItem(item);
            }
            return item;
        }

        static PhysicalInteractable NewItem(string assetId, string instanceId, string label, PhysicalItemType type, Vector3 size)
        {
            var go = new GameObject(assetId);
            go.transform.SetParent(root, false);
            var item = go.AddComponent<PhysicalInteractable>();
            item.itemInstanceId = instanceId; item.assetId = assetId; item.displayName = label; item.itemType = type;
            item.dimensions = DimensionProfile(assetId, size);
            item.interactionCollider = go.AddComponent<BoxCollider>(); item.interactionCollider.size = size;
            item.visualRoot = Child(go.transform, "Visual");
            item.grabAnchor = Child(go.transform, "GrabAnchor");
            item.placementAnchor = Child(go.transform, "PlacementAnchor");
            item.placementAnchor.localPosition = Vector3.down * size.y * .5f;
            item.inspectAnchor = inspect;
            stock.Add(instanceId, item);
            return item;
        }

        static PhysicalDimensionProfile DimensionProfile(string assetId, Vector3 size)
        {
            string path = Folder + "/Dimensions/" + assetId + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<PhysicalDimensionProfile>(path);
            if (!profile) { profile = ScriptableObject.CreateInstance<PhysicalDimensionProfile>(); AssetDatabase.CreateAsset(profile, path); }
            profile.size = size; profile.colliderSize = size; profile.minDistance = .05f; profile.maxDistance = 3;
            profile.pageSize = new Vector2(size.x, size.z);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static PhysicalStorageSlot Slot(Transform parent, PhysicalContainer container, string name, PhysicalItemType type, Vector3 position)
        {
            var transform = Child(parent, name); transform.localPosition = position;
            var slot = transform.gameObject.AddComponent<PhysicalStorageSlot>();
            slot.slotId = "stage4b-" + name;
            slot.container = container; slot.acceptedType = type; slot.snapRadius = .033f;
            slot.permanentReservation = true;
            slots.Add(slot); return slot;
        }

        static void BindHome(PhysicalInteractable item, PhysicalStorageSlot slot)
        {
            item.ConfigureHome(slot);
            item.inspectAnchor = inspect;
            item.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
            item.transform.localScale = Vector3.one;
        }

        static void ConfigureSeatedCamera()
        {
            var rig = Object.FindFirstObjectByType<DeskCamera>();
            if (!rig) return;
            const string path = Folder + "/DeskCameraProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<DeskCameraProfile>(path);
            if (!profile)
            {
                profile = rig.profile ? Object.Instantiate(rig.profile) : ScriptableObject.CreateInstance<DeskCameraProfile>();
                profile.name = "Stage4B Fixed Desk Camera";
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.seatedPosition = new Vector3(0, 1.52f, -.96f);
            profile.seatedTarget = new Vector3(0, .79f, .025f);
            profile.seatedFieldOfView = 58;
            // Derive from the retained original profile so repeated installation cannot accumulate offsets.
            var original = AssetDatabase.LoadAssetAtPath<DeskCameraProfile>("Assets/Data/Physical/DeskCameraProfile.asset");
            profile.journalPosition = (original ? original.journalPosition : new Vector3(0, 1.14f, -.34f)) + Vector3.back * .05f;
            profile.journalTarget = (original ? original.journalTarget : new Vector3(0, .77f, -.06f)) + Vector3.back * .05f;
            rig.profile = profile;
            rig.transform.position = profile.seatedPosition;
            rig.transform.LookAt(profile.seatedTarget);
            rig.GetComponent<Camera>().fieldOfView = profile.seatedFieldOfView;
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(rig);
        }

        static void SaveItem(PhysicalInteractable item)
        {
            string path = Folder + "/Prefabs/" + item.assetId + ".prefab";
            // Do not save references to scene-only home/inspection anchors in a reusable entity asset.
            var oldInspect = item.inspectAnchor;
            var oldHome = item.homeAnchor;
            var oldSlot = item.homeSlot;
            var serialized = new SerializedObject(item);
            var currentSlot = serialized.FindProperty("currentSlot");
            Object oldCurrentSlot = currentSlot != null ? currentSlot.objectReferenceValue : null;
            item.inspectAnchor = null;
            item.homeAnchor = null;
            item.homeSlot = null;
            serialized.Update();
            if (currentSlot != null) { currentSlot.objectReferenceValue = null; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            PrefabUtility.SaveAsPrefabAsset(item.gameObject, path);
            item.inspectAnchor = oldInspect;
            item.homeAnchor = oldHome;
            item.homeSlot = oldSlot;
            serialized.Update();
            if (currentSlot != null) { currentSlot.objectReferenceValue = oldCurrentSlot; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            prefabPaths.Add(path);
        }

        static void SaveFixture(GameObject fixture, string name)
        {
            string path = Folder + "/Prefabs/" + name + ".prefab";
            Vector3 position = fixture.transform.position;
            fixture.transform.position = Vector3.zero;
            PrefabUtility.SaveAsPrefabAsset(fixture, path);
            fixture.transform.position = position;
            prefabPaths.Add(path);
        }

        static void WriteManifest()
        {
            var manifest = new Manifest { drawers = 3, shafts = 15,
                prefabs = Directory.GetFiles(Folder + "/Prefabs", "*.prefab").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray() };
            foreach (var item in stock.Values.OrderBy(i => i.itemInstanceId))
            {
                switch (item.itemType)
                {
                    case PhysicalItemType.Tape: manifest.tapeRolls++; break;
                    case PhysicalItemType.Sticker: manifest.stickers++; break;
                    case PhysicalItemType.Note: manifest.notes++; break;
                    case PhysicalItemType.LoosePage: manifest.loosePages++; break;
                    case PhysicalItemType.TravelersNotebook: manifest.travelersNotebooks++; break;
                    case PhysicalItemType.ReleaseBook: manifest.releaseBooks++; break;
                }
                manifest.items.Add(new Record { instanceId = item.itemInstanceId, assetId = item.assetId, type = item.itemType.ToString(),
                    home = item.homeSlot ? item.homeSlot.slotId : "", dimensions = item.dimensions.size,
                    prefab = File.Exists(Folder + "/Prefabs/" + item.assetId + ".prefab") ? Folder + "/Prefabs/" + item.assetId + ".prefab" : "Assets/PhysicalWorkspace/Prefabs/" + item.assetId + ".prefab" });
            }
            File.WriteAllText("docs/stage4b-logs/scene-assets.json", JsonUtility.ToJson(manifest, true));
        }

        static void FitPaperArtwork(PhysicalInteractable item, Vector3 target)
        {
            Bounds bounds = default; bool any = false;
            foreach (var filter in item.visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var meshBounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var signs = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    Vector3 point = item.transform.InverseTransformPoint(filter.transform.TransformPoint(meshBounds.center + Vector3.Scale(meshBounds.extents, signs)));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; } else bounds.Encapsulate(point);
                }
            }
            if (any && bounds.size.x > 0 && bounds.size.z > 0)
                item.visualRoot.localScale = Vector3.Scale(item.visualRoot.localScale, new Vector3(target.x / bounds.size.x, 1, target.z / bounds.size.z));
        }

        static Mesh AnimalBoard(string name, int animal)
        {
            string path = Folder + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (existing) return existing;
            var polygon = new List<Vector2> { new Vector2(-.019f, 0), new Vector2(.019f, 0), new Vector2(.024f, .265f) };
            if (animal == 0) polygon.AddRange(new[] { new Vector2(.030f, .283f), new Vector2(.026f, .320f), new Vector2(.009f, .306f), new Vector2(-.009f, .306f), new Vector2(-.026f, .320f), new Vector2(-.030f, .283f) });
            else if (animal == 1) polygon.AddRange(new[] { new Vector2(.036f, .278f), new Vector2(.031f, .304f), new Vector2(.016f, .319f), new Vector2(-.016f, .319f), new Vector2(-.031f, .304f), new Vector2(-.036f, .278f) });
            else polygon.AddRange(new[] { new Vector2(.027f, .281f), new Vector2(.039f, .294f), new Vector2(.022f, .299f), new Vector2(.012f, .318f), new Vector2(-.009f, .322f), new Vector2(-.026f, .307f), new Vector2(-.029f, .282f) });
            polygon.Add(new Vector2(-.024f, .265f));
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            int n = polygon.Count;
            for (int side = 0; side < 2; side++) foreach (var p in polygon)
            { vertices.Add(new Vector3(p.x, p.y, side == 0 ? -.0015f : .0015f)); uv.Add(new Vector2(p.x / .08f + .5f, p.y / .33f)); }
            // Ear-clipping supports the concave ears and beak of the actual extruded silhouette.
            var indices = Enumerable.Range(0, n).ToList();
            int guard = n * n;
            while (indices.Count > 2 && guard-- > 0)
            {
                bool clipped = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int a = indices[(j + indices.Count - 1) % indices.Count], b = indices[j], c = indices[(j + 1) % indices.Count];
                    if (Cross(polygon[b] - polygon[a], polygon[c] - polygon[b]) <= 0) continue;
                    bool occupied = indices.Any(k => k != a && k != b && k != c && InTriangle(polygon[k], polygon[a], polygon[b], polygon[c]));
                    if (occupied) continue;
                    triangles.AddRange(new[] { a, c, b, a + n, b + n, c + n });
                    indices.RemoveAt(j); clipped = true; break;
                }
                if (!clipped) throw new InvalidOperationException("Animal profile triangulation failed: " + name);
            }
            for (int j = 0; j < n; j++) { int k = (j + 1) % n; triangles.AddRange(new[] { j, k, j + n, k, k + n, j + n }); }
            var mesh = new Mesh { name = name + " 3mm acrylic silhouette" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;

        static void AnimalFace(Transform parent, int animal)
        {
            Material accent = animal == 0 ? ink : MakeMaterial(animal == 1 ? "PuppyCaramel" : "BirdSage", animal == 1 ? new Color(.48f, .29f, .16f) : new Color(.30f, .43f, .37f), null);
            var face = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            face.name = animal == 0 ? "Black cat face" : animal == 1 ? "Puppy face" : "Bird face";
            face.transform.SetParent(parent, false); face.transform.localPosition = new Vector3(0, .303f, .008f);
            face.transform.localScale = new Vector3(.044f, .038f, .003f);
            face.GetComponent<Renderer>().sharedMaterial = accent; Object.DestroyImmediate(face.GetComponent<Collider>());
            if (animal == 0)
                for (int sign = -1; sign <= 1; sign += 2)
                    TriangleDecoration(parent, "CatEar_" + sign,
                        new Vector2(sign * .007f, .314f), new Vector2(sign * .026f, .328f), new Vector2(sign * .025f, .300f), ink);
            if (animal == 1)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var ear = GameObject.CreatePrimitive(PrimitiveType.Sphere); ear.name = "Soft puppy ear"; ear.transform.SetParent(parent, false);
                    ear.transform.localPosition = new Vector3(sign * .022f, .301f, .007f); ear.transform.localScale = new Vector3(.014f, .033f, .004f);
                    ear.GetComponent<Renderer>().sharedMaterial = accent; Object.DestroyImmediate(ear.GetComponent<Collider>());
                }
            for (int sign = -1; sign <= 1; sign += 2)
                Box(parent, "Gold animal eye", new Vector3(sign * .009f, .306f, .005f), new Vector3(.003f, .002f, .001f), brass, false);
            if (animal == 1) Box(parent, "Puppy nose", new Vector3(0, .296f, .004f), new Vector3(.005f, .003f, .001f), ink, false);
            if (animal == 2) TriangleDecoration(parent, "BirdBeak", new Vector2(.017f, .307f), new Vector2(.035f, .301f), new Vector2(.017f, .296f), brass);
        }

        static void TriangleDecoration(Transform parent, string name, Vector2 a, Vector2 b, Vector2 c, Material material)
        {
            string path = Folder + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = new Mesh { name = name + " 1mm inlay" };
                mesh.vertices = new[] { new Vector3(a.x, a.y, .0055f), new Vector3(b.x, b.y, .0055f), new Vector3(c.x, c.y, .0055f),
                    new Vector3(a.x, a.y, .0065f), new Vector3(b.x, b.y, .0065f), new Vector3(c.x, c.y, .0065f) };
                mesh.triangles = new[] { 0, 2, 1, 3, 4, 5, 0, 1, 3, 1, 4, 3, 1, 2, 4, 2, 5, 4, 2, 0, 5, 0, 3, 5 };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            }
            var art = Child(parent, name); art.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            art.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material CopyMaterial(string name, string source)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var value = AssetDatabase.LoadAssetAtPath<Material>(path); if (value) return value;
            var original = AssetDatabase.LoadAssetAtPath<Material>(source);
            value = original ? new Material(original) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(value, path); return value;
        }

        static Material MakeMaterial(string name, Color color, Texture2D texture, bool transparent = false, bool cutout = false)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var value = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!value) { value = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(value, path); }
            value.SetColor("_BaseColor", color); value.SetTexture("_BaseMap", texture); value.SetFloat("_Smoothness", transparent ? .82f : .18f);
            value.SetFloat("_Cull", 0);
            if (transparent)
            {
                value.SetFloat("_Surface", 1); value.SetFloat("_ZWrite", 0); value.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); value.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                value.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); value.SetOverrideTag("RenderType", "Transparent"); value.renderQueue = 3000;
            }
            if (cutout) { value.SetFloat("_AlphaClip", 1); value.SetFloat("_Cutoff", .1f); value.EnableKeyword("_ALPHATEST_ON"); }
            EditorUtility.SetDirty(value); return value;
        }

        static Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            if (material && material.HasProperty("_Surface") && material.GetFloat("_Surface") > .5f)
            { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static void Cylinder(Transform parent, string name, Vector3 position, float radius, float length, Material material, Vector3 axis, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis); go.transform.localScale = new Vector3(radius * 2, length * .5f, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = material; if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static MeshRenderer Surface(Transform parent, string name, float width, float depth, Material material)
        {
            var art = Child(parent, name); string path = Folder + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = new Mesh { name = name };
                mesh.vertices = new[] { new Vector3(-width / 2, 0, -depth / 2), new Vector3(-width / 2, 0, depth / 2), new Vector3(width / 2, 0, depth / 2), new Vector3(width / 2, 0, -depth / 2) };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }; mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            }
            art.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = art.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; return renderer;
        }
        static void Label(Transform parent, string text, Vector3 position, float size)
        {
            var t = Child(parent, "Label"); t.localPosition = position; var label = t.gameObject.AddComponent<TextMesh>();
            label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.fontSize = 64; label.characterSize = size; label.color = new Color(.93f, .80f, .57f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.font = font; t.GetComponent<Renderer>().sharedMaterial = font.material;
        }
        static void MoveLegacyProp(string name, Vector3 position) { var go = GameObject.Find(name); if (go) go.transform.position = position; }
    }
}
