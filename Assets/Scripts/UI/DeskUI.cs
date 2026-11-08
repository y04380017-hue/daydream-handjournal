using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Resolution-independent paper-and-brass UI. Catalog items remain data, not scene objects.</summary>
    [DisallowMultipleComponent]
    public sealed class DeskUI : MonoBehaviour
    {
        public DeskCamera cameraRig;
        public DeskInteraction interaction;
        public string CurrentHover { get; set; } = string.Empty;
        public string CurrentPanel { get; private set; } = "start";
        public bool HasBegun { get; private set; }
        public bool IsBlocking => !HasBegun || CurrentPanel != "desk";
        public bool SceneInputAllowed => !IsBlocking && nativePointerArmed;
        public ItemCatalog Catalog { get; private set; }
        public CatalogItem SelectedItem { get; private set; }
        public int VisibleItemCount => visible.Count;

        private readonly List<CatalogItem> visible = new List<CatalogItem>();
        private Vector2 scroll;
        private Font bodyFont;
        private GUIStyle title, heading, body, small, eyebrow, button, itemName;
        private bool stylesReady, reducedQuality;
        private float volume = .7f;
        private string previousPanel = "desk";
        JournalController journal;
        ScrapbookController scrapbook;ScrapbookInput scrapbookInput;
        private bool nativePointerArmed;
        private DreamTimeController dreamTime;
        private int pointerReadyAfterFrame;
        private readonly Color paper = DaydreamPalette.Paper;
        private readonly Color cream = DaydreamPalette.Card;
        private readonly Color ink = DaydreamPalette.Ink;
        private readonly Color muted = DaydreamPalette.Muted;
        private readonly Color brass = DaydreamPalette.SkyDeep;
        private readonly Color sage = DaydreamPalette.MintDeep;

        private void Awake()
        {
            Catalog = ItemCatalog.Load();
            dreamTime = FindFirstObjectByType<DreamTimeController>();
            journal=FindFirstObjectByType<JournalController>();
            scrapbook=FindFirstObjectByType<ScrapbookController>();scrapbookInput=FindFirstObjectByType<ScrapbookInput>();
            if (!cameraRig) cameraRig = FindFirstObjectByType<DeskCamera>();
            volume = PlayerPrefs.GetFloat("VintageJournal.Volume", .7f);
            AudioListener.volume = volume;
            QualitySettings.vSyncCount = PlayerPrefs.GetInt("VintageJournal.VSync", 1);
            Application.targetFrameRate = 60;
            Debug.Log("[Desk] UI:Start; catalog=" + Catalog.items.Length);
        }

        private void Update()
        {
            if (!RuntimeInputGate.BlocksLegacyInput && !RuntimeInputGate.ConsumedThisFrame && (!journal || !journal.IsEditing) && Input.GetKeyDown(KeyCode.Escape)) HandleBack();
            // OnGUI and Update observe the same native mouse sample in different
            // parts of a frame. A panel disappearing must never expose that sample
            // to the scene. Re-arm only on a later, fully released, edge-free frame.
            if (!nativePointerArmed && !IsBlocking && Application.isFocused &&
                Time.frameCount > pointerReadyAfterFrame &&
                !Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonUp(0) &&
                !Input.GetMouseButton(1) && !Input.GetMouseButtonDown(1) && !Input.GetMouseButtonUp(1) &&
                !Input.GetMouseButton(2) && !Input.GetMouseButtonDown(2) && !Input.GetMouseButtonUp(2))
                nativePointerArmed = true;
        }

        public void ConsumePointer()
        {
            nativePointerArmed = false;
            pointerReadyAfterFrame = Time.frameCount + 1;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) ConsumePointer(); }

        public void Begin()
        {
            ConsumePointer();
            HasBegun = true;
            CurrentPanel = "desk";
            Debug.Log("[Desk] UI:Begin");
            if (interaction) interaction.LogVisibleTargets();
        }

        public void ShowCatalog(DeskItemKind kind)
        {
            if (!HasBegun) return;
            string category = kind == DeskItemKind.Tape ? "tape"
                : kind == DeskItemKind.Sticker ? "sticker"
                : kind == DeskItemKind.Pages ? "page" : kind==DeskItemKind.Note?"note":"decoration";
            OpenCategory(category);
        }

        public void OpenCategory(string category)
        {
            if (!HasBegun) return;
            ConsumePointer();
            CurrentPanel = category;
            CurrentHover = string.Empty;
            visible.Clear();
            foreach (var item in Catalog.items)
                if (item != null && item.category == category) visible.Add(item);
            SelectedItem = visible.Count > 0 ? visible[0] : null;
            scroll = Vector2.zero;
            Debug.Log("[Desk] UI:Catalog:" + category + "; count=" + visible.Count);
            if(scrapbook)scrapbook.NotifyDeskCatalog(category);
        }

        public void SelectItem(int index)
        {
            if (index < 0 || index >= visible.Count) return;
            SelectedItem = visible[index];
            Debug.Log("[Desk] UI:Select:" + SelectedItem.id);
        }

        public void OpenSettings()
        {
            ConsumePointer();
            previousPanel = CurrentPanel;
            CurrentPanel = "settings";
            CurrentHover = string.Empty;
            Debug.Log("[Desk] UI:Settings");
        }

        public void Close()
        {
            if(scrapbook)scrapbook.CloseBrowser();
            ConsumePointer();
            CurrentPanel = HasBegun ? "desk" : "start";
            CurrentHover = string.Empty;
            Debug.Log("[Desk] UI:Close");
        }

        public void HandleBack()
        {
            ConsumePointer();
            if (journal && journal.IsEditing)
            {
                if(journal.HasPaperPreview)journal.CancelPaper();else journal.ExitEditing();
                return;
            }
            if (CurrentPanel == "settings")
            {
                CurrentPanel = previousPanel;
                Debug.Log("[Desk] UI:CloseSettings");
            }
            else if (HasBegun && CurrentPanel != "desk") Close();
            else if (HasBegun && cameraRig && cameraRig.IsInspecting) cameraRig.Return();
        }

        public bool IsPointerOverInterface(Vector2 screenPosition)
        {
            if (IsBlocking || (AtelierTutorial.Instance && AtelierTutorial.Instance.Contains(screenPosition))) return true;
            Vector2 p = new Vector2(screenPosition.x * 1920f / Screen.width,
                (Screen.height - screenPosition.y) * 1080f / Screen.height);
            if (new Rect(24, 24, 530, 78).Contains(p) || new Rect(1740, 24, 156, 66).Contains(p)) return true;
            if (new Rect(24, 984, 712, 70).Contains(p)) return true;
            if (cameraRig && cameraRig.IsInspecting && new Rect(24, 118, 205, 62).Contains(p)) return true;
            return false;
        }

        private void PrepareStyles()
        {
            if (stylesReady) return;
            string[] available = Font.GetOSInstalledFontNames();
            string chosen = "Arial";
            foreach (string candidate in new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Noto Sans CJK SC" })
                if (System.Array.IndexOf(available, candidate) >= 0) { chosen = candidate; break; }
            bodyFont = Font.CreateDynamicFontFromOSFont(chosen, 22);
            title = Style(58, ink, FontStyle.Normal);
            heading = Style(31, ink, FontStyle.Normal);
            body = Style(21, ink);
            small = Style(17, muted);
            eyebrow = Style(15, brass, FontStyle.Bold);
            button = Style(21, ink);
            button.alignment = TextAnchor.MiddleCenter;
            itemName = Style(19, ink);
            itemName.alignment = TextAnchor.MiddleCenter;
            stylesReady = true;
        }

        private GUIStyle Style(int size, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { font = bodyFont, fontSize = size, fontStyle = fontStyle,
                wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0) };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private void OnGUI()
        {
            if(RuntimeInputGate.BlocksLegacyInput)return;
            if (journal && journal.IsEditing) return;
            PrepareStyles();
            var inkNow = DaydreamPalette.Text(ink);   // night text #3B3F6E keeps >= 4.5:1 on the night panel
            title.normal.textColor = heading.normal.textColor = body.normal.textColor = itemName.normal.textColor = inkNow;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
            GUI.color = Color.white;
            if (CurrentPanel == "start") DrawStart();
            else if (CurrentPanel == "settings") DrawSettings();
            else
            {
                DrawDeskChrome();
                if (CurrentPanel != "desk") DrawCatalog();
            }
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }

        private void DrawStart()
        {
            Fill(new Rect(0, 0, 1920, 1080), DaydreamPalette.Veil);
            Paper(new Rect(112, 150, 598, 760));
            Label(162, 193, 490, 30, "DAYDREAM   /   FLOATING ATELIER", eyebrow);
            Line(162, 246, 498, brass);
            Label(159, 279, 500, 170, "白日梦手作房", title);
            Label(164, 401, 440, 60, "在云上的书桌，回一封信。", heading);
            Label(164, 504, 443, 100, "邮筒里已经有人在等你的作品。\n挑一卷胶带，从第一页开始吧。", body);
            if (Button(new Rect(163, 645, 496, 68), "坐到桌前", true)) Begin();
            if (Button(new Rect(163, 732, 496, 58), "设置")) OpenSettings();
            Label(164, 833, 465, 28, "创作手账 · 悬浮委托信", small);
            Label(1470, 1000, 395, 28, "白日梦里的手作时光。", small);
        }

        private void DrawDeskChrome()
        {
            Paper(new Rect(24, 24, 530, 78), false);
            Label(48, 38, 270, 38, "白日梦手作房", heading);
            Label(310, 54, 220, 25, "JOURNAL ATELIER", eyebrow);
            if (Button(new Rect(1740, 24, 156, 66), "设置")) OpenSettings();
            if (cameraRig && cameraRig.IsInspecting)
            {
                if (Button(new Rect(24, 118, 205, 62), "← 返回桌面")) HandleBack();
            }
            if (CurrentPanel != "desk") return;
            bool hover = !string.IsNullOrEmpty(CurrentHover);
            float width = hover ? 530 : 712;
            // Night: the hint bar rests at 60 % opacity until the pointer is over it.
            var bar = new Rect(24, 984, width, 70); var oldTint = GUI.color;
            GUI.color = new Color(1, 1, 1, bar.Contains(Event.current.mousePosition) ? 1 : Mathf.Lerp(1, .6f, DaydreamPalette.Night));
            Paper(bar, false);
            Label(48, 1002, width - 48, 34,
                hover ? CurrentHover + "   ·   点击查看" : "点击粉彩邮筒读信   ·   点击手账本编辑   ·   ESC 返回", hover ? body : small);
            GUI.color = oldTint;
            Label(1690, 1012, 205, 25, "ABOVE THE CLOUDS", eyebrow);
        }

        private void DrawCatalog()
        {
            Fill(new Rect(0, 0, 1920, 1080), new Color(.80f, .88f, .95f, .55f));
            Paper(new Rect(120, 110, 1680, 858));
            Label(160, 142, 900, 47, CategoryName(CurrentPanel) + "   /   素材收藏", heading);
            Label(160, 194, 1020, 27, "挑一份喜欢的花纹，让今天的灵感慢慢浮现。", small);
            if (Button(new Rect(1607, 139, 150, 52), "返回  ×")) { HandleBack(); return; }
            string[] categories = { "tape", "sticker", "page", "note", "decoration" };
            for (int i = 0; i < categories.Length; i++)
            {
                string category = categories[i];
                if (Button(new Rect(160 + i * 204, 238, 189, 48), CategoryName(category), CurrentPanel == category))
                    OpenCategory(category);
            }
            Line(160, 304, 1596, new Color(brass.r, brass.g, brass.b, .45f));
            const int columns = 4;
            int rows = Mathf.CeilToInt(visible.Count / (float)columns);
            scroll = GUI.BeginScrollView(new Rect(155, 324, 1122, 566), scroll,
                new Rect(0, 0, 1092, Mathf.Max(560, rows * 220)), false, false);
            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i];
                var r = new Rect((i % columns) * 273 + 5, (i / columns) * 220, 254, 203);
                bool selected = item == SelectedItem;
                Fill(r, selected ? DaydreamPalette.Mint : cream);
                Outline(r, selected ? sage : new Color(.55f, .70f, .80f, .38f), selected ? 2 : 1);
                if (item.Thumbnail) GUI.DrawTexture(new Rect(r.x + 13, r.y + 11, r.width - 26, 147), item.Thumbnail, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(r.x + 12, r.y + 169, r.width - 24, 28), item.name, itemName);
                bool usable=scrapbookInput&&(item.category=="tape"||item.category=="sticker"||item.category=="note");
                if(usable&&Event.current.type==EventType.MouseDown&&Event.current.button==0&&r.Contains(Event.current.mousePosition))
                {SelectItem(i);scrapbookInput.StartAssetDrag(item.id);Event.current.Use();break;}
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) SelectItem(i);
            }
            GUI.EndScrollView();
            if (visible.Count == 0) Label(210, 435, 780, 70, "这一格收藏暂时空着。", body);
            Fill(new Rect(1302, 326, 454, 566), cream);
            Outline(new Rect(1302, 326, 454, 566), new Color(brass.r, brass.g, brass.b, .4f));
            if (SelectedItem != null)
            {
                if (SelectedItem.Thumbnail) GUI.DrawTexture(new Rect(1330, 350, 398, 295), SelectedItem.Thumbnail, ScaleMode.ScaleToFit, true);
                Label(1330, 672, 398, 50, SelectedItem.name, heading);
                Line(1330, 735, 398, new Color(brass.r, brass.g, brass.b, .4f));
                Label(1330, 756, 398, 82, string.IsNullOrEmpty(SelectedItem.description)
                    ? "一份温柔的复古收藏，可在后续手账创作中使用。" : SelectedItem.description, body);
                Label(1330, 850, 398, 27, "编号  " + SelectedItem.id, small);
            }
            Label(160, 918, 1530, 27, "共 " + visible.Count + " 款   ·   点击缩略图查看细节   ·   ESC 关闭   ·   本阶段为素材预览", small);
        }

        private void DrawSettings()
        {
            Fill(new Rect(0, 0, 1920, 1080), new Color(.80f, .88f, .95f, .55f));
            Paper(new Rect(535, 185, 850, 706));
            Label(589, 229, 650, 50, "让午后更合心意", heading);
            Label(590, 294, 650, 28, "SETTINGS  /  个人偏好", eyebrow);
            Line(590, 343, 740, brass);
            Label(590, 380, 650, 35, "音量  " + Mathf.RoundToInt(volume * 100) + "%", body);
            float nextVolume = GUI.HorizontalSlider(new Rect(590, 438, 738, 25), volume, 0, 1);
            if (!Mathf.Approximately(nextVolume, volume))
            {
                volume = nextVolume;
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat("VintageJournal.Volume", volume);
            }
            Label(590, 479, 650, 30, "画面质量", body);
            if (Button(new Rect(590, 527, 350, 54), "精致阴影", !reducedQuality)) SetQuality(false);
            if (Button(new Rect(978, 527, 350, 54), "流畅优先", reducedQuality)) SetQuality(true);
            bool sync = QualitySettings.vSyncCount > 0;
            if (Button(new Rect(590, 608, 738, 54), sync ? "垂直同步：开" : "垂直同步：关"))
            {
                QualitySettings.vSyncCount = sync ? 0 : 1;
                PlayerPrefs.SetInt("VintageJournal.VSync", QualitySettings.vSyncCount);
                Debug.Log("[Desk] Settings:VSync=" + QualitySettings.vSyncCount);
            }
            if (dreamTime)
            {
                Label(590, 680, 737, 28, "窗外的时间 · 柔和过渡，自动记住选择", small);
                if (Button(new Rect(590, 720, 236, 50), "白天 · 云海", !dreamTime.IsNight && !dreamTime.IsDusk)) dreamTime.SetNight(false);
                if (Button(new Rect(841, 720, 236, 50), "黄昏 · 粉橙", dreamTime.IsDusk)) dreamTime.SetDusk(true);
                if (Button(new Rect(1092, 720, 236, 50), "黑夜 · 夜航邮路", dreamTime.IsNight)) dreamTime.SetNight(true);
            }
            if (Button(new Rect(590, 795, 738, 57), "保存并返回", true)) { PlayerPrefs.Save(); HandleBack(); }
        }

        private void SetQuality(bool low)
        {
            reducedQuality = low;
            QualitySettings.shadows = low ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowResolution = low ? ShadowResolution.Medium : ShadowResolution.High;
            QualitySettings.shadowDistance = low ? 12 : 25;
            // URP reads per-light shadow modes even when pipeline-owned atlas settings
            // override the legacy QualitySettings fields.
            foreach (var sceneLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (sceneLight.shadows != LightShadows.None)
                    sceneLight.shadows = low ? LightShadows.Hard : LightShadows.Soft;
            Debug.Log("[Desk] Settings:Quality=" + (low ? "Performance" : "Detailed"));
        }

        private static string CategoryName(string category)
        {
            switch (category)
            {
                case "tape": return "和纸胶带";
                case "sticker": return "复古贴纸";
                case "page": return "手账内页";
                case "note": return "纸间便签";
                default: return "装饰小物";
            }
        }

        private void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void Outline(Rect r, Color color, float thickness = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, thickness), color);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Fill(new Rect(r.x, r.y, thickness, r.height), color);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        private void Paper(Rect r, bool inset = true)
        {
            Fill(new Rect(r.x + 4, r.y + 6, r.width, r.height), DaydreamPalette.Shadow * new Color(1, 1, 1, 1 - DaydreamPalette.Night));
            DaydreamPalette.OuterGlow(r);
            Fill(r, DaydreamPalette.Surface(paper));
            Outline(r, DaydreamPalette.Border(new Color(brass.r, brass.g, brass.b, .78f)));
            if (inset) Outline(new Rect(r.x + 9, r.y + 9, r.width - 18, r.height - 18), new Color(brass.r, brass.g, brass.b, .22f));
            // Faint square grid (studio tiles / sky-grid reference) instead of horizontal grain; large panels only.
            if (inset) DaydreamPalette.Grid(new Rect(r.x + 9, r.y + 9, r.width - 18, r.height - 18), 26f);
        }

        private void Line(float x, float y, float width, Color color) { Fill(new Rect(x, y, width, 1), color); }
        private void Label(float x, float y, float width, float height, string text, GUIStyle style)
        { GUI.Label(new Rect(x, y, width, height), text, style); }

        private bool Button(Rect r, string text, bool primary = false)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            float night = DaydreamPalette.Night;
            Color background = primary ? sage : DaydreamPalette.Surface(cream);
            // Day: hover tints the button. Night: hover breathes a faint glow instead of flipping colour.
            if (hover) background = Color.Lerp(background, Color.Lerp(background, brass, .15f), 1 - night);
            if (hover && night > .01f) DaydreamPalette.OuterGlow(r, 1.4f + .6f * Mathf.Sin(Time.unscaledTime * 2 * Mathf.PI / 1.8f));
            Fill(r, background);
            Outline(r, primary ? sage : DaydreamPalette.Border(new Color(brass.r, brass.g, brass.b, .7f)));
            var labelColor = primary ? cream : DaydreamPalette.Text(ink);
            button.normal.textColor = labelColor;
            button.hover.textColor = labelColor;
            button.active.textColor = labelColor;
            button.focused.textColor = labelColor;
            bool clicked = GUI.Button(r, text, button);
            if (clicked) ConsumePointer();
            return clicked;
        }

        private void OnApplicationQuit() { PlayerPrefs.Save(); }
    }
}
