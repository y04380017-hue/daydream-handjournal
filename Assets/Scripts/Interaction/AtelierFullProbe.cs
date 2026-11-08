using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Explicit opt-in evidence for the complete demo. Real GPU frames/actions, never a claim of native mouse input.</summary>
    public sealed class AtelierFullProbe : MonoBehaviour
    {
        public string output;
        public bool restart;
        public Action<bool> completed;
        WorkspaceGameplayController w;
        JournalController j;
        DreamGameplayController dream;
        CommissionDeskController mail;
        AtelierHub hub;
        float started;
        bool finished;
        readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        readonly List<Check> checks = new List<Check>();
        readonly List<string> errors = new List<string>(), screenshots = new List<string>();
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Expected { public string sourceId, artId, diary, title; public int coins, purchases, receipts; }
        [Serializable] public sealed class Report
        {
            public bool passed, actualPlayMode, actualWindowsPlayer, nativeInputValidated, freshProcessRestart;
            public string timestampUtc, unity, gpu, savePath, scope;
            public int frames; public Check[] checks; public string[] errors, screenshots, manual;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--atelier-full-probe");
            if (Application.isEditor || index < 0 || index + 1 >= args.Length || !args.Contains("--atelier-full-validation") || !args.Contains("--desk-revision-validation")) return;
            var target = new GameObject("Complete gameplay player evidence").AddComponent<AtelierFullProbe>();
            target.output = Path.GetFullPath(args[index + 1]); target.restart = args.Contains("--atelier-full-restart"); Application.runInBackground = true;
        }
        void Start() { Directory.CreateDirectory(output); started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; stack.Push(Execute()); }
        void Log(string message, string trace, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(message + "\n" + trace); }
        void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup - started > 180) { Mark("Harness timeout", false); Finish(); return; }
            RuntimeInputGate.BlocksLegacyInput = hub && hub.IsOpen;
            RuntimeInputGate.BlocksJournalOperations = hub && hub.IsOpen;
            try
            {
                while (stack.Count > 0) { var routine = stack.Peek(); if (!routine.MoveNext()) { stack.Pop(); continue; } if (routine.Current is IEnumerator nested) { stack.Push(nested); continue; } return; }
                Finish();
            }
            catch (Exception error) { errors.Add(error.ToString()); Mark("Harness exception", false, error.Message); Finish(); }
        }
        void Mark(string name, bool pass, string detail = "") { checks.Add(new Check { name = name, passed = pass, detail = detail }); Debug.Log("[AllStages] " + (pass ? "PASS " : "FAIL ") + name + " " + detail); }
        void Require(string name, bool pass, string detail = "") { Mark(name, pass, detail); if (!pass) throw new InvalidOperationException(name + ": " + detail); }
        IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        IEnumerator Idle() { float end = Time.realtimeSinceStartup + 10; while ((j.IsBusy || j.cameraRig.IsTransitioning || w.physical.IsSettling) && Time.realtimeSinceStartup < end) yield return null; yield return null; }
        IEnumerator Shot(string name)
        {
            yield return Wait(.25f); string path = Path.Combine(output, name + ".png");
            if (!Application.isEditor) { ScreenCapture.CaptureScreenshot(path); yield return Wait(.35f); Require("Windows screenshot " + name, File.Exists(path)); }
            else
            {
                var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); var previous = RenderTexture.active;
                try { RenderPipeline.SubmitRenderRequest(w.lens, new RenderPipeline.StandardRequest { destination = target }); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
                finally { RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(texture); }
            }
            screenshots.Add(path);
        }
        Ray PageRay(JournalSide side, Vector2 position)
        { var anchor = j.pageView.GetSurface(side); var size = j.pageView.SurfaceSize(side); return w.lens.ScreenPointToRay(w.lens.WorldToScreenPoint(anchor.TransformPoint(new Vector3(position.x * size.x, position.y * size.y, 0)))); }
        IEnumerator Execute()
        {
            float end = Time.realtimeSinceStartup + 30;
            while (!(w = FindFirstObjectByType<WorkspaceGameplayController>()) || !w.Ready || !(dream = DreamGameplayController.Instance) || !dream.Ready ||
                !(mail = CommissionDeskController.Instance) || !mail.Ready || !(hub = AtelierHub.Instance) || w.Service.Document.personalStudio == null)
            { if (Time.realtimeSinceStartup > end) throw new TimeoutException("Complete gameplay bindings did not initialize"); yield return null; }
            j = w.journal;
            foreach (var adapter in FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None)) adapter.enabled = false;
            RuntimeInputGate.Reset(); w.desk.Begin(); yield return Wait(.5f);
            Require("Isolated save", j.SavePath.Contains("DeskRevisionValidation"), j.SavePath);
            Require("Old schema and original binder pages", j.Document.schemaVersion == 1 && j.Document.pages.Length == 24 && j.Document.pages[0].pageId == "journal-page-001");
            if (restart) { yield return Reload(); yield break; }
            var time = FindFirstObjectByType<DreamTimeController>();
            foreach (int setting in new[] { 0, 1, 2 })
            {
                if (setting == 0) time.SetNight(true, false, true); else if (setting == 1) time.SetNight(false, false, true); else time.SetDusk(true, false, true);
                Require("Initialize collection setting " + setting, w.Service.RefreshSettingCollection(setting, DateTime.UtcNow.Ticks)); yield return Wait(.5f);
                Physics.SyncTransforms();
                var pending = dream.Data.pending.Where(t => t.setting == setting).ToArray(); Require("Independent stock setting " + setting, pending.Length == 3);
                var views = dream.collection.Collectibles.Where(v => v && v.gameObject.activeInHierarchy && pending.Any(t => t.tokenId == v.TokenId)).ToArray();
                Require("Three 3D collectibles setting " + setting, views.Length == 3);
                Ray? chosen = null;
                foreach (var view in views)
                {
                    var screen = w.lens.WorldToScreenPoint(view.HitCollider.bounds.center); var ray = w.lens.ScreenPointToRay(screen);
                    bool visible = screen.z > 0 && screen.x > 0 && screen.y > 0 && screen.x < Screen.width && screen.y < Screen.height;
                    bool clear = Physics.Raycast(ray, out var hit, 3, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider == view.HitCollider;
                    Mark("Seated visible collider " + setting + "/" + view.VisualKind, visible && clear);
                    if (visible && clear && !chosen.HasValue) chosen = ray;
                }
                Require("Collect actual world object setting " + setting, chosen.HasValue && dream.collection.TryClick(chosen.Value));
                Require("Only active setting loses one token " + setting, dream.Data.pending.Count(t => t.setting == setting) == 2);
                yield return Shot("setting-" + setting);
            }
            time.SetNight(true, false, true); yield return Wait(.3f);
            var collected = w.Service.Items.First(i => i.instanceId.StartsWith("dream-catch-", StringComparison.Ordinal) && i.owner == CraftOwner.Stock);
            string sourceId = collected.instanceId;
            Require("Real held ownership", w.Service.BeginStock(sourceId)); Require("Cancel restores stock", w.Cancel() && w.Service.GetItem(sourceId).owner == CraftOwner.Stock);
            Require("Enter binder", j.EnterEditing()); yield return Idle();
            Require("Place finite collected material", w.Service.BeginStock(sourceId) && w.TryPlacePointer(PageRay(JournalSide.Left, new Vector2(.5f, .5f))));
            string pageId = j.Document.GetPage(JournalSide.Left).pageId;
            for (int i = 0; i < 12; i++)
            {
                w.ClearSelection();
                var letter = mail.Service.OpenLetters.FirstOrDefault(); if (letter == null) { mail.Service.RequestLetterNow(); letter = mail.Service.OpenLetters.FirstOrDefault(); }
                Require("Recurring brief " + i, letter != null);
                Require("Fresh page composition " + i, w.Service.RotateAttached(sourceId, i * 11));
                mail.OpenPanel(); mail.SelectLetter(letter.letterId); Require("Target authored page " + i, mail.SelectTarget(CraftOwner.JournalPage, "journal", pageId));
                Require("Actual photo send " + i, mail.SendActive(), mail.Status); mail.Service.FastForwardReplies(); mail.ClosePanel(); yield return null;
            }
            Require("Reputation advances from real replies", AtelierProgressCatalog.Reputation(j.Document) >= 4);
            foreach (string upgrade in AtelierProgressCatalog.UpgradeIds) Require("Atomic upgrade " + upgrade, w.Service.PurchaseUpgrade(upgrade), w.Service.LastMessage);
            int coins = dream.Data.coins;
            Require("Repeat purchase cannot deduct twice", !w.Service.PurchaseUpgrade(AtelierProgressCatalog.UpgradeIds[0]) && dream.Data.coins == coins);
            var today = DateTime.Now;
            bool dailyAlready = j.Document.atelierProgress.dailyLetters.Contains(AtelierProgressCatalog.DateId(today));
            if (!dailyAlready) Require("Daily commission", w.Service.ClaimDailyCommission(today));
            Require("Daily request is idempotent", !w.Service.ClaimDailyCommission(today));
            int beforeSeason = j.Document.atelierProgress.seasonalClaims.Count;
            bool claimed = w.Service.ClaimSeasonalEvent(today);
            Require("Seasonal event claims exactly once", (claimed ? j.Document.atelierProgress.seasonalClaims.Count == beforeSeason + 1 : true) && !w.Service.ClaimSeasonalEvent(today));
            Require("Customize personal cover", w.Service.CustomizeCover("验收用云端日记", "棉布", 3, 2));
            Require("Private mode", w.Service.SetPersonalMode(true));
            Require("Themed section", w.Service.AddStudioSection("今天收集的光", pageId));
            Require("Actual diary text", w.Service.SaveDiary(today.ToString("yyyy-MM-dd"), AtelierProgressCatalog.TodayPrompt(today), "今天把一小片星光留在纸上。"));
            Require("Favorite and date index", w.Service.MarkStudioPage(pageId, "星光第一页", true));
            Require("Colored paper appearance", w.Service.SetStudioPaper(pageId, j.Document.GetPage(JournalSide.Left).paperId, 2));
            var stamp = StudioGraphics.Stamp(1, StudioGraphics.Colors[2]); var diecut = StudioGraphics.DieCut(stamp);
            string artId;
            try { artId = w.Service.SaveStudioArt("手绘星光", "sticker", Convert.ToBase64String(diecut.EncodeToPNG())); }
            finally { Destroy(stamp); Destroy(diecut); }
            Require("Save sticker template", !string.IsNullOrEmpty(artId), w.Service.LastMessage);
            PersonalStudioAssets.Bind(w);
            Require("Custom sticker renders with transparent mask", PersonalStudioAssets.Find(artId)?.alphaMask.Any(a => a == 0) == true);
            Require("Print and place custom sticker", w.Service.BeginStudioArt(artId) && w.TryPlacePointer(PageRay(JournalSide.Right, new Vector2(.5f,.5f))));
            var custom = w.Service.Items.Last(i => i.assetId == artId); string customId = custom.instanceId;
            Require("Undo custom page edit preserves template", w.Undo() && j.Document.personalStudio.artworks.Any(a => a.assetId == artId) && w.Service.GetItem(customId).owner == CraftOwner.Stock);
            Require("Redo custom ownership", w.Redo() && w.Service.GetItem(customId).owner == CraftOwner.JournalPage);
            foreach (int style in new[] { 0, 1, 2 })
            {
                var text = StudioGraphics.Text("白日梦 hand made", style, DaydreamPalette.Ink);
                try { Require("Handwriting texture " + style, text.GetPixels32().Any(c => c.a > 64) && text.GetPixels32().Any(c => c.a == 0)); }
                finally { Destroy(text); }
            }
            var photo = StudioGraphics.Scene(w.lens, 1); var framed = StudioGraphics.Framed(photo);
            try { Require("Photo frame creates usable material", !string.IsNullOrEmpty(w.Service.SaveStudioArt("工作室照片", "photo", Convert.ToBase64String(framed.EncodeToPNG())))); }
            finally { Destroy(photo); Destroy(framed); }
            string exported = StudioExports.ExportPage(w, mail.Targets().First(t => t.pageId == pageId)); Require("Styled PNG export", File.Exists(exported) && new FileInfo(exported).Length > 1000, exported);
            string year = StudioExports.ExportYear(w); Require("Real record year review export", File.Exists(year) && new FileInfo(year).Length > 500, year);
            string metrics=AtelierProgressPanel.ExportMetrics(w);string metricText=File.ReadAllText(metrics);
            Require("Local metrics export excludes diary contents",metricText.StartsWith("record_type,")&&!metricText.Contains("今天把一小片星光")&&j.Document.atelierProgress.sessions.Count>0&&j.Document.atelierProgress.shares.Count>=2,metrics);
            // A social receipt may arrive during paper selection: cancel only changes provisional paper IDs.
            w.ClearSelection();w.scrapbook.Cancel();yield return null;
            j.BeginPaperSelection(JournalSide.Left);
            Require("Paper preview opens", j.HasPaperPreview);
            Require("Independent receipt while preview open", w.Service.SaveDiary(today.ToString("yyyy-MM-dd"), "预览保护", "取消换纸也不会丢掉这条记录。"));
            j.CancelPaper(); Require("Cancel preview preserves independently saved diary", j.Document.personalStudio.diary.Find(d => d.dateKey == today.ToString("yyyy-MM-dd")).body == "取消换纸也不会丢掉这条记录。");
            if(Environment.GetCommandLineArgs().Contains("--atelier-http-fixture"))yield return AtelierSocialProbeSteps.Run(w,output,Mark);
            hub.Open();
            foreach(int tab in new[]{0,1,2,3,4,5,6}){hub.SelectTab(tab);yield return Shot("creative-hub-"+tab);}
            hub.Close(); RuntimeInputGate.Reset();
            Require("All optional data validates", AtelierProgressData.Validate(j.Document, out _) && PersonalStudioData.Validate(j.Document, out _) && DreamGameplayData.Validate(j.Document, out _));
            Require("Atomic full save", j.Save() && w.Service.Flush());
            var expected = new Expected { sourceId = customId, artId = artId, diary = "取消换纸也不会丢掉这条记录。", title = j.Document.personalStudio.title,
                coins = dream.Data.coins, purchases = j.Document.atelierProgress.purchases.Count, receipts = dream.Data.claimedRewardLetters.Count };
            File.WriteAllText(Path.Combine(output, "expected.json"), JsonUtility.ToJson(expected, true));
            yield return Shot("complete-desk");
        }
        IEnumerator Reload()
        {
            var expected = JsonUtility.FromJson<Expected>(File.ReadAllText(Path.Combine(output,"expected.json")));
            Require("Full data persisted across process", j.Document.personalStudio.title == expected.title && dream.Data.coins == expected.coins &&
                j.Document.atelierProgress.purchases.Count == expected.purchases && dream.Data.claimedRewardLetters.Count == expected.receipts);
            Require("Custom instance and template retain ownership", w.Service.GetItem(expected.sourceId)?.owner == CraftOwner.JournalPage && j.Document.personalStudio.artworks.Any(a => a.assetId == expected.artId));
            Require("Diary survives restart", j.Document.personalStudio.diary.Any(d => d.body == expected.diary));
            Require("Custom art resolves after reload", PersonalStudioAssets.Find(expected.artId)?.IsAvailable == true);
            if(Environment.GetCommandLineArgs().Contains("--atelier-http-fixture"))
            {
                var social=AtelierSocialController.Ensure(w);
                Require("Social identity and gift ledger survive restart",social.Registered&&social.Data.gifts.Count==1&&social.Data.claimedGiftIds.Count==1);
                var gift=social.Data.gifts[0];
                Require("Received gift restores exact unique instance and artwork",w.Service.Items.Count(i=>i.instanceId=="dream-social-"+gift.giftId)==1&&SocialMaterialAssets.Find(gift.assetId)?.IsAvailable==true);
                social.View=0;social.Refresh();float until=Time.realtimeSinceStartup+15;
                while(social.Busy&&Time.realtimeSinceStartup<until)yield return null;
                Require("Persisted identity authenticates after server process restart",!social.Busy&&social.LastRequestSucceeded);
            }
            Require("Reload validates all data", CraftWorkspaceData.Validate(j.Document,out _) && DreamGameplayData.Validate(j.Document,out _) && PersonalStudioData.Validate(j.Document,out _) && AtelierProgressData.Validate(j.Document,out _) && AtelierSocialData.Validate(j.Document,out _));
            hub.Open(); yield return Shot("reloaded-hub"); hub.Close();
        }
        void Finish()
        {
            if (finished) return; finished = true; Application.logMessageReceived -= Log;
            bool pass = checks.Count > 0 && checks.All(c => c.passed) && errors.Count == 0;
            File.WriteAllText(Path.Combine(output, restart ? "restart.json" : "result.json"), JsonUtility.ToJson(new Report {
                passed=pass,actualPlayMode=Application.isEditor,actualWindowsPlayer=!Application.isEditor,nativeInputValidated=false,freshProcessRestart=restart,
                timestampUtc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,savePath=j?j.SavePath:null,
                scope="Runtime actions and actual camera rays. Editor GPU captures exclude IMGUI; Windows screenshots include it. Local HTTP and native mouse have separate reports.",
                frames=Time.frameCount,checks=checks.ToArray(),errors=errors.ToArray(),screenshots=screenshots.ToArray(),manual=new[]{"Human playtest/retention", "Multi-hour natural pacing", "Public network operation"}},true));
            Debug.Log("[AllStages] Completed " + (pass?"PASS":"FAIL")); if(completed!=null)completed(pass);else Application.Quit(pass?0:1);
        }
    }
}
