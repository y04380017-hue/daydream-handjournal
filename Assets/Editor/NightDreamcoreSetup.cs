using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Night dreamcore (2026-11-07 spec): incremental. Rebuilds only the "Night dreamcore" root, its grade profile and
    /// three materials, and moves/scales the existing moon (original pose is restored by Remove). Never reruns the
    /// luminous, desk-revision or bright-pass authoring tools. Backup: handbook-before-night-dreamcore-20261107.
    /// </summary>
    public static class NightDreamcoreSetup
    {
        const string Scene = "Assets/Scenes/VintageJournalDesk.unity", Folder = "Assets/Daydream/NightDreamcore/", RootName = "Night dreamcore";
        static readonly Vector3 MoonHomePosition = new Vector3(-1.6f, .48f, 4), MoonHomeEuler = new Vector3(0, 0, 342), MoonHomeScale = Vector3.one;
        static Material glowMat; static Transform root; static DeskCameraProfile seat; static readonly List<NightDreamcore.Glow> glows = new List<NightDreamcore.Glow>();
        static readonly List<string> log = new List<string>();

        [MenuItem("Vintage Journal/Daydream/Install night dreamcore")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(Scene); log.Clear(); glows.Clear();
            var time = Object.FindFirstObjectByType<DreamTimeController>(); if (!time) throw new InvalidOperationException("DreamTimeController missing.");
            if (!GameObject.Find("Luminous postal atelier")) throw new InvalidOperationException("Install after the luminous atelier pass.");
            var old = GameObject.Find(RootName); if (old) Object.DestroyImmediate(old);
            seat = Object.FindFirstObjectByType<DeskCamera>().profile;
            root = new GameObject(RootName).transform;
            var core = root.gameObject.AddComponent<NightDreamcore>();
            glowMat = Mat("Night glow card", "Daydream/Night glow card"); glowMat.renderQueue = 3400;   // after the refractive glass veils, which would otherwise paint over far glows
            core.grade = Grade();

            // ---- Light rig additions: rim from behind and an uncanny glow from under the tabletop ----
            var rim = new GameObject("Rim light from behind").AddComponent<Light>(); rim.transform.SetParent(root, false);
            rim.type = LightType.Directional; rim.transform.rotation = Quaternion.Euler(28, 165, 0); rim.color = core.rim; rim.intensity = 0; rim.shadows = LightShadows.None; rim.enabled = false;
            var under = new GameObject("Glow from under the tabletop").AddComponent<Light>(); under.transform.SetParent(root, false);
            under.type = LightType.Point; under.transform.position = new Vector3(0, .5f, .05f); under.range = 1.35f; under.color = core.underGlow; under.intensity = 0; under.shadows = LightShadows.None; under.enabled = false;
            core.rimLight = rim; core.underTableGlow = under;
            var sunData = time.sun ? time.sun.GetComponent<UniversalAdditionalLightData>() : null; if (sunData) { sunData.softShadowQuality = SoftShadowQuality.High; EditorUtility.SetDirty(sunData); }

            // ---- Sourceless emissive glows ----
            DrawerGlows(); TrayGlows(); CardRackGlow(); MailboxGlow(); PageGlows();
            // ---- Sky: bigger moon with a halo, a faint second moon, a far glowing doorway, near soft stars ----
            Moon(time); Doorway(); NearStars();
            // ---- Liminal ground: endless floor fading into fog, thin cloud layer below the furniture ----
            core.floor = Plane("Endless dream floor", new Vector3(0, -.82f, .6f), 6, Mat("Night dream floor", "Daydream/Night dream floor"));
            core.cloudLayer = Plane("Thin cloud layer below the desk", new Vector3(0, -.55f, .6f), 2.2f, Mat("Night cloud layer", "Daydream/Night cloud layer"));

            core.glows = glows.ToArray();
            core.lanternShells = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.sharedMaterial && r.sharedMaterial.name == "Honey lantern").ToArray();
            core.lanternAureoles = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.sharedMaterial && r.sharedMaterial.name == "Beeswax aureole").ToArray();
            var sea = GameObject.Find("Daydream Studio/Far cloud sea"); core.darkNightSurfaces = sea ? new[] { sea.GetComponent<Renderer>() } : Array.Empty<Renderer>();
            core.motes = Object.FindFirstObjectByType<DaydreamMotes>(FindObjectsInactive.Include);
            var music = GameObject.Find("DeskExperience"); core.music = music ? music.GetComponent<AudioSource>() : null;
            time.dreamcore = core; time.nightTransitionSeconds = 8;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            if (root.GetComponentsInChildren<Collider>(true).Length > 0) throw new InvalidOperationException("Night dreamcore must not contain colliders.");
            log.Add($"glows={glows.Count} lanternShells={core.lanternShells.Length} aureoles={core.lanternAureoles.Length} motes={(core.motes ? "yes" : "no")} music={(core.music ? "yes" : "no")}");
            EditorUtility.SetDirty(time); EditorUtility.SetDirty(core);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory("docs/night-dreamcore"); File.WriteAllLines("docs/night-dreamcore/authoring.txt", log);
            Debug.Log("[NightDreamcore] Installed. " + string.Join(" | ", log));
        }

        [MenuItem("Vintage Journal/Daydream/Remove night dreamcore")]
        public static void Remove()
        {
            var scene = EditorSceneManager.OpenScene(Scene);
            var old = GameObject.Find(RootName); if (old) Object.DestroyImmediate(old);
            var time = Object.FindFirstObjectByType<DreamTimeController>();
            if (time) { time.dreamcore = null; if (time.moon) { var m = time.moon.transform; m.position = MoonHomePosition; m.eulerAngles = MoonHomeEuler; m.localScale = MoonHomeScale; foreach (Transform c in m.Cast<Transform>().ToArray()) if (c.name.StartsWith("Dream ")) Object.DestroyImmediate(c.gameObject); } EditorUtility.SetDirty(time); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        // ------------------------------------------------------------------ helpers
        static Color Hex(string s) { ColorUtility.TryParseHtmlString("#" + s, out var c); return c; }
        static Material Mat(string name, string shader)
        {
            var sh = Shader.Find(shader); if (!sh || !sh.isSupported) throw new InvalidOperationException("Shader unavailable: " + shader);
            string path = Folder + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
            return m;
        }
        static Vector3 SeatRay(float vx, float vy, float distance)
        {
            var look = Quaternion.LookRotation(seat.seatedTarget - seat.seatedPosition, Vector3.up);
            float tan = Mathf.Tan(seat.seatedFieldOfView * Mathf.Deg2Rad * .5f);
            var dir = (look * new Vector3((vx * 2 - 1) * tan * (16f / 9f), (vy * 2 - 1) * tan, 1)).normalized;
            return seat.seatedPosition + dir * distance;
        }
        static Quaternion FaceSeat(Vector3 at) { var d = at - seat.seatedPosition; d.y = 0; return Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d : Vector3.forward, Vector3.up); }
        static Renderer Card(Transform parent, string name, Vector3 position, Quaternion rotation, Vector2 size, float shape, float softness, float core = 0, float fog = 0)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(parent ? parent : root, true); q.transform.position = position; q.transform.rotation = rotation;
            var ls = parent ? parent.lossyScale : Vector3.one; q.transform.localScale = new Vector3(size.x / Mathf.Max(1e-4f, ls.x), size.y / Mathf.Max(1e-4f, ls.y), 1);
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = glowMat; r.enabled = false;
            var b = new MaterialPropertyBlock(); b.SetFloat("_Shape", shape); b.SetFloat("_Softness", softness); b.SetFloat("_Core", core); b.SetFloat("_FogAmount", fog); b.SetFloat("_Intensity", 0); r.SetPropertyBlock(b);
            return r;
        }
        static void AddGlow(Renderer r, string hex, float intensity, int stage, float pulse = 0, float period = 0, float phase = 0)
            => glows.Add(new NightDreamcore.Glow { renderer = r, color = Hex(hex), intensity = intensity, stage = stage, pulse = pulse, period = period, phase = phase });
        static Renderer Plane(string name, Vector3 position, float scale, Material m)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Plane); Object.DestroyImmediate(p.GetComponent<Collider>());
            p.name = name; p.transform.SetParent(root, false); p.transform.position = position; p.transform.localScale = new Vector3(scale, 1, scale);
            var r = p.GetComponent<Renderer>(); r.sharedMaterial = m; r.enabled = false; return r;
        }
        static Bounds RenderBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) && !r.name.Contains("label") && !(r.sharedMaterial && r.sharedMaterial.shader.name.Contains("Text"))).ToArray();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero); foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static Volume Grade()
        {
            string path = Folder + "NightDreamcoreGrade.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile) { foreach (var c in profile.components.ToArray()) { profile.Remove(c.GetType()); Object.DestroyImmediate(c, true); } }
            else { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            T Add<T>() where T : VolumeComponent { var c = profile.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, profile); return c; }
            var bloom = Add<Bloom>(); bloom.threshold.value = .6f; bloom.intensity.value = .6f;   // spec .9; Unity's bloom scale blows the lit pages out at .9 bloom.scatter.value = .75f;
            var adj = Add<ColorAdjustments>(); adj.postExposure.overrideState = false; adj.contrast.value = -20; adj.saturation.value = -15;
            adj.colorFilter.overrideState = false; adj.hueShift.overrideState = false;
            var lgg = Add<LiftGammaGain>(); lgg.lift.value = new Vector4(.88f, .86f, 1f, .07f); lgg.gamma.value = new Vector4(1f, .99f, 1.02f, .02f); lgg.gain.value = new Vector4(1f, .98f, .94f, -.05f);
            var smh = Add<ShadowsMidtonesHighlights>(); smh.shadows.value = new Vector4(.90f, .88f, 1.04f, 0); smh.highlights.value = new Vector4(1f, .98f, .94f, 0);
            var dof = Add<DepthOfField>(); dof.mode.value = DepthOfFieldMode.Gaussian; dof.gaussianStart.value = 3f; dof.gaussianEnd.value = 14f; dof.gaussianMaxRadius.value = .8f;
            var ca = Add<ChromaticAberration>(); ca.intensity.value = .2f;
            var vig = Add<Vignette>(); vig.color.value = Hex("E8E2F5"); vig.intensity.value = .25f; vig.smoothness.value = .6f; vig.rounded.value = false;
            var grain = Add<FilmGrain>(); grain.type.value = FilmGrainLookup.Thin1; grain.intensity.value = .08f; grain.response.value = .8f;
            EditorUtility.SetDirty(profile);
            var go = new GameObject("Night grade volume"); go.transform.SetParent(root, false);
            var v = go.AddComponent<Volume>(); v.isGlobal = true; v.priority = 20; v.weight = 0; v.sharedProfile = profile;
            log.Add("grade: bloom .6/.9/.75, contrast -20, saturation -15, lift floor +.10 toward 5A5788, gain -.05 toward FFF1E0, gaussian far DoF, CA .2, pale vignette .25, grain .08");
            return v;
        }

        static void DrawerGlows()
        {
            var cabinet = GameObject.Find("WorkspaceStage4B/Wooden washi atelier"); if (!cabinet) { log.Add("drawers: cabinet missing"); return; }
            int n = 0;
            foreach (var name in new[] { "drawer-tape-top", "drawer-tape-middle", "drawer-release-books" })
            {
                var d = cabinet.transform.Find(name); if (!d) continue;
                var b = RenderBounds(d);
                // Light leaking from the gap above each drawer front; static, so an opened drawer shows the glow at the cabinet.
                var at = new Vector3(b.center.x, b.max.y + .003f, b.min.z - .002f);
                AddGlow(Card(null, "Drawer gap glow " + name, at, Quaternion.identity, new Vector2(b.size.x * 1.02f, .016f), 1, .9f), "FFF1C9", .6f, 1);
                n++;
            }
            var cb = RenderBounds(cabinet.transform);
            AddGlow(Card(null, "Cabinet inner haze", new Vector3(cb.center.x, cb.center.y - .02f, cb.min.z - .01f), Quaternion.identity, new Vector2(cb.size.x * 1.25f, cb.size.y * 1.1f), 0, .95f), "FFF1C9", .025f, 1);
            log.Add("drawers: " + n + " gap glows at cabinet front z=" + cb.min.z.ToString("F3"));
        }

        static void TrayGlows()
        {
            var fronts = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(t => t.name == "Low wooden tray front").Select(t => t.parent).Distinct().OrderBy(t => -t.position.z).ToArray();
            for (int i = 0; i < fronts.Length; i++)
            {
                var b = RenderBounds(fronts[i]);
                var yaw = Quaternion.Euler(90, fronts[i].eulerAngles.y, 0);
                AddGlow(Card(null, "Stamp tray glow " + (i + 1), new Vector3(b.center.x, b.min.y + .012f, b.center.z), yaw, new Vector2(b.size.x * .8f, b.size.z * .8f), 1, .6f), i % 2 == 0 ? "FFF1C9" : "CDE8F2", .25f, 1);
            }
            log.Add("stamp trays: " + fronts.Length + " (" + string.Join(", ", fronts.Select(f => f.name)) + ")");
        }

        static void CardRackGlow()
        {
            // The card board at the back right is found the way the player sees it: a ray from the seat through its screen spot.
            // The ray lands on one hanging note; the glow wraps every note within 0.3 m of it (the whole hanging board).
            var origin = seat.seatedPosition; var dir = (SeatRay(.635f, .833f, 1) - origin).normalized;
            var hits = Physics.RaycastAll(origin, dir, 4f).OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0) { log.Add("card rack: nothing hit at viewport (.635,.833)"); return; }
            var first = hits[0].collider.transform; var board = first;
            var notes = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.parent == first.parent && t.name.StartsWith("note_") && (t.position - first.position).magnitude < .3f).ToArray();
            var b = RenderBounds(first); foreach (var n in notes) b.Encapsulate(RenderBounds(n));
            var rot = FaceSeat(b.center);
            AddGlow(Card(null, "Card rack edge glow", b.center + rot * Vector3.forward * (b.extents.z + .01f), rot, new Vector2(b.size.x * 1.12f, b.size.y * 1.08f), 1, .35f), "FFF1C9", .3f, 1, .1f, 5f, .2f);
            log.Add("card rack: hit " + Path(first) + ", wrapping " + notes.Length + " notes, size " + b.size.ToString("F2"));
        }

        static void MailboxGlow()
        {
            var box = GameObject.Find("Desk revision 20261107/Pastel floor mailbox/Rounded postbox"); if (!box) { log.Add("mailbox: missing"); return; }
            var b = RenderBounds(box.transform);
            AddGlow(Card(null, "Mailbox face glow", b.center, FaceSeat(b.center), new Vector2(.36f, .30f), 0, .9f, .25f), "FFD6E8", .55f, 1);
            log.Add("mailbox: focal glow FFD6E8 .8 at " + b.center.ToString("F2"));
        }

        static void PageGlows()
        {
            int n = 0;
            foreach (var name in new[] { "OpenJournal/Left decorated page", "OpenJournal/Right decorated page" })
            {
                var page = GameObject.Find(name); if (!page) continue;
                var b = page.GetComponent<Renderer>().bounds;
                // Child of the page, so it follows the page whenever the journal is shown, hidden or swapped.
                AddGlow(Card(page.transform, "Dream moonlit paper", b.center + Vector3.up * .0015f, Quaternion.Euler(90, 0, 0), new Vector2(b.size.x, b.size.z), 1, .5f), "F4EEFF", .15f, 1);
                n++;
            }
            log.Add("pages: " + n + " moonlit paper glows");
        }

        static void Moon(DreamTimeController time)
        {
            if (!time.moon) { log.Add("moon: missing"); return; }
            var m = time.moon.transform; foreach (Transform c in m.Cast<Transform>().ToArray()) if (c.name.StartsWith("Dream ")) Object.DestroyImmediate(c.gameObject);
            var spot = SeatRay(.83f, .86f, 6.5f);
            m.position = spot; m.eulerAngles = MoonHomeEuler; m.localScale = Vector3.one * 2.2f;
            var face = FaceSeat(spot); m.rotation = face * Quaternion.Euler(0, 0, -18);
            var mb = m.GetComponent<Renderer>().bounds;
            AddGlow(Card(m, "Dream moon halo", mb.center + face * Vector3.forward * .05f, face, Vector2.one * Mathf.Max(mb.size.x, mb.size.y) * 3f, 0, .95f), "E6E0FF", .3f, 0);
            var second = SeatRay(.43f, .935f, 9f);
            AddGlow(Card(null, "Faint second moon", second, FaceSeat(second), Vector2.one * .45f, 0, .55f, .7f), "E6E0FF", .18f, 0);
            log.Add("moon: x2.2 at viewport (.83,.86) 6.5 m with 3x halo; faint second moon at (.43,.935)");
        }

        static void Doorway()
        {
            // Stands on the dream floor far behind the desk; the seated view (looking ~30° down) sees its lower half above the cabinet.
            var at = new Vector3(-2.4f, -.82f + 1.8f, 10f); var face = FaceSeat(at);
            AddGlow(Card(null, "Distant glowing doorway", at, face, new Vector2(1.5f, 3.6f), 1, .12f, 0, .6f), "FFF1E0", .32f, 0);
            log.Add("doorway: 1.5 x 3.6 m standing on the floor at (-2.4, -0.82, 10), partly fogged");
        }

        static void NearStars()
        {
            var spots = new[] { new Vector3(-.98f, .56f, .42f), new Vector3(-1.18f, .32f, .95f), new Vector3(-.74f, .18f, -.28f), new Vector3(-1.35f, .74f, 1.25f),
                                new Vector3(1.08f, .62f, .52f), new Vector3(1.26f, .36f, 1.05f), new Vector3(.62f, .16f, -.30f), new Vector3(1.38f, .80f, 1.35f) };
            for (int i = 0; i < spots.Length; i++)
            {
                float size = .07f + .05f * ((i * 37) % 10) / 10f;
                var r = Card(null, "Near soft star " + (i + 1), spots[i], FaceSeat(spots[i]), Vector2.one * size, 0, .9f, .55f);
                var f = r.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .025f; f.bobPeriod = 7 + i % 3; f.swayDegrees = 0; f.phase = i * .37f;
                AddGlow(r, i % 2 == 0 ? "FFF1E0" : "CDE8F2", .32f, 1, .3f, 3 + (i * 5) % 6, i * .21f);
            }
            log.Add("near stars: " + spots.Length + " soft stars at/below table height, bob 7–9 s, twinkle 3–8 s");
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}
