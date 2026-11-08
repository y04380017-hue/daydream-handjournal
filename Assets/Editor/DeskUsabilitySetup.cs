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
    /// <summary>Incremental, repeat-safe authoring. Original assets and stable item/home/shaft IDs are retained.</summary>
    public static class DeskUsabilitySetup
    {
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        const string Folder = "Assets/Daydream/DeskUsability/";
        static Transform root, archive, stock;
        static DeskLayoutBindings bindings;
        static Material wood, cream, mint, pink, blue, lilac, gold, ink;
        static readonly List<DeskLayoutPlacement> placements = new List<DeskLayoutPlacement>();

        [MenuItem("Vintage Journal/Daydream/Apply desk usability and two-drawer revision")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var previous = Object.FindFirstObjectByType<DeskLayoutBindings>();
            if (previous)
            {
                CalibrateCurrent(previous);
                ValidateBindings(previous);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                Debug.Log("[DeskUsability] Existing layout calibrated; stable instances retained.");
                return;
            }
            foreach (var path in new[] { Folder, Folder + "Materials", Folder + "Dimensions" }) Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            // A scene instance may be unpacked, but its source prefab and all historical art stay untouched.
            foreach (var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None)) Unpack(item.gameObject);
            foreach (var container in Object.FindObjectsByType<PhysicalContainer>(FindObjectsSortMode.None)) Unpack(container.gameObject);
            var originals = Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None);
            var originalIds = originals.Select(i => i.itemInstanceId).ToArray();
            if (originalIds.Distinct().Count() != originalIds.Length) throw new InvalidOperationException("Duplicate original instances; refusing authoring.");
            wood = Mat("Pale maple", "EAD5B5"); cream = Mat("Cotton cream", "FFF9EF"); mint = Mat("Seafoam", "B9E4D4");
            pink = Mat("Peach", "F6C3C9"); blue = Mat("Sky", "B9DDEB"); lilac = Mat("Lilac", "D2C6EF");
            gold = Mat("Satin fittings", "E9C87C"); gold.SetFloat("_Metallic", .45f); ink = Mat("Ink", "536583");
            root = new GameObject("Desk usability revision 20261107").transform;
            archive = Child(root, "Retained previous fixture presentation", Vector3.zero);
            stock = Object.FindFirstObjectByType<WorkspaceGameplayController>().transform;
            bindings = stock.gameObject.AddComponent<DeskLayoutBindings>(); placements.Clear();
            // Retain the source apron, replacing its solid front with two actual drawer openings.
            var desk = GameObject.Find("Desk");
            var apron = desk ? desk.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Front apron") : null;
            if (apron) { Unpack(apron.gameObject); apron.gameObject.SetActive(false); }
            bindings.leftDrawer = Drawer("desk-drawer-left", "桌下左抽屉 · 胶带与内页", -.281f);
            bindings.rightDrawer = Drawer("desk-drawer-right", "桌下右抽屉 · 便签贴纸与笔", .281f);
            TapeStock(originals); PaperArchive(originals); StickerBoxes(originals); Notes(originals); Notebooks(originals); Tools();
            // Every entity participates in registration, including the two release books that keep their previous homes.
            foreach (var item in Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None))
                if (!placements.Any(p => p.item == item)) AddPlacement(item, item.homeSlot, item.CurrentSlot ? item.CurrentSlot : item.homeSlot);
            bindings.placements = placements.ToArray();
            archive.gameObject.SetActive(false);
            CalibrateCurrent(bindings);
            foreach (var container in Object.FindObjectsByType<PhysicalContainer>(FindObjectsSortMode.None)) container.Refresh();
            ValidateBindings(bindings);
            if (originalIds.Any(id => !bindings.placements.Any(p => p.item.itemInstanceId == id))) throw new InvalidOperationException("An original entity was lost.");
            foreach (var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c && PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(bindings);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory("docs/desk-usability");
            File.WriteAllLines("docs/desk-usability/layout-authoring.txt", bindings.placements.Select(p =>
                p.item.itemInstanceId + " | " + p.item.assetId + " | home=" + (p.homeSlot ? p.homeSlot.slotId : "desktop") +
                " | display=" + (p.desiredDisplaySlot ? p.desiredDisplaySlot.slotId : "desktop")));
            Debug.Log("[DeskUsability] Authored two below-desk drawers, 15 filled shafts, 7 spine-out books and " + bindings.placements.Length + " exclusive instances.");
        }

        static WorkspaceDrawer Drawer(string id, string label, float x)
        {
            var chassis = Child(root, id, new Vector3(x, .565f, -.12f));
            var container = chassis.gameObject.AddComponent<PhysicalContainer>();
            container.displayName = label; container.canClose = true; container.isOpen = false; container.contentsVisibleWhenClosed = false;
            // A real fixed carcass and a single limited sliding tray, below the .76m desk surface.
            Box(chassis, "Fixed upper rail", new Vector3(0, .138f, 0), new Vector3(.540f, .012f, .466f), wood, true);
            for (int s = -1; s <= 1; s += 2) Box(chassis, "Fixed side guide", new Vector3(s * .266f, .064f, 0), new Vector3(.008f, .132f, .466f), wood, true);
            var slide = Child(chassis, "Bounded sliding tray", Vector3.zero);
            Box(slide, "Solid drawer floor", Vector3.zero, new Vector3(.520f, .006f, .45f), wood, true);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(slide, "Drawer side", new Vector3(s * .257f, .059f, 0), new Vector3(.006f, .118f, .45f), wood, true);
                Box(slide, s < 0 ? "Drawer front" : "Drawer back", new Vector3(0, .059f, s * .222f), new Vector3(.520f, .118f, .006f), wood, true);
            }
            Box(slide, "Satin pull handle", new Vector3(0, .069f, -.241f), new Vector3(.128f, .015f, .025f), gold, true);
            Label(slide, x < 0 ? "WASHI  /  REFILLS" : "NOTES  /  STICKERS  /  PENS", new Vector3(0, .030f, -.226f), .0022f);
            var d = chassis.gameObject.AddComponent<WorkspaceDrawer>(); d.drawerId = id; d.slide = slide; d.container = container;
            d.slideAxis = Vector3.back; d.travel = .46f; d.closedLocalPosition = Vector3.zero; d.SetOpenAmount(0);
            return d;
        }

        static void TapeStock(PhysicalInteractable[] originals)
        {
            var tapes = originals.Where(i => i.itemType == PhysicalItemType.Tape).OrderBy(i => i.itemInstanceId).ToArray();
            var shafts = Object.FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None).OrderBy(s => s.shaftId).ToArray();
            if (tapes.Length != 15 || shafts.Length != 15) throw new InvalidOperationException("Expected the retained 15 original rolls and shafts.");
            var box = OpenBox(bindings.leftDrawer.slide, "Tape archive box", "胶带盒 · 丰富花色", new Vector3(.163f, .005f, 0), new Vector2(.164f, .433f), .057f, mint);
            for (int i = 0; i < tapes.Length; i++)
            {
                var item = tapes[i]; var home = item.homeSlot;
                MoveSlot(home, box, new Vector3((i % 5 - 2) * .028f, .029f, -.155f + i / 5 * .050f), Quaternion.Euler(0, 0, 90));
                // The old roll's exclusive long-term home remains a drawer slot; the shaft is only its work position.
                item.ConfigureHome(shafts[i].slot); item.homeSlot = home; item.homeAnchor = home.transform;
                home.TryReserve(item); AddPlacement(item, home, shafts[i].slot);
                var copy = Clone(item, "physical-" + item.assetId + "-usability-archive-" + (i + 1).ToString("D2"));
                var extra = Slot(box.transform, box, "usability-home-" + copy.itemInstanceId, PhysicalItemType.Tape,
                    new Vector3((i % 5 - 2) * .028f, .029f, .025f + i / 5 * .050f), Quaternion.Euler(0, 0, 90));
                Bind(copy, extra);
            }
        }

        static void PaperArchive(PhysicalInteractable[] originals)
        {
            var pages = originals.Where(i => i.itemType == PhysicalItemType.LoosePage).OrderBy(i => i.assetId).ToArray();
            var box = OpenBox(bindings.leftDrawer.slide, "Refill archive box", "内页盒 · 全部八种内页", new Vector3(-.100f, .005f, 0), new Vector2(.309f, .433f), .050f, cream);
            for (int i = 0; i < pages.Length; i++)
            {
                int cell = i % 4, layer = i / 4;
                MoveSlot(pages[i].homeSlot, box, new Vector3((cell % 2 - .5f) * .155f + layer * .006f, .012f + layer * .0013f, (cell / 2 - .5f) * .210f + layer * .005f), Quaternion.identity);
                Bind(pages[i], pages[i].homeSlot);
            }
            RetainFixture("TieredPaperArchive");
        }

        static void StickerBoxes(PhysicalInteractable[] originals)
        {
            var sourceBoxes = Object.FindObjectsByType<PhysicalContainer>(FindObjectsSortMode.None)
                .Where(c => c.transform.name.StartsWith("SmallNoteBox_", StringComparison.Ordinal)).OrderBy(c => c.transform.name).ToArray();
            for (int i = 0; i < sourceBoxes.Length; i++)
            {
                var box = sourceBoxes[i]; box.transform.SetParent(bindings.rightDrawer.slide, false);
                box.transform.localPosition = new Vector3(-.185f + i * .130f, .008f, .110f); box.transform.localRotation = Quaternion.identity;
                box.isOpen = true; box.canClose = false;
                foreach (var item in originals.Where(item => item.itemType == PhysicalItemType.Sticker && item.homeSlot && item.homeSlot.transform.IsChildOf(box.transform)))
                { item.ConfigureHome(item.homeSlot); AddPlacement(item, item.homeSlot, item.homeSlot); }
            }
        }

        static void Notes(PhysicalInteractable[] originals)
        {
            var notes = originals.Where(i => i.itemType == PhysicalItemType.Note).OrderBy(i => i.assetId).ToArray();
            var box = OpenBox(root, "Deep desktop note organizer", "桌面便签盒 · 密集分格", new Vector3(-.080f, .760f, .170f), new Vector2(.326f, .174f), .062f, wood);
            bindings.desktopNotes = box;
            for (int n = 0; n < 24; n++)
            {
                var item = n < notes.Length ? notes[n] : Clone(notes[n % notes.Length], "physical-" + notes[n % notes.Length].assetId + "-usability-desk-" + (n + 1).ToString("D2"));
                int cell = n % 8, layer = n / 8;
                var slot = n < notes.Length ? item.homeSlot : Slot(box.transform, box, "usability-home-" + item.itemInstanceId, PhysicalItemType.Note, Vector3.zero);
                MoveSlot(slot, box, new Vector3((cell % 4 - 1.5f) * .078f + layer * .0005f, .052f + layer * .001f, (cell / 4 - .5f) * .078f + layer * .0005f), Quaternion.identity);
                Bind(item, slot);
                if (layer == 0)
                {
                    // The pad's noninteractive body is presentation; every removable top sheet is a distinct instance.
                    Box(box.transform, "Bound note pad body " + cell, new Vector3((cell % 4 - 1.5f) * .078f, .027f, (cell / 4 - .5f) * .078f), new Vector3(.075f, .049f, .075f), cream, false);
                    for (int line = 0; line < 10; line++) Box(box.transform, "Visible paper edges", new Vector3((cell % 4 - 1.5f) * .078f, .008f + line * .004f, (cell / 4 - .5f) * .078f - .0376f), new Vector3(.074f, .00035f, .0003f), blue, false);
                }
            }
            for (int b = 0; b < 3; b++)
            {
                var drawerBox = OpenBox(bindings.rightDrawer.slide, "Boxed note refills " + b, "便签补充盒 " + (b + 1), new Vector3(-.190f + b * .115f, .008f, -.110f), new Vector2(.102f, .125f), .052f, new[] { pink, mint, lilac }[b]);
                Box(drawerBox.transform, "Dense pad body", new Vector3(0, .017f, 0), new Vector3(.075f, .027f, .075f), cream, false);
                for (int n = 0; n < 6; n++)
                {
                    var sample = notes[(b * 3 + n) % notes.Length];
                    var copy = Clone(sample, "physical-" + sample.assetId + "-usability-refill-" + (b * 6 + n + 1).ToString("D2"));
                    Bind(copy, Slot(drawerBox.transform, drawerBox, "usability-home-" + copy.itemInstanceId, PhysicalItemType.Note, new Vector3(n * .0005f, .032f + n * .0008f, n * .0005f)));
                }
            }
            RetainFixture("Cloth note pocket organizer");
        }

        static void Notebooks(PhysicalInteractable[] originals)
        {
            var books = originals.Where(i => i.itemType == PhysicalItemType.TravelersNotebook).OrderBy(i => i.assetId).ToList();
            if (books.Count != 4) throw new InvalidOperationException("Expected four retained TN books.");
            for (int i = 0; i < 3; i++)
            {
                var item = Clone(books[2 + i % 2], "physical-memo_book_" + (i + 1).ToString("D2") + "-001");
                item.assetId = "memo_book_" + (i + 1).ToString("D2"); item.displayName = new[] { "花园便签本", "天空便签本", "心情便签本" }[i];
                foreach (var r in item.visualRoot.GetComponentsInChildren<Renderer>())
                    if (r.name.Contains("cover", StringComparison.OrdinalIgnoreCase) || r.name.Contains("spine", StringComparison.OrdinalIgnoreCase)) r.sharedMaterial = new[] { pink, blue, lilac }[i];
                books.Add(item);
            }
            var shelf = Child(root, "Seven spine-out notebook library", new Vector3(.43f, .76f, .215f));
            var c = shelf.gameObject.AddComponent<PhysicalContainer>(); c.displayName = "侧插书架 · 四本TN与三本便签本"; c.canClose = false; c.isOpen = true;
            Box(shelf, "Bookcase floor", new Vector3(0, .006f, 0), new Vector3(.290f, .012f, .150f), wood, true);
            Box(shelf, "Low rear rail", new Vector3(0, .078f, .073f), new Vector3(.290f, .145f, .006f), wood, true);
            for (int s = -1; s <= 1; s += 2) Box(shelf, "Bookend", new Vector3(s * .143f, .125f, 0), new Vector3(.006f, .25f, .15f), wood, true);
            for (int i = 0; i < books.Count; i++)
            {
                var item = books[i]; var slot = i < 4 ? item.homeSlot : Slot(shelf, c, "usability-home-" + item.itemInstanceId, PhysicalItemType.TravelersNotebook, Vector3.zero);
                MoveSlot(slot, c, new Vector3((i - 3) * .033f, .012f + item.dimensions.size.y * .5f, -.004f), Quaternion.Euler(0, -90, 0));
                Bind(item, slot);
                // Spine text and a contrasting headband are separate 3D presentation, not interaction logic.
                var mark = Child(item.visualRoot, "Independent spine index", new Vector3(-item.dimensions.size.x * .5f - .001f, .014f, 0));
                mark.localRotation = Quaternion.Euler(0, -90, 0);
                Label(mark, (i < 4 ? "TN " : "MEMO ") + (i + 1), Vector3.zero, .00145f);
            }
            bindings.notebookShelf = c;
            Object.FindFirstObjectByType<NotebookWorkbench>().notebooks = books.ToArray();
            RetainFixture("TravelersBookcase");
            var oldPen = GameObject.Find("BrassPenPot"); if (oldPen) { oldPen.transform.SetParent(archive, true); oldPen.SetActive(false); }
        }

        static void Tools()
        {
            var penBox = OpenBox(bindings.rightDrawer.slide, "Pen case", "笔盒 · 六种笔", new Vector3(.205f, .008f, 0), new Vector2(.075f, .350f), .040f, blue);
            for (int i = 0; i < 6; i++)
            {
                var item = NewTool("atelier_pen_" + (i + 1).ToString("D2"), new[] { "钢笔", "薄荷中性笔", "蜜桃水笔", "蓝色铅笔", "淡紫记号笔", "白色高光笔" }[i], new Vector3(.009f, .009f, .137f));
                Cylinder(item.visualRoot, "Pen barrel", Vector3.zero, .0042f, .110f, new[] { ink, mint, pink, blue, lilac, cream }[i], Vector3.forward);
                Cylinder(item.visualRoot, "Pen cap", new Vector3(0, 0, -.052f), .0044f, .030f, gold, Vector3.forward);
                Cylinder(item.visualRoot, "Writing tip", new Vector3(0, 0, .061f), .002f, .015f, ink, Vector3.forward);
                Bind(item, Slot(penBox.transform, penBox, "usability-home-" + item.itemInstanceId, PhysicalItemType.Tool, new Vector3((i % 3 - 1) * .019f, .022f, i < 3 ? -.087f : .087f)));
            }
            var box = OpenBox(root, "Desktop hinged supply box", "桌面收纳盒", new Vector3(.210f, .760f, -.280f), new Vector2(.110f, .082f), .045f, mint);
            box.canClose = true; box.closedLidEuler = Vector3.zero; box.openLidEuler = new Vector3(110, 0, 0);
            var hinge = Child(box.transform, "Supply box hinged lid", new Vector3(0, .047f, .041f)); box.lid = hinge;
            Box(hinge, "Solid maple lid", new Vector3(0, 0, -.041f), new Vector3(.114f, .005f, .086f), wood, true);
            box.isOpen = true; bindings.desktopBox = box;
            var glue = NewTool("atelier_dot_glue", "点点胶 · 可拿取查看", new Vector3(.055f, .026f, .032f));
            Box(glue.visualRoot, "Rounded glue dispenser body", Vector3.zero, new Vector3(.047f, .021f, .031f), mint, false);
            Cylinder(glue.visualRoot, "Visible adhesive reel", new Vector3(-.005f, .012f, 0), .011f, .002f, cream, Vector3.up);
            Cylinder(glue.visualRoot, "Reel axle", new Vector3(-.005f, .0115f, 0), .003f, .003f, gold, Vector3.up);
            Box(glue.visualRoot, "Application nose", new Vector3(.0225f, -.004f, 0), new Vector3(.010f, .009f, .022f), cream, false);
            var home = Slot(box.transform, box, "usability-home-" + glue.itemInstanceId, PhysicalItemType.Tool, new Vector3(0, .020f, 0));
            var desk = Slot(root, null, "usability-desktop-dot-glue", PhysicalItemType.Tool, new Vector3(.315f, .774f, -.235f));
            glue.ConfigureHome(desk); glue.homeSlot = home; glue.homeAnchor = home.transform; home.TryReserve(glue);
            AddPlacement(glue, home, desk); bindings.glue = glue;
        }

        static void ValidateBindings(DeskLayoutBindings b)
        {
            if (!b || b.placements == null || b.placements.Any(p => p == null || !p.item || !p.homeSlot || !p.desiredDisplaySlot)) throw new InvalidOperationException("Incomplete layout bindings.");
            if (b.placements.Select(p => p.item.itemInstanceId).Distinct().Count() != b.placements.Length) throw new InvalidOperationException("Duplicate layout instance.");
            if (b.placements.Select(p => p.homeSlot.slotId).Distinct().Count() != b.placements.Length) throw new InvalidOperationException("Duplicate permanent home.");
            if (b.placements.Where(p => p.item.itemType == PhysicalItemType.TravelersNotebook).Count() != 7) throw new InvalidOperationException("Spine-out library requires seven books.");
            if (!b.leftDrawer || !b.rightDrawer || b.leftDrawer.slideAxis != Vector3.back || b.rightDrawer.slideAxis != Vector3.back) throw new InvalidOperationException("Two bounded drawers are required.");
        }
        public static void ValidateScene()
        { ValidateBindings(Object.FindFirstObjectByType<DeskLayoutBindings>()); Debug.Log("[DeskUsability] Explicit binding validation passed."); }

        /// <summary>Correct only our authored fixtures; can be repeated without adding/removing any entity or slot.</summary>
        public static void Calibrate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath); var b = Object.FindFirstObjectByType<DeskLayoutBindings>();
            if (!b) throw new InvalidOperationException("Author the incremental desk before calibrating it.");
            int count = b.placements.Length; CalibrateCurrent(b); ValidateBindings(b);
            if (b.placements.Length != count) throw new InvalidOperationException("Calibration changed stock identity.");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("[DeskUsability] Calibrated drawer openings and true-scale contents; instances retained=" + count);
        }
        static void CalibrateCurrent(DeskLayoutBindings b)
        {
            // Retain the previous decorative paper leaves as inactive art; real refill instances already live in the drawer.
            var looseSamples = b.transform.Find("Loose refill samples");
            if (looseSamples) { Unpack(looseSamples.gameObject); looseSamples.gameObject.SetActive(false); EditorUtility.SetDirty(looseSamples.gameObject); }
            var desk = GameObject.Find("Desk");
            var apron = desk ? desk.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Front apron") : null;
            if (apron) { Unpack(apron.gameObject); apron.gameObject.SetActive(false); }
            foreach (var d in new[] { b.leftDrawer, b.rightDrawer })
            {
                var p = d.transform.position; p.x = d == b.leftDrawer ? -.281f : .281f; d.transform.position = p; d.travel = .46f;
                d.transform.Find("Fixed upper rail").localScale = new Vector3(.540f, .012f, .466f);
                foreach (Transform child in d.transform)
                    if (child.name == "Fixed side guide") { var q = child.localPosition; q.x = Mathf.Sign(q.x) * .266f; child.localPosition = q; }
                d.slide.Find("Solid drawer floor").localScale = new Vector3(.520f, .006f, .450f);
                foreach (Transform child in d.slide)
                {
                    if (child.name == "Drawer side") { var q = child.localPosition; q.x = Mathf.Sign(q.x) * .257f; child.localPosition = q; }
                    if (child.name == "Drawer front" || child.name == "Drawer back") child.localScale = new Vector3(.520f, .118f, .006f);
                }
                d.SetOpenAmount(d.OpenAmount); EditorUtility.SetDirty(d);
            }
            var tapeBox = b.leftDrawer.slide.Find("Tape archive box").GetComponent<PhysicalContainer>();
            tapeBox.transform.localPosition = new Vector3(.163f, .005f, 0);
            tapeBox.transform.Find("Independent box floor").localScale = new Vector3(.164f, .004f, .433f);
            foreach (Transform child in tapeBox.transform)
            {
                if (child.name == "Box side wall") { var q = child.localPosition; q.x = Mathf.Sign(q.x) * .080f; child.localPosition = q; }
                if (child.name == "Box front and back") child.localScale = new Vector3(.164f, .057f, .004f);
            }
            var workRolls = b.placements.Where(p => p.item.itemType == PhysicalItemType.Tape && p.desiredDisplaySlot.accessShaft).OrderBy(p => p.item.itemInstanceId).ToArray();
            var spareRolls = b.placements.Where(p => p.item.itemType == PhysicalItemType.Tape && !p.desiredDisplaySlot.accessShaft).OrderBy(p => p.item.itemInstanceId).ToArray();
            for (int i = 0; i < workRolls.Length; i++) workRolls[i].homeSlot.transform.localPosition = new Vector3((i % 5 - 2) * .028f, .029f, -.155f + i / 5 * .050f);
            for (int i = 0; i < spareRolls.Length; i++) spareRolls[i].homeSlot.transform.localPosition = new Vector3((i % 5 - 2) * .028f, .029f, .025f + i / 5 * .050f);
            b.leftDrawer.slide.Find("Refill archive box").localPosition = new Vector3(-.100f, .005f, 0);
            var stickerBoxes = b.rightDrawer.slide.GetComponentsInChildren<PhysicalContainer>(true)
                .Where(c => c.transform.name.StartsWith("SmallNoteBox_", StringComparison.Ordinal)).OrderBy(c => c.transform.name).ToArray();
            for (int i = 0; i < stickerBoxes.Length; i++)
            {
                // Adapt only the legacy square frame. The real two-column sticker sheets keep their metric sizes and anchors.
                var box = stickerBoxes[i]; box.transform.localPosition = new Vector3(-.185f + i * .130f, .008f, .110f);
                box.transform.localRotation = Quaternion.identity;
                box.transform.Find("Small walnut box floor").localScale = new Vector3(.128f, .006f, .153f);
                foreach (Transform child in box.transform)
                {
                    if (child.name == "Box side") { var p = child.localPosition; p.x = Mathf.Sign(p.x) * .061f; child.localPosition = p; child.localScale = new Vector3(.006f, .024f, .153f); }
                    if (child.name == "Box front/back") child.localScale = new Vector3(.128f, .024f, .006f);
                }
                foreach (var text in box.GetComponentsInChildren<TextMesh>(true))
                {
                    text.transform.localPosition = new Vector3(0, .028f, -.078f);
                    text.transform.localRotation = Quaternion.identity;
                }
                EditorUtility.SetDirty(box.transform);
            }
            foreach (var slot in b.desktopNotes.GetComponentsInChildren<PhysicalStorageSlot>())
            {
                var p = slot.transform.localPosition; int layer = Mathf.RoundToInt((p.y - (p.y >= .05f ? .052f : .032f)) / .001f);
                p.y = .052f + layer * .001f; slot.transform.localPosition = p;
            }
            foreach (Transform child in b.desktopNotes.transform)
                if (child.name.StartsWith("Bound note pad body ", StringComparison.Ordinal)) { var p = child.localPosition; p.y = .027f; child.localPosition = p; child.localScale = new Vector3(.075f, .049f, .075f); }
            b.desktopBox.openLidEuler = new Vector3(110, 0, 0); b.desktopBox.Refresh();
            var glueArt = b.glue.visualRoot; glueArt.Find("Reel axle").localPosition = new Vector3(-.005f, .0115f, 0); glueArt.Find("Application nose").localPosition = new Vector3(.0225f, -.004f, 0);
            CalibrateReleaseWorkplaces(b);
            foreach (var entry in b.placements)
            {
                var item = entry.item; var current = item.CurrentSlot;
                if (current) item.transform.SetPositionAndRotation(current.WorldPose.position, current.WorldPose.rotation);
                EditorUtility.SetDirty(item); EditorUtility.SetDirty(item.transform);
            }
            DeskUsabilityCameraSetup.Apply();
            foreach (var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c && PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(b);
        }
        static void CalibrateReleaseWorkplaces(DeskLayoutBindings b)
        {
            var surface = Object.FindFirstObjectByType<DeskPlacementSurface>();
            if (!surface) throw new InvalidOperationException("A metric placement surface is required for release-book workplaces.");
            var host = b.leftDrawer.transform.parent;
            foreach (var entry in b.placements.Where(p => p.item.itemType == PhysicalItemType.ReleaseBook))
            {
                bool first = entry.item.assetId == "ReleaseBook_A";
                string id = first ? "usability-release-work-a" : "usability-release-work-b";
                var slot = host.GetComponentsInChildren<PhysicalStorageSlot>(true).FirstOrDefault(s => s.slotId == id);
                if (!slot) slot = Slot(host, null, id, PhysicalItemType.ReleaseBook, Vector3.zero);
                var location = first ? new Vector3(-.32f, 0, -.08f) : new Vector3(-.47f, 0, -.24f);
                var pose = surface.RestingPose(entry.item, location, Quaternion.identity);
                if (!surface.IsWithinBounds(entry.item, pose)) throw new InvalidOperationException("Release-book workplace overhangs the metric desk.");
                if (surface.HasCollision(entry.item, pose.position, pose.rotation, false)) throw new InvalidOperationException("Release-book workplace is blocked: " + entry.item.itemInstanceId);
                slot.transform.SetPositionAndRotation(pose.position, pose.rotation);
                // This is an open temporary work position; the original drawer/home ID stays its long-term return address.
                var home = entry.homeSlot;
                entry.item.ConfigureHome(slot); entry.item.homeSlot = home; entry.item.homeAnchor = home.transform; home.TryReserve(entry.item);
                entry.desiredDisplaySlot = slot;
                EditorUtility.SetDirty(slot); EditorUtility.SetDirty(entry.item);
            }
        }
        static void AddPlacement(PhysicalInteractable item, PhysicalStorageSlot home, PhysicalStorageSlot display)
        { placements.Add(new DeskLayoutPlacement { item = item, homeSlot = home, desiredDisplaySlot = display }); EditorUtility.SetDirty(item); }
        static void Bind(PhysicalInteractable item, PhysicalStorageSlot slot)
        { item.ConfigureHome(slot); item.transform.localScale = Vector3.one; AddPlacement(item, slot, slot); }
        static void MoveSlot(PhysicalStorageSlot slot, PhysicalContainer container, Vector3 local, Quaternion rotation)
        { slot.transform.SetParent(container.transform, false); slot.container = container; slot.transform.localPosition = local; slot.transform.localRotation = rotation; EditorUtility.SetDirty(slot); }
        static PhysicalStorageSlot Slot(Transform parent, PhysicalContainer container, string id, PhysicalItemType type, Vector3 local, Quaternion? rotation = null)
        {
            var t = Child(parent, id, local); t.localRotation = rotation ?? Quaternion.identity;
            var slot = t.gameObject.AddComponent<PhysicalStorageSlot>(); slot.container = container; slot.slotId = id; slot.acceptedType = type; slot.permanentReservation = true; slot.snapRadius = .028f; return slot;
        }
        static PhysicalInteractable Clone(PhysicalInteractable source, string id)
        {
            var old = Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).FirstOrDefault(i => i.itemInstanceId == id); if (old) return old;
            var item = Object.Instantiate(source.gameObject, stock).GetComponent<PhysicalInteractable>(); item.name = source.assetId + " independent stock";
            item.itemInstanceId = id; item.homeSlot = null; item.homeAnchor = null; item.transform.localScale = Vector3.one; return item;
        }
        static PhysicalInteractable NewTool(string asset, string title, Vector3 size)
        {
            var t = Child(stock, title, Vector3.zero); var item = t.gameObject.AddComponent<PhysicalInteractable>();
            item.itemInstanceId = "physical-" + asset + "-001"; item.assetId = asset; item.displayName = title; item.itemType = PhysicalItemType.Tool;
            var path = Folder + "Dimensions/" + asset + ".asset"; var d = AssetDatabase.LoadAssetAtPath<PhysicalDimensionProfile>(path);
            if (!d) { d = ScriptableObject.CreateInstance<PhysicalDimensionProfile>(); AssetDatabase.CreateAsset(d, path); }
            d.size = d.colliderSize = size; d.minDistance = .05f; d.maxDistance = 3; item.dimensions = d;
            item.interactionCollider = t.gameObject.AddComponent<BoxCollider>(); item.interactionCollider.size = size;
            item.visualRoot = Child(t, "Replaceable tool presentation", Vector3.zero); item.grabAnchor = Child(t, "Grab anchor", Vector3.zero);
            item.placementAnchor = Child(t, "Placement anchor", Vector3.down * size.y * .5f); item.inspectAnchor = Object.FindFirstObjectByType<PhysicalInteractionController>().inspectAnchor;
            EditorUtility.SetDirty(d); return item;
        }
        static PhysicalContainer OpenBox(Transform parent, string name, string title, Vector3 pos, Vector2 size, float depth, Material material)
        {
            var t = Child(parent, name, pos); var box = t.gameObject.AddComponent<PhysicalContainer>(); box.displayName = title; box.canClose = false; box.isOpen = true;
            Box(t, "Independent box floor", Vector3.zero, new Vector3(size.x, .004f, size.y), material, false);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(t, "Box side wall", new Vector3(s * (size.x * .5f - .002f), depth * .5f, 0), new Vector3(.004f, depth, size.y), material, false);
                Box(t, "Box front and back", new Vector3(0, depth * .5f, s * (size.y * .5f - .002f)), new Vector3(size.x, depth, .004f), material, false);
            }
            Label(t, title.Contains("笔") ? "PENS" : title.Contains("内页") ? "REFILLS" : title.Contains("胶带") ? "WASHI" : "NOTES", new Vector3(0, depth * .55f, -size.y * .5f - .0005f), .0014f);
            return box;
        }
        static void RetainFixture(string name)
        { var go = GameObject.Find(name); if (go) { go.transform.SetParent(archive, true); go.SetActive(false); } }
        static void Unpack(GameObject go)
        { var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go); if (outer) PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); }
        static Transform Child(Transform parent, string name, Vector3 pos)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = pos; return t; }
        static Transform Box(Transform p, string name, Vector3 position, Vector3 size, Material material, bool collide)
        { var t = Piece(p, name, PrimitiveType.Cube, position, size, material); if (!collide) Object.DestroyImmediate(t.GetComponent<Collider>()); return t; }
        static void Cylinder(Transform p, string name, Vector3 position, float radius, float length, Material material, Vector3 axis)
        { var t = Piece(p, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2, length * .5f, radius * 2), material); t.localRotation = Quaternion.FromToRotation(Vector3.up, axis); Object.DestroyImmediate(t.GetComponent<Collider>()); }
        static Transform Piece(Transform p, string name, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        { var t = GameObject.CreatePrimitive(type).transform; t.name = name; t.SetParent(p, false); t.localPosition = position; t.localScale = size; t.GetComponent<Renderer>().sharedMaterial = material; return t; }
        static Material Mat(string name, string hex)
        { var path = Folder + "Materials/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); } ColorUtility.TryParseHtmlString("#" + hex, out var c); m.color = c; m.SetFloat("_Smoothness", .2f); return m; }
        static void Label(Transform p, string text, Vector3 pos, float size)
        { var t = Child(p, "Independent printed label", pos); var l = t.gameObject.AddComponent<TextMesh>(); l.text = text; l.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); l.characterSize = size; l.fontSize = 64; l.anchor = TextAnchor.MiddleCenter; l.color = new Color(.25f, .31f, .43f); t.GetComponent<Renderer>().sharedMaterial = l.font.material; }
    }
}
