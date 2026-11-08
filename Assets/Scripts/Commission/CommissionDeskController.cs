using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public sealed class CommissionPageTarget
    {
        public CraftOwner owner;
        public string containerId, pageId, label;
        public Transform anchor;
        public Vector2 localSize, metres;
    }

    /// <summary>
    /// Floating mailbox, letter panel and postcard line. Reads input only through the workspace router
    /// (TryClick / ContainsPointer); all persistent changes go through CommissionDeskService.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class CommissionDeskController : MonoBehaviour
    {
        public static CommissionDeskController Instance { get; private set; }
        public WorkspaceGameplayController workspace;
        [Header("Visual bindings (replaceable art)")]
        public Collider mailboxCollider;
        public Transform flag, floatingEnvelope, mailboxRoot;
        public Renderer[] postcardFrames = Array.Empty<Renderer>();
        public TextMesh stampCounter;
        public int photoWidth = 420;

        public CommissionDeskService Service { get; private set; }
        public bool Ready => Service != null;
        public bool PanelOpen { get; private set; }
        public bool PanelCompact { get; private set; }
        public string ActiveLetterId { get; private set; }
        public string ViewingReplyId { get; private set; }
        public int SelectedTargetIndex { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public string PhotoFolder { get; private set; }

        readonly Dictionary<string, Texture2D> photos = new Dictionary<string, Texture2D>();
        Quaternion flagDown, flagUp;
        Vector3 envelopeHome;
        float pulse;
        bool visualsDirty = true;
        GUIStyle label, title, small, body, button, chip;
        Font font;
        DreamTimeController timeOfDay;

        void Awake() { Instance = this; nativeValidation = Environment.GetCommandLineArgs().Contains("--commission-native-validation"); }
        bool nativeValidation;
        readonly Dictionary<string, Rect> uiRects = new Dictionary<string, Rect>();

        /// <summary>Native-probe aid (F9, validation flag only): screen points of real targets and the last drawn UI rects.</summary>
        void LogTargets()
        {
            var lens = workspace.lens;
            var parts = new List<string> { "w=" + Screen.width, "h=" + Screen.height, "panel=" + (PanelOpen ? PanelCompact ? "compact" : ViewingReplyId != null ? "reply" : "full" : "closed") };
            string P(Vector3 world) { var s = lens.WorldToScreenPoint(world); return s.z > 0 ? (s.x / Screen.width).ToString("F4") + "," + (1 - s.y / Screen.height).ToString("F4") : "behind"; }
            if (mailboxCollider) parts.Add("mailbox=" + P(mailboxCollider.bounds.center));
            foreach (var t in Targets().Where(t => t.owner == CraftOwner.JournalPage))
                foreach (var uv in new[] { new Vector2(.45f, .55f), new Vector2(.55f, .35f) })
                    parts.Add(t.pageId + "@" + uv.x.ToString("F2") + "=" + P(t.anchor.TransformPoint(new Vector3(t.localSize.x * uv.x, t.localSize.y * uv.y, 0))));
            // Report only stickers a real click would reach: the camera ray through their centre must hit them first.
            // (Two instances can share an assetId; an occluded one would otherwise mislead native probes.)
            bool Reachable(PhysicalInteractable item, Vector3 world)
            {
                var s = lens.WorldToScreenPoint(world); if (s.z <= 0) return false;
                return Physics.Raycast(lens.ScreenPointToRay(s), out var hit, 100f, ~0, QueryTriggerInteraction.Collide) && hit.transform.IsChildOf(item.transform);
            }
            foreach (var item in FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(i => i.itemType == PhysicalItemType.Sticker && i.gameObject.activeInHierarchy))
            {
                var world = item.interactionCollider ? item.interactionCollider.bounds.center : item.transform.position;
                if (Reachable(item, world)) parts.Add(item.assetId + "=" + P(world));
            }
            foreach (var kv in uiRects) parts.Add("ui:" + kv.Key + "=" + (kv.Value.center.x / 1920f).ToString("F4") + "," + (kv.Value.center.y / 1080f).ToString("F4"));
            Debug.Log("[CommissionTargets] " + string.Join(";", parts));
        }
        void OnDestroy() { if (Instance == this) Instance = null; foreach (var t in photos.Values) if (t) Destroy(t); }

        System.Collections.IEnumerator Start()
        {
            if (!workspace) workspace = FindFirstObjectByType<WorkspaceGameplayController>();
            if (flag) { flagDown = flag.localRotation; flagUp = flagDown * Quaternion.Euler(0, 0, -80); }
            if (floatingEnvelope) envelopeHome = floatingEnvelope.localPosition;
            while (!workspace || !workspace.Ready) yield return null;
            CommissionCatalog catalog;
            try { catalog = CommissionCatalog.Load(); }
            catch (Exception error) { Status = error.Message; Debug.LogError("[Commission] " + error.Message); yield break; }
            var craft = workspace.Service;
            var args = Environment.GetCommandLineArgs();
            bool nightSlice = args.Contains("--atelier-full-validation") || args.Contains("--night-gameplay-validation") || !args.Any(a => a.Contains("validation") || a.Contains("probe"));
            timeOfDay = FindFirstObjectByType<DreamTimeController>();
            Service = new CommissionDeskService(catalog, () => craft.Document.commissionDesk, (mutate, label) => craft.CommitCommission(mutate, label),
                (owner, container, page) => craft.GetPageItems(owner, owner == CraftOwner.JournalPage ? null : container, page), nightSlice);
            bool legacyNight=Environment.GetCommandLineArgs().Contains("--night-gameplay-validation")&&!Environment.GetCommandLineArgs().Contains("--atelier-full-validation");
            Service.ProgressionEnabled=nightSlice&&!legacyNight;
            Service.ContentAvailable = !legacyNight||!timeOfDay || timeOfDay.IsNight;
            Service.ContentSetting=legacyNight?0:timeOfDay&&timeOfDay.IsNight?0:timeOfDay&&timeOfDay.IsDusk?2:1;
            Service.LetterArrived += l => { visualsDirty = true; Status = "邮筒里有一封新信：" + l.title; Debug.Log("[Commission] Arrived:" + l.letterId); };
            Service.ReplyArrived += l => { visualsDirty = true; Status = l.sender + " 回信了 · +" + l.stamps + " 枚邮票" +
                (!string.IsNullOrEmpty(l.clientId) ? " · +" + l.coins + " 金币与素材" : ""); Debug.Log("[Commission] Reply:" + l.letterId + ":tier" + l.tier); };
            craft.DataChanged += () => visualsDirty = true;
            string save = workspace.journal ? workspace.journal.SavePath : null;
            PhotoFolder = Path.Combine(string.IsNullOrEmpty(save) ? Application.persistentDataPath : Path.GetDirectoryName(save), "commission-photos");
            Directory.CreateDirectory(PhotoFolder);
            Debug.Log("[Commission] Ready: letters=" + Service.Data.letters.Count + " stamps=" + Service.Data.stamps + " photos=" + PhotoFolder);
        }

        bool Active => !AtelierTutorial.Blocking && Ready && workspace.desk && workspace.desk.HasBegun && !workspace.desk.IsBlocking &&
            !(AtelierHub.Instance&&AtelierHub.Instance.IsOpen) && !AtelierHub.PrivateMode &&
            !workspace.journal.HasPaperPreview && !(workspace.scrapbook && workspace.scrapbook.IsTransforming);

        void Update()
        {
            if (!Active) return;
            bool legacyNight=Environment.GetCommandLineArgs().Contains("--night-gameplay-validation")&&!Environment.GetCommandLineArgs().Contains("--atelier-full-validation");
            Service.ContentAvailable = !legacyNight||!timeOfDay || timeOfDay.IsNight;
            Service.ContentSetting=legacyNight?0:timeOfDay&&timeOfDay.IsNight?0:timeOfDay&&timeOfDay.IsDusk?2:1;
            if (Service.Tick(Time.unscaledDeltaTime)) visualsDirty = true;
            pulse += Time.unscaledDeltaTime;
            bool unread = Service.UnreadCount > 0;
            if (flag) flag.localRotation = Quaternion.Slerp(flag.localRotation, unread ? flagUp : flagDown, 1 - Mathf.Exp(-6 * Time.unscaledDeltaTime));
            if (floatingEnvelope)
            {
                floatingEnvelope.gameObject.SetActive(unread);
                floatingEnvelope.localPosition = envelopeHome + Vector3.up * (.012f * Mathf.Sin(pulse * 2.1f));
                floatingEnvelope.localRotation = Quaternion.Euler(0, 8 * Mathf.Sin(pulse * .9f), 6 * Mathf.Sin(pulse * 1.3f));
            }
            // While the player is handling material or drawers, the full letter shrinks to its checklist so storage stays reachable.
            bool busy = workspace.InteractionBusy;
            if (PanelOpen && !PanelCompact && ViewingReplyId == null && busy && ActiveLetterId != null) PanelCompact = true;
            if (visualsDirty) RefreshVisuals();
            if (nativeValidation && Input.GetKeyDown(KeyCode.F9)) LogTargets();
        }

        // ---------- Input seam (called only by WorkspaceInputRouter) ----------
        public bool TryClick(Ray ray)
        {
            if (!Active || !mailboxCollider || !mailboxCollider.enabled) return false;
            if (!mailboxCollider.Raycast(ray, out _, 10f)) return false;
            OpenPanel(); return true;
        }
        public bool ContainsPointer(Vector2 screen)
        {
            if (!Active) return false;
            var p = ToVirtual(screen);
            return PanelRect.Contains(p);
        }
        static Vector2 ToVirtual(Vector2 screen) => new Vector2(screen.x * 1920 / Screen.width, (Screen.height - screen.y) * 1080 / Screen.height);
        public Rect PanelRect => !PanelOpen ? new Rect(24, 196, 330, 64) : PanelCompact ? new Rect(24, 862, 430, 98) : new Rect(1376, 176, 520, 770);

        // ---------- Actions (shared by UI, router and validation) ----------
        public void OpenPanel()
        {
            if (!Ready) return;
            if (Service.Data.letters.Count == 0) Service.RequestLetterNow();
            PanelOpen = true; PanelCompact = false;
            if (ActiveLetterId == null || Service.Data.letters.All(l => l.letterId != ActiveLetterId || !l.IsOpen))
                ActiveLetterId = Service.OpenLetters.FirstOrDefault()?.letterId;
            var reply = Service.Replied.FirstOrDefault(l => !Service.ReplySeen(l.letterId));
            ViewingReplyId = reply?.letterId;
            Debug.Log("[Commission] Panel:open:" + (reply != null ? "reply:" + reply.letterId : "letter:" + ActiveLetterId));
            if (reply != null) Service.MarkReplySeen(reply.letterId);
            else if (ActiveLetterId != null) Service.MarkRead(ActiveLetterId);
            visualsDirty = true;
        }
        public void ClosePanel() { PanelOpen = false; ViewingReplyId = null; Debug.Log("[Commission] Panel:closed"); }
        public void SetCompact(bool compact) { PanelCompact = compact; }
        public void SelectLetter(string letterId) { ActiveLetterId = letterId; ViewingReplyId = null; Service.MarkRead(letterId); }

        public List<CommissionPageTarget> Targets()
        {
            var list = new List<CommissionPageTarget>();
            if (!Ready) return list;
            var journal = workspace.journal;
            if (journal && journal.pageView && journal.Document != null)
                foreach (JournalSide side in new[] { JournalSide.Left, JournalSide.Right })
                {
                    var page = journal.Document.GetPage(side); if (page == null) continue;
                    int index = Array.IndexOf(journal.Document.ActivePages, page);
                    list.Add(new CommissionPageTarget { owner = CraftOwner.JournalPage, containerId = "journal", pageId = page.pageId,
                        label = "手账 第" + (index + 1) + "页", anchor = journal.pageView.GetSurface(side), localSize = journal.pageView.SurfaceSize(side),
                        metres = workspace.JournalMetres(side) });
                }
            foreach (var view in workspace.releaseBooks.Where(v => v).OrderBy(v => v.bookId))
            {
                var book = workspace.Service.FindBook(view.bookId); if (book == null || !book.isOpen) continue;
                for (int side = 0; side < view.pageAnchors.Length; side++)
                {
                    int index = book.currentPage + side; var anchor = view.pageAnchors[side];
                    if (!anchor || index < 0 || index >= book.pages.Count) continue;
                    list.Add(new CommissionPageTarget { owner = CraftOwner.ReleaseBookPage, containerId = view.bookId, pageId = book.pages[index].pageId,
                        label = (view.bookId == "release-a" ? "离型本 A" : view.bookId == "release-b" ? "离型本 B" : view.bookId) + " 第" + (index + 1) + "页",
                        anchor = anchor, localSize = ReleaseBookView.LocalPageSize(anchor, view.pageSize), metres = view.pageSize });
                }
            }
            return list;
        }
        public CommissionPageTarget CurrentTarget()
        {
            var targets = Targets(); if (targets.Count == 0) return null;
            // Prefer the page the player last touched, else the fullest page.
            if (SelectedTargetIndex < 0 || SelectedTargetIndex >= targets.Count) SelectedTargetIndex = 0;
            return targets[SelectedTargetIndex];
        }
        public void SelectTarget(int index) { SelectedTargetIndex = index; }
        public bool SelectTarget(CraftOwner owner, string containerId, string pageId)
        {
            var targets = Targets(); int i = targets.FindIndex(t => t.owner == owner && t.pageId == pageId && (owner == CraftOwner.JournalPage || t.containerId == containerId));
            if (i >= 0) SelectedTargetIndex = i; return i >= 0;
        }
        void AutoTarget()
        {
            var targets = Targets(); if (targets.Count == 0 || !Ready) return;
            var selected = workspace.Service.GetItem(workspace.SelectedMaterialId ?? workspace.scrapbook?.SelectedId);
            if (selected != null && SelectTarget(selected.owner, selected.containerId, selected.pageId)) return;
            if (SelectedTargetIndex >= 0 && SelectedTargetIndex < targets.Count) return;
            SelectedTargetIndex = 0;
        }

        public CommissionEvaluation Preview()
        {
            var letter = Service?.Data.letters.FirstOrDefault(l => l.letterId == ActiveLetterId && l.IsOpen);
            var target = CurrentTarget();
            return letter == null || target == null ? null : Service.Evaluate(letter, target.owner, target.containerId, target.pageId);
        }

        public bool SendActive()
        {
            if (!Ready) return false;
            if (workspace.journal.HasPaperPreview || (workspace.scrapbook && workspace.scrapbook.IsTransforming))
            { Status = "请先完成或取消纸张预览和素材编辑，再寄出。"; return false; }
            if (workspace.InteractionBusy) { Status = "请先放下手里的素材，再寄出。"; return false; }
            var letter = Service.Data.letters.FirstOrDefault(l => l.letterId == ActiveLetterId && l.IsOpen);
            var target = CurrentTarget();
            if (letter == null) { Status = "先在邮筒里选一封信。"; return false; }
            if (target == null) { Status = "请打开手账或离型本，让要寄出的页面出现在桌上。"; return false; }
            var evaluation = Service.Evaluate(letter, target.owner, target.containerId, target.pageId);
            if (!evaluation.HasContent) { Status = "这一页还是空白的，先贴上一些素材吧。"; return false; }
            if (Service.AlreadySent(evaluation.signature)) { Status = "这一页已经原样寄出过了。换一页，或者再添点什么。"; return false; }
            string file = Path.Combine(PhotoFolder, letter.letterId + ".png");
            bool captured = CapturePage(target, file);
            bool sent = Service.Send(letter.letterId, target.owner, target.containerId, target.pageId, captured ? Path.GetFileName(file) : string.Empty);
            if (!sent && captured) { try { File.Delete(file); } catch (IOException) { } }
            Status = Service.LastMessage;
            if (sent)
            {
                Debug.Log("[Commission] Sent:" + letter.letterId + ":" + target.owner + ":" + target.pageId + ":tier" + evaluation.tier + ":photo=" + captured);
                ActiveLetterId = Service.OpenLetters.FirstOrDefault()?.letterId; PanelCompact = false; visualsDirty = true;
            }
            return sent;
        }

        /// <summary>Orthographic photo straight onto the page anchor; includes every rendered material on it.</summary>
        public bool CapturePage(CommissionPageTarget target, string file)
        {
            if (target == null || !target.anchor) return false;
            var go = new GameObject("Commission photo camera");
            RenderTexture rt = null; Texture2D tex = null; var previous = RenderTexture.active;
            try
            {
                var anchor = target.anchor;
                Vector3 centre = anchor.TransformPoint(new Vector3(target.localSize.x * .5f, target.localSize.y * .5f, 0));
                Vector3 up = anchor.up, normal = anchor.forward;
                var cam = go.AddComponent<Camera>(); cam.enabled = false; cam.orthographic = true;
                cam.orthographicSize = target.metres.y * .5f * 1.04f; cam.aspect = target.metres.x / target.metres.y;
                cam.nearClipPlane = .01f; cam.farClipPlane = .6f; cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = DaydreamPalette.Card;
                go.transform.SetPositionAndRotation(centre - normal * .25f, Quaternion.LookRotation(normal, up));
                int w = Mathf.Max(64, photoWidth), h = Mathf.RoundToInt(w * target.metres.y / target.metres.x);
                rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                File.WriteAllBytes(file, tex.EncodeToPNG());
                return true;
            }
            catch (Exception error) { Debug.LogWarning("[Commission] Photo capture failed: " + error.Message); return false; }
            finally
            {
                RenderTexture.active = previous;
                if (tex) Destroy(tex);
                if (rt) { rt.Release(); Destroy(rt); }
                Destroy(go);
            }
        }

        Texture2D Photo(CommissionLetterState letter)
        {
            if (letter == null || string.IsNullOrEmpty(letter.photoFile) || string.IsNullOrEmpty(PhotoFolder)) return null;
            if (photos.TryGetValue(letter.letterId, out var cached) && cached) return cached;
            string path = Path.Combine(PhotoFolder, letter.photoFile);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) { Destroy(tex); return null; }
            tex.wrapMode = TextureWrapMode.Clamp; photos[letter.letterId] = tex; return tex;
        }

        void RefreshVisuals()
        {
            visualsDirty = false; if (!Ready) return;
            var sent = Service.Data.letters.Where(l => !l.IsOpen).OrderByDescending(l => l.sequence).ToArray();
            for (int i = 0; i < postcardFrames.Length; i++)
            {
                var frame = postcardFrames[i]; if (!frame) continue;
                var letter = i < sent.Length ? sent[i] : null; var photo = Photo(letter);
                frame.transform.parent.gameObject.SetActive(letter != null);
                if (letter == null) continue;
                var block = new MaterialPropertyBlock(); frame.GetPropertyBlock(block);
                if (photo) { block.SetTexture("_BaseMap", photo); block.SetTexture("_MainTex", photo); }
                frame.SetPropertyBlock(block);
            }
            if (stampCounter) stampCounter.text = "邮票 × " + Service.Data.stamps;
        }

        // ---------- IMGUI letter panel (daydream skin) ----------
        void Styles()
        {
            if (font) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "Arial" }, 20);
            label = S(20); title = S(26); small = S(16); body = S(19); chip = S(20);
            title.fontStyle = FontStyle.Bold; title.normal.textColor = DaydreamPalette.StarBlue; small.normal.textColor = DaydreamPalette.Muted; body.wordWrap = true;
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18, wordWrap = true };
            button.normal.background = button.hover.background = button.active.background = Texture2D.whiteTexture;
            button.normal.textColor = button.hover.textColor = button.active.textColor = DaydreamPalette.Ink;
        }
        GUIStyle S(int size) { var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, wordWrap = true }; s.normal.textColor = DaydreamPalette.Ink; return s; }
        static void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
        static void Card(Rect r, Color c)
        {
            Fill(new Rect(r.x + 3, r.y + 5, r.width, r.height), DaydreamPalette.Shadow * new Color(1, 1, 1, 1 - DaydreamPalette.Night));
            DaydreamPalette.OuterGlow(r, .7f); c = DaydreamPalette.Surface(c);
            Fill(new Rect(r.x + 6, r.y, r.width - 12, r.height), c); Fill(new Rect(r.x, r.y + 6, r.width, r.height - 12), c);
        }
        bool Btn(Rect r, string text, Color tint)
        {
            if (Event.current.type == EventType.Repaint) uiRects[UiKey(text)] = r;
            var old = GUI.backgroundColor; GUI.backgroundColor = tint; bool hit = GUI.Button(r, text, button); GUI.backgroundColor = old;
            if (hit) Debug.Log("[CommissionUI] Click:" + UiKey(text));
            return hit;
        }
        static string UiKey(string text) => text.Replace("● ", "").Split('（')[0];

        void OnGUI()
        {
            if (!Active) return;
            if (DreamGameplayController.Instance && DreamGameplayController.Instance.BagOpen) return;
            Styles();
            { var ink = DaydreamPalette.Text(DaydreamPalette.Ink); label.normal.textColor = body.normal.textColor = chip.normal.textColor = button.normal.textColor = button.hover.textColor = button.active.textColor = ink; title.normal.textColor = DaydreamPalette.Text(DaydreamPalette.StarBlue); }
            var oldMatrix = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
            try
            {
                if (!PanelOpen) DrawChip();
                else if (PanelCompact) DrawCompact();
                else DrawFull();
            }
            finally { GUI.matrix = oldMatrix; }
        }

        void DrawChip()
        {
            var r = new Rect(24, 196, 330, 64); int unread = Service.UnreadCount;
            Card(r, unread > 0 ? DaydreamPalette.Peach : DaydreamPalette.Cream);
            if (Event.current.type == EventType.Repaint) uiRects["chip"] = r;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { Debug.Log("[CommissionUI] Click:chip"); OpenPanel(); }
            GUI.Label(new Rect(r.x + 22, r.y + 16, 200, 34), unread > 0 ? "✉ 新信件 × " + unread : "✉ 委托信", label);
            GUI.Label(new Rect(r.x + 214, r.y + 18, 110, 30), "邮票 " + Service.Data.stamps, small);
        }

        void DrawCompact()
        {
            var r = PanelRect; Card(r, DaydreamPalette.Cream); DaydreamPalette.Grid(r);
            var letter = Service.Data.letters.FirstOrDefault(l => l.letterId == ActiveLetterId && l.IsOpen);
            GUI.Label(new Rect(r.x + 22, r.y + 14, 330, 34), letter != null ? letter.title : "暂时没有待回应的信", title);
            if (Btn(new Rect(r.xMax - 128, r.y + 14, 106, 38), "展开信", DaydreamPalette.Sky)) PanelCompact = false;
            if (letter != null) GUI.Label(new Rect(r.x+22,r.y+58,r.width-44,30),!string.IsNullOrEmpty(letter.clientId) ? ChapterLabel(letter) : "把你的心情留在纸上，随时可以寄出。",small);
        }

        void DrawFull()
        {
            var r = PanelRect; Card(r, DaydreamPalette.Cream); DaydreamPalette.Grid(r);
            Fill(new Rect(r.x, r.y, r.width, 8), DaydreamPalette.Mint);
            GUI.Label(new Rect(r.x + 24, r.y + 20, 300, 36), "云上邮局", title);
            GUI.Label(new Rect(r.x + 166, r.y + 28, 128, 28), "邮票 × " + Service.Data.stamps, small);
            if (Btn(new Rect(r.xMax - 212, r.y + 18, 112, 38), "开始创作", DaydreamPalette.Sky)) { ClosePanel(); return; };
            if (Btn(new Rect(r.xMax - 92, r.y + 18, 68, 38), "×", DaydreamPalette.Card)) { ClosePanel(); return; }

            // Letter tabs: open letters, then latest reply.
            float x = r.x + 24, y = r.y + 70;
            foreach (var l in Service.OpenLetters.Take(3))
            {
                bool on = l.letterId == ActiveLetterId && ViewingReplyId == null;
                string shortName = l.clientId == "moth" ? "飞蛾馆员" : l.sender;
                if (Btn(new Rect(x, y, 150, 44), (l.status == CommissionLetterStatus.Unread ? "● " : "") + shortName, on ? DaydreamPalette.Mint : DaydreamPalette.Card)) SelectLetter(l.letterId);
                x += 158;
            }
            var replies = Service.Replied.Take(1).ToArray();
            if (replies.Length > 0 && x < r.xMax - 120)
                if (Btn(new Rect(x, y, Mathf.Min(150, r.xMax - 24 - x), 44), "回信", ViewingReplyId != null ? DaydreamPalette.Peach : DaydreamPalette.Card))
                { ViewingReplyId = replies[0].letterId; Service.MarkReplySeen(ViewingReplyId); }

            y += 62;
            if (ViewingReplyId != null) { DrawReply(new Rect(r.x + 24, y, r.width - 48, r.yMax - y - 20)); return; }
            var letter = Service.Data.letters.FirstOrDefault(l => l.letterId == ActiveLetterId && l.IsOpen);
            if (letter == null)
            {
                int waiting = Service.WaitingReplies.Count();
                GUI.Label(new Rect(r.x + 24, y, r.width - 48, 120), waiting > 0 ? "作品已寄出，正在等 " + waiting + " 封回信。\n朋友读完后，故事会继续。" :
                    Service.NightClientsEnabled && !Service.ContentAvailable ? "灯塔守望者和飞蛾馆员在夜晚来信。\n在设置中选择黑夜，等星光亮起来。" : "邮筒正在等下一封信……", body);
                if (Btn(new Rect(r.x + 24, y + 130, 220, 46), "看看有没有新信", DaydreamPalette.Sky)) { if (Service.RequestLetterNow()) { ActiveLetterId = Service.OpenLetters.FirstOrDefault()?.letterId; } Status = Service.LastMessage; }
                DrawStatus(r); return;
            }
            Card(new Rect(r.x + 24, y, r.width - 48, 270), DaydreamPalette.Card);
            GUI.Label(new Rect(r.x + 44, y + 14, r.width - 88, 34), letter.title, title);
            GUI.Label(new Rect(r.x + 44, y + 52, r.width - 88, 150), letter.body, body);
            GUI.Label(new Rect(r.x + 44, y + 210, r.width - 88, 46), "—— " + letter.sender + "\n" + ChapterLabel(letter), small);
            y += 286;
            DrawChecklist(new Rect(r.x + 24, y, r.width - 48, 110));
            y += 120;
            DrawSendRow(new Rect(r.x + 24, y, r.width - 48, 140), letter);
            DrawStatus(r);
        }

        void DrawChecklist(Rect r)
        {
            AutoTarget();
            var letter = Service.Data.letters.FirstOrDefault(l => l.letterId == ActiveLetterId && l.IsOpen); if (letter == null) return;
            GUI.Label(new Rect(r.x, r.y, r.width, 50), string.IsNullOrEmpty(letter.twistBrief) ? "按你自己的理解创作。每一页完成的心意，都会被接住。" : letter.twistBrief + "\n只是邀请，随心创作也会被接住。", small);
            GUI.Label(new Rect(r.x, r.y + 60, r.width, 46), "寄出的是照片，作品和素材留在你的本子里。\n情绪没有标准答案，回信会告诉你朋友喜欢的细节。", small);
        }

        void DrawSendRow(Rect r, CommissionLetterState letter)
        {
            var targets = Targets();
            if (targets.Count == 0) { GUI.Label(new Rect(r.x, r.y, r.width, 50), "打开手账或离型本，要寄出的页面会出现在这里。", small); return; }
            if (SelectedTargetIndex >= targets.Count) SelectedTargetIndex = 0;
            var target = targets[SelectedTargetIndex];
            if (Btn(new Rect(r.x, r.y, 44, 44), "‹", DaydreamPalette.Card)) SelectedTargetIndex = (SelectedTargetIndex + targets.Count - 1) % targets.Count;
            GUI.Label(new Rect(r.x + 54, r.y + 8, r.width - 170, 32), "寄出页：" + target.label, label);
            if (Btn(new Rect(r.xMax - 106, r.y, 44, 44), "›", DaydreamPalette.Card)) SelectedTargetIndex = (SelectedTargetIndex + 1) % targets.Count;
            GUI.enabled = letter != null && !workspace.InteractionBusy;
            if (Btn(new Rect(r.x, r.y + 52, r.width, 50), "拍照寄出（作品留在本子里）", DaydreamPalette.Peach)) SendActive();
            GUI.enabled = true;
        }

        void DrawReply(Rect r)
        {
            var letter = Service.Data.letters.FirstOrDefault(l => l.letterId == ViewingReplyId); if (letter == null) return;
            Card(new Rect(r.x, r.y, r.width, 560), DaydreamPalette.Card);
            GUI.Label(new Rect(r.x + 20, r.y + 14, r.width - 40, 34), "来自 " + letter.sender + " 的回信", title);
            GUI.Label(new Rect(r.x + 20, r.y + 54, r.width - 40, 150), letter.replyText, body);
            string rewards = "邮票 +" + letter.stamps;
            if (!string.IsNullOrEmpty(letter.clientId)) rewards += "  ·  金币 +" + letter.coins + "\n" + RewardName(letter.rewardAssetId) + " × " + letter.rewardCount +
                (letter.rareRewardCount > 0 ? "  ·  遗忘的梦 × " + letter.rareRewardCount : "") + "\n" + (letter.afterStory ? "故事之后，也欢迎继续来信。" : "故事 " + letter.storyChapter + " / " + letter.storyLength + " 已收藏");
            GUI.Label(new Rect(r.x + 20, r.y + 210, r.width - 40, 88), rewards, small);
            var photo = Photo(letter);
            if (photo) GUI.DrawTexture(new Rect(r.x + 20, r.y + 300, r.width - 40, 234), photo, ScaleMode.ScaleToFit, true);
            if (Btn(new Rect(r.x, r.y + 568, 220, 46), "继续看委托", DaydreamPalette.Mint)) { ViewingReplyId = null; ActiveLetterId = Service.OpenLetters.FirstOrDefault()?.letterId; }
        }

        static string ChapterLabel(CommissionLetterState letter) => string.IsNullOrEmpty(letter.clientId) ? "" : letter.clientId=="daily"?"今日的一封信":letter.clientId=="season-guest"?"季节旅人的来信":letter.afterStory ? "故事之后的来信" : "故事 " + letter.storyChapter + " / " + letter.storyLength;
        static string RewardName(string assetId) => assetId == "night-lantern-paper" ? "灯笼纸" : assetId == "night-glow-ink" ? "微光墨迹" : "夜间素材";

        void DrawStatus(Rect r)
        {
            string text = !string.IsNullOrEmpty(Status) ? Status : Service.LastMessage;
            if (!string.IsNullOrEmpty(text)) GUI.Label(new Rect(r.x + 24, r.yMax - 48, r.width - 48, 34), text, small);
        }
    }
}
