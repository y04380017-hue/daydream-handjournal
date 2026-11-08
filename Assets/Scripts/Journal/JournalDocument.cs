using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public enum JournalSide { Left, Right }

    /// <summary>A placed element belongs to one page; coordinates are independent of camera and book position.</summary>
    [Serializable]
    public sealed class JournalDecorationData
    {
        public string instanceId;
        public string assetId;
        public string type;
        // Additive schema-v1 fields. The containing page remains authoritative.
        public string pageId;
        // Base size in normalized page units, before scale and tape length.
        // Zero means a legacy record whose catalogue supplies its original size.
        public Vector2 size = Vector2.zero;
        public float length = 1f;
        // Original physical cut length; zero denotes a legacy digital decoration.
        public float lengthMeters;
        public bool physicalMaterial;
        public Vector2 physicalPageSize;
        // Normalized page coordinates: (0,0) lower left, (1,1) upper right.
        public Vector2 position = new Vector2(.5f, .5f);
        public Vector2 scale = Vector2.one;
        public float rotationDegrees;
        public int sortOrder;
        public string payloadJson;
    }

    /// <summary>Player-owned page instance. A paper asset can be reused by many instances.</summary>
    [Serializable]
    public sealed class JournalPageData
    {
        public string pageId;
        public string paperId;
        public List<JournalDecorationData> decorations = new List<JournalDecorationData>();
    }

    [Serializable]
    public sealed class JournalDocument
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public int currentSpread;
        public JournalPageData[] pages;
        // Optional additive section. Old schema-v1 saves deserialize this as null and migrate on first use.
        public CraftWorkspaceData craftWorkspace;
        // Inline Unity serialization can materialize null classes as empty objects. This flag preserves absence.
        public bool craftWorkspaceEnabled;
        // Optional commission-letter branch with its own schema version; absent in every save written before 2026-11-05.
        public CommissionDeskData commissionDesk;
        public bool commissionDeskEnabled;
        // Independently versioned night gameplay; absent in older journal-v1 saves.
        public DreamGameplayData dreamGameplay;
        public bool dreamGameplayEnabled;
        // Optional independently versioned branches added by the complete gameplay demo.
        public AtelierProgressData atelierProgress;
        public bool atelierProgressEnabled;
        public PersonalStudioData personalStudio;
        public bool personalStudioEnabled;
        public AtelierSocialData atelierSocial;
        public bool atelierSocialEnabled;

        public NotebookLibraryData notebookLibrary;
        public bool notebookLibraryEnabled;
        public string ActiveBookId => notebookLibrary?.activeBookId ?? NotebookLibraryData.BinderId;
        public NotebookData ActiveNotebook => notebookLibrary?.Active;
        public JournalPageData[] ActivePages => ActiveNotebook?.pages ?? pages;
        public int ActiveSpread { get => ActiveNotebook != null ? ActiveNotebook.currentSpread : currentSpread;
            set { if (ActiveNotebook != null) ActiveNotebook.currentSpread = value; else currentSpread = value; } }
        public IEnumerable<JournalPageData> AllPages => (pages ?? Array.Empty<JournalPageData>()).Concat(
            notebookLibrary?.notebooks?.SelectMany(b => b?.pages ?? Array.Empty<JournalPageData>()) ?? Enumerable.Empty<JournalPageData>());
        public Vector2 PageMetres(string pageId, Vector2 binderSize)
        { return notebookLibrary?.notebooks?.FirstOrDefault(b => b.pages.Any(p => p.pageId == pageId))?.pageSizeMeters ?? binderSize; }
        public int SpreadCount => ActivePages == null ? 0 : ActivePages.Length / 2;
        public int PageCount => pages == null ? 0 : pages.Length;

        public JournalPageData GetPage(JournalSide side)
        {
            int index = ActiveSpread * 2 + (side == JournalSide.Left ? 0 : 1);
            return ActivePages != null && index >= 0 && index < ActivePages.Length ? ActivePages[index] : null;
        }

        public JournalDocument DeepClone()
        {
            var clone = JsonUtility.FromJson<JournalDocument>(JsonUtility.ToJson(this));
            clone.NormalizeOptionalData();
            return clone;
        }

        public void NormalizeOptionalData()
        {
            if (!atelierProgressEnabled)
            { if (atelierProgress != null && atelierProgress.initialized) atelierProgressEnabled = true; else atelierProgress = null; }
            if (!personalStudioEnabled)
            { if (personalStudio != null && personalStudio.initialized) personalStudioEnabled = true; else personalStudio = null; }
            if (!atelierSocialEnabled)
            { if (atelierSocial != null && atelierSocial.initialized) atelierSocialEnabled = true; else atelierSocial = null; }
            if (!dreamGameplayEnabled)
            {
                if (dreamGameplay != null && (dreamGameplay.initialized || dreamGameplay.coins > 0 || dreamGameplay.nextRefreshUtcTicks > 0 ||
                    (dreamGameplay.claimedRewardLetters != null && dreamGameplay.claimedRewardLetters.Count > 0) ||
                    (dreamGameplay.pending != null && dreamGameplay.pending.Count > 0))) dreamGameplayEnabled = true;
                else dreamGameplay = null;
            }
            NormalizeCommissionDesk();
            if (!notebookLibraryEnabled)
            {
                if (notebookLibrary?.notebooks != null && notebookLibrary.notebooks.Count > 0) notebookLibraryEnabled = true;
                else notebookLibrary = null;
            }
            if (!craftWorkspaceEnabled)
            {
                bool containsWorkspace = craftWorkspace != null && ((craftWorkspace.books != null && craftWorkspace.books.Count > 0) ||
                    (craftWorkspace.items != null && craftWorkspace.items.Count > 0) || (craftWorkspace.drawers != null && craftWorkspace.drawers.Count > 0));
                if (!containsWorkspace) { craftWorkspace = null; return; }
                // Compatibility with the first additive workspace snapshots written before the presence flag.
                craftWorkspaceEnabled = true;
            }
            if (craftWorkspace != null && craftWorkspace.pending != null && string.IsNullOrEmpty(craftWorkspace.pending.instanceId) &&
                !craftWorkspace.pending.created && (craftWorkspace.pending.origin == null || string.IsNullOrEmpty(craftWorkspace.pending.origin.instanceId)) &&
                (craftWorkspace.pending.originDecoration == null || string.IsNullOrEmpty(craftWorkspace.pending.originDecoration.instanceId)) &&
                (craftWorkspace.items == null || !craftWorkspace.items.Exists(i => i != null && i.owner == CraftOwner.Held)))
                craftWorkspace.pending = null;
        }

        void NormalizeCommissionDesk()
        {
            if (commissionDeskEnabled) return;
            bool contains = commissionDesk != null && ((commissionDesk.letters != null && commissionDesk.letters.Count > 0) ||
                commissionDesk.clockSeconds > 0 || commissionDesk.stamps > 0 || commissionDesk.nextSequence > 0);
            if (contains) commissionDeskEnabled = true; else commissionDesk = null;
        }

        public static JournalDocument CreateDefault(JournalPaperCatalog catalog)
        {
            if (!catalog || catalog.papers == null || catalog.papers.Length == 0)
                throw new InvalidOperationException("没有可用的手账内页素材，无法建立默认手账。");
            var document = new JournalDocument { pages = new JournalPageData[24], currentSpread = 0 };
            var defaultPaper = catalog.Find(catalog.defaultPaperId);
            for (int i = 0; i < document.pages.Length; i++)
            {
                var definition = defaultPaper ?? catalog.papers[i % catalog.papers.Length];
                if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                    throw new InvalidOperationException("内页素材目录包含无效定义。");
                document.pages[i] = new JournalPageData {
                    pageId = "journal-page-" + (i + 1).ToString("D3"),
                    paperId = definition.id,
                    decorations = new List<JournalDecorationData>()
                };
            }
            return document;
        }
    }
}
