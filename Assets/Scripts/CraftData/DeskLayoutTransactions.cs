using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Stable IDs and calibrated poses only; no artwork, hierarchy names or input dependencies.</summary>
    public sealed class CraftDeskLayoutTarget
    {
        public string instanceId, homeSlotId, displaySlotId, releaseBookId;
        public Vector3 homePosition, homeEulerAngles, displayPosition, displayEulerAngles;
        public bool migrateLegacyHome = true;
        public bool relocateStockOnMigration, relocateDesktopOnMigration;
        public string[] legacySlotIds = Array.Empty<string>();
    }

    public sealed partial class CraftWorkspaceService
    {
        /// <summary>Registers new finite stock and migrates legacy homes in one atomic snapshot.</summary>
        public bool RegisterDeskLayout(IEnumerable<CraftItemRecord> definitions, IEnumerable<CraftDeskLayoutTarget> targets, int version)
        {
            if (definitions == null || targets == null || version < 1 || version > CraftWorkspaceData.CurrentDeskLayoutVersion) return Fail("收纳布局版本或定义无效。");
            if (HeldItem != null) return Fail("请先放下正在制作的素材，再整理收纳。");
            var targetList = targets.ToArray();
            if (targetList.Any(t => t == null || string.IsNullOrWhiteSpace(t.instanceId) || string.IsNullOrWhiteSpace(t.homeSlotId) ||
                !CraftWorkspaceData.Finite(t.homePosition) || !CraftWorkspaceData.Finite(t.homeEulerAngles) ||
                !CraftWorkspaceData.Finite(t.displayPosition) || !CraftWorkspaceData.Finite(t.displayEulerAngles)) ||
                targetList.Select(t => t.instanceId).Distinct(StringComparer.Ordinal).Count() != targetList.Length)
                return Fail("收纳布局实例或锚点无效。");
            var byId = targetList.ToDictionary(t => t.instanceId, StringComparer.Ordinal);
            var candidate = Document.DeepClone();
            var data = candidate.craftWorkspace;
            bool migrate = data.deskLayoutVersion < version, changed = migrate;
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.instanceId)) return Fail("实体登记记录为空。");
                var existing = data.items.FirstOrDefault(i => i.instanceId == definition.instanceId);
                if (existing != null)
                {
                    if (existing.assetId != definition.assetId || existing.kind != definition.kind) return Fail("实体编号与已保存素材不一致。");
                    continue; // Deleted/attached instances remain owned exactly where they were.
                }
                data.items.Add(definition.Clone()); added.Add(definition.instanceId); changed = true;
            }
            var requests = data.items.Where(i => byId.ContainsKey(i.instanceId) &&
                (added.Contains(i.instanceId) && (i.owner==CraftOwner.Stock||i.owner==CraftOwner.Desktop) || migrate &&
                (i.owner == CraftOwner.Stock && (byId[i.instanceId].relocateStockOnMigration || byId[i.instanceId].migrateLegacyHome &&
                    (i.containerId == byId[i.instanceId].homeSlotId || (byId[i.instanceId].legacySlotIds?.Contains(i.containerId) ?? false))) ||
                 i.owner == CraftOwner.Desktop && (byId[i.instanceId].relocateDesktopOnMigration || data.deskLayoutVersion < 1 && i.kind == CraftItemKind.LoosePage)))).ToArray();
            var moving = new HashSet<string>(requests.Select(i => i.instanceId), StringComparer.Ordinal);
            var occupied = new HashSet<string>(data.items.Where(i => i.owner == CraftOwner.Stock && i.kind != CraftItemKind.TapeStrip &&
                !moving.Contains(i.instanceId) && !string.IsNullOrEmpty(i.containerId)).Select(i => i.containerId), StringComparer.Ordinal);
            // Existing user placements reserve their slots before any newly displayed stock is assigned.
            foreach (var item in requests.OrderBy(i => added.Contains(i.instanceId) ? 1 : 0))
            {
                var target = byId[item.instanceId];
                bool display = !string.IsNullOrEmpty(target.displaySlotId) && !occupied.Contains(target.displaySlotId);
                string slot = display ? target.displaySlotId : target.homeSlotId;
                if (!occupied.Add(slot)) return Fail("收纳位已被占用，原存档保持不变：" + item.instanceId);
                item.owner = CraftOwner.Stock; item.containerId = slot; item.pageId = null;
                item.worldPosition = display ? target.displayPosition : target.homePosition;
                item.worldEulerAngles = display ? target.displayEulerAngles : target.homeEulerAngles;
                if(item.kind==CraftItemKind.ReleaseBook&&!string.IsNullOrEmpty(target.releaseBookId))
                {
                    var book=data.books.FirstOrDefault(b=>b.bookId==target.releaseBookId);
                    if(book==null)return Fail("离型本收纳绑定无效，原存档保持不变。");
                    book.isOpen=false;
                }
            }
            data.deskLayoutVersion = Math.Max(data.deskLayoutVersion, version);
            return !changed || Commit(candidate, "整理双抽屉与桌面收纳", false);
        }
    }
}
