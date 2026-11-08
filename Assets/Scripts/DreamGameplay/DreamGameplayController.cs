using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Night slice orchestration and material bag. Input is dispatched by WorkspaceInputRouter.</summary>
    public sealed class DreamGameplayController : MonoBehaviour
    {
        public static DreamGameplayController Instance { get; private set; }
        public WorkspaceGameplayController workspace;
        public DreamTimeController timeOfDay;
        public NightCollectionController collection;
        public MaterialDiscoveryPresentation discovery;
        [Min(1)] public int waitingCap = 6, refreshBatch = 3;
        [Min(.01f)] public float refreshHours = 2;
        public bool Ready { get; private set; }
        public bool BagOpen { get; private set; }
        public bool LegacyNightSlice {get;private set;}
        public int CurrentSetting=>timeOfDay&&timeOfDay.IsNight?0:timeOfDay&&timeOfDay.IsDusk?2:1;
        public DreamGameplayData Data => workspace && workspace.Ready ? workspace.Service.Document.dreamGameplay : null;
        public bool CanCollect => Ready && timeOfDay && (!LegacyNightSlice||timeOfDay.IsNight) && workspace.desk.HasBegun &&
            workspace.journal.cameraRig && workspace.journal.cameraRig.ForwardFacing &&
            !workspace.desk.IsBlocking && !BagOpen && !AtelierTutorial.Blocking && !workspace.InteractionBusy &&
            !(AtelierHub.Instance&&AtelierHub.Instance.IsOpen) &&
            !workspace.journal.IsBusy && !workspace.journal.HasPaperPreview && !workspace.scrapbook.IsTransforming &&
            !workspace.scrapbook.BrowserOpen && workspace.scrapbook.Chosen==null &&
            !(CommissionDeskController.Instance && CommissionDeskController.Instance.PanelOpen) &&
            !(workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism);
        public string Status { get; private set; } = "轻点周边漂浮物，三个时段都可以收集素材。";
        public float FeedbackUntil {get;private set;}
        Rect BagButton => workspace && workspace.journal.IsEditing ? new Rect(1740, 354, 156, 48) : new Rect(1740, 164, 156, 48);
        GUIStyle label, title, button;
        Font font;
        float nextRefresh;
        int lastSetting=-1;
        Vector2 bagScroll;
        void Awake() { Instance = this; }
        IEnumerator Start()
        {
            if (!workspace) workspace = GetComponent<WorkspaceGameplayController>();
            while (!workspace || !workspace.Ready) yield return null;
            timeOfDay = FindFirstObjectByType<DreamTimeController>();
            var args=Environment.GetCommandLineArgs();
            LegacyNightSlice=args.Contains("--night-gameplay-validation")&&!args.Contains("--atelier-full-validation");
            // New installs start in the requested slice. Previously chosen lighting stays a preference.
            bool isolated = Environment.GetCommandLineArgs().Any(a => a.Contains("validation") || a.Contains("probe"));
            if (!isolated && !PlayerPrefs.HasKey("VintageJournal.DreamNight") && !PlayerPrefs.HasKey("VintageJournal.DreamDusk") && timeOfDay)
                timeOfDay.SetNight(true);
            if (!workspace.Service.RefreshSettingCollection(LegacyNightSlice?0:CurrentSetting,DateTime.UtcNow.Ticks, waitingCap, refreshBatch, refreshHours))
            { Status = workspace.Service.LastMessage; yield break; }
            collection = GetComponent<NightCollectionController>();
            if (!collection) collection = gameObject.AddComponent<NightCollectionController>();
            collection.workspace = workspace; collection.timeOfDay = timeOfDay;
            discovery=GetComponent<MaterialDiscoveryPresentation>();
            if(!discovery)discovery=gameObject.AddComponent<MaterialDiscoveryPresentation>();discovery.workspace=workspace;
            Ready = true;
            if(!LegacyNightSlice)workspace.Service.InitializeAtelierProgress(DateTime.UtcNow.Ticks);
            if(!GetComponent<AtelierUpgradeView>())gameObject.AddComponent<AtelierUpgradeView>().workspace=workspace;
            Debug.Log("[NightGameplay] Ready; waiting=" + Data.pending.Count + "; coins=" + Data.coins);
        }
        void Update()
        {
            if (!Ready || Time.unscaledTime < nextRefresh&&lastSetting==CurrentSetting) return;
            // Legacy paper previews and digital drags retain a whole-document rollback snapshot.
            // Wait until those edits finish before recording independent collection events.
            if (workspace.journal.HasPaperPreview || workspace.scrapbook.IsTransforming || AtelierHub.Instance&&AtelierHub.Instance.IsOpen) return;
            nextRefresh = Time.unscaledTime + 15;
            lastSetting=CurrentSetting;
            if(!LegacyNightSlice||CurrentSetting==0)workspace.Service.RefreshSettingCollection(LegacyNightSlice?0:CurrentSetting,DateTime.UtcNow.Ticks, waitingCap, refreshBatch, refreshHours);
            if(!LegacyNightSlice)workspace.Service.TickAtelierProgress(DateTime.Now);
        }
        public bool CanCollectDirection(Vector3 worldPoint) => CanCollect && workspace.journal.cameraRig.CanCollectDirection(worldPoint);
        public bool TryCatch(string tokenId,Vector3? worldPoint=null)
        {
            if (!CanCollect) return false;
            if(worldPoint.HasValue&&!CanCollectDirection(worldPoint.Value))return false;
            if(!worldPoint.HasValue&&collection&&collection.TryGetTokenPosition(tokenId,out var actualPoint)&&!CanCollectDirection(actualPoint))return false;
            var token = Data.pending.Find(t => t.tokenId == tokenId);
            if (token == null || token.setting!=CurrentSetting) return false;
            string name = NightMaterialCatalog.NameFor(token.assetId);
            bool ok = workspace.Service.CatchNightMaterial(tokenId);
            ShowCollectionFeedback(ok ? "获得额外素材：「" + name + "」 ×1 · 已放入素材袋" : workspace.Service.LastMessage);
            if (ok) Debug.Log("[NightGameplay] Caught:" + tokenId);
            return ok;
        }
        public void ShowCollectionFeedback(string message)
        {Status=message;FeedbackUntil=Time.unscaledTime+5;}
        public bool UseMaterial(string assetId)
        {
            if (!Ready || workspace.InteractionBusy || workspace.journal.IsBusy || workspace.journal.HasPaperPreview || workspace.scrapbook.IsTransforming) return false;
            var item = workspace.Service.Items.FirstOrDefault(i => i.assetId == assetId && i.owner == CraftOwner.Stock &&
                i.instanceId.StartsWith("dream-", StringComparison.Ordinal));
            if (item == null) { Status = "这份素材已经用完了，新的微光会再来。"; return false; }
            workspace.scrapbook.Cancel();
            if (!workspace.Service.BeginStock(item.instanceId)) { Status = workspace.Service.LastMessage; return false; }
            BagOpen = false; workspace.RefreshViews(); workspace.desk.ConsumePointer(); RuntimeInputGate.Consume();
            Status = "左键贴到本子 · Q/E 旋转 · [/] 缩放 · ESC 放回素材袋";
            return true;
        }
        public void OpenBag()
        {
            if (!Ready || workspace.InteractionBusy || workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism) return;
            if (CommissionDeskController.Instance && CommissionDeskController.Instance.PanelOpen) CommissionDeskController.Instance.ClosePanel();
            workspace.scrapbook.Cancel(); BagOpen = true; workspace.desk.ConsumePointer(); RuntimeInputGate.Consume();
        }
        public void CloseBag() { BagOpen = false; workspace?.desk.ConsumePointer(); RuntimeInputGate.Consume(); }
        public bool Contains(Vector2 pixel)
        {
            if (!Visible) return false;
            return BagOpen || (discovery&&discovery.Contains(pixel)) || BagButton.Contains(new Vector2(pixel.x * 1920 / Screen.width, (Screen.height - pixel.y) * 1080 / Screen.height));
        }
        bool Visible => Ready && workspace.desk.HasBegun && !workspace.desk.IsBlocking && !AtelierTutorial.Blocking && !(AtelierHub.Instance&&AtelierHub.Instance.IsOpen);
        void OnGUI()
        {
            if (!Visible) return;
            if (!font)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 22);
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 23, wordWrap = true }; label.normal.textColor = DaydreamPalette.Ink;
                title = new GUIStyle(label) { fontSize = 32 };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22, wordWrap = true };
                button.normal.textColor = button.hover.textColor = button.active.textColor = DaydreamPalette.Ink;
                button.normal.background = button.hover.background = button.active.background = Texture2D.whiteTexture;
            }
            var old = GUI.matrix; int depth = GUI.depth; var bg = GUI.backgroundColor;
            label.normal.textColor = title.normal.textColor = button.normal.textColor = button.hover.textColor = button.active.textColor = DaydreamPalette.Text(DaydreamPalette.Ink);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1)); GUI.depth = -140;
            GUI.backgroundColor = DaydreamPalette.Mint;
            if (!BagOpen) {
                if (GUI.Button(BagButton, "素材袋", button)) OpenBag();
                if(Time.unscaledTime<FeedbackUntil&&!(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)){
                    Fill(new Rect(548,750,1140,74),DaydreamPalette.Surface(DaydreamPalette.Cream));
                    GUI.Label(new Rect(570,766,1096,52),Status,label);
                }
            }
            else
            {
                Fill(new Rect(0, 0, 1920, 1080), new Color(.25f, .3f, .5f, .45f));
                DaydreamPalette.OuterGlow(new Rect(300, 200, 1320, 700));
                Fill(new Rect(300, 200, 1320, 700), DaydreamPalette.Surface(DaydreamPalette.Cream));
                Fill(new Rect(300, 200, 1320, 8), DaydreamPalette.Mint);
                GUI.Label(new Rect(342, 230, 900, 56), "三时段素材袋  ·  " + Data.coins + " 枚梦币", title);
                if (GUI.Button(new Rect(1425, 234, 150, 48), "关闭 ×", button)) CloseBag();
                GUI.Label(new Rect(342, 302, 1230, 70), "点击桌后的漂浮物收集。每个时段独立积攒，每份只能用一次；取消会放回，揭取可继续转移。", label);
                string[] assets = LegacyNightSlice?new[]{"night-glow-ink","night-star-sticker","night-lantern-paper","night-moonlit-star","night-forgotten-dream"}:NightMaterialCatalog.AllAssetIds;
                bagScroll=GUI.BeginScrollView(new Rect(332,380,1258,330),bagScroll,new Rect(0,0,1234,Mathf.CeilToInt(assets.Length/5f)*300));
                for (int i = 0; i < assets.Length; i++)
                {
                    string id = assets[i]; float x = 10 + i%5 * 246,y=i/5*300;
                    var definition = NightMaterialCatalog.Find(id);
                    Fill(new Rect(x, y, 228, 290), DaydreamPalette.Surface(DaydreamPalette.Card));
                    if (definition?.thumbnail) GUI.DrawTexture(new Rect(x + 34, y+18, 160, 138), definition.thumbnail, ScaleMode.ScaleToFit, true);
                    GUI.Label(new Rect(x + 10, y+162, 208, 62), NightMaterialCatalog.NameFor(id), label);
                    int count = workspace.Service.Items.Count(item => item.assetId == id && item.owner == CraftOwner.Stock && item.instanceId.StartsWith("dream-", StringComparison.Ordinal));
                    GUI.enabled = count > 0 && !workspace.InteractionBusy;
                    if (GUI.Button(new Rect(x + 12, y+240, 204, 48), "取用 · " + count + " 份", button)) UseMaterial(id);
                    GUI.enabled = true;
                }
                GUI.EndScrollView();
                bool night = timeOfDay && timeOfDay.IsNight;
                GUI.Label(new Rect(342, 724, 900, 86), night ?
                    "窗外还有 " + Data.pending.Count(t=>t.setting==CurrentSetting) + " 份素材在等你。每时段约每两小时补充，最多各保留 " + waitingCap + " 份。\n梦币可在工房升级中添置小物与创作工具。" :
                    AtelierProgressCatalog.SettingName(CurrentSetting)+"的漂浮物与客户正在到访。切换时段可以遇见不同的材料和朋友。", label);
                if (!night && timeOfDay && GUI.Button(new Rect(1270, 736, 300, 54), "看看黑夜的微光", button)) { timeOfDay.SetNight(true); CloseBag(); }
                GUI.Label(new Rect(342, 826, 1220, 52), Status, label);
            }
            GUI.matrix = old; GUI.depth = depth; GUI.backgroundColor = bg;
        }
        static void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        void OnDestroy() { if (Instance == this) Instance = null; if (font) Destroy(font); }
    }
}
