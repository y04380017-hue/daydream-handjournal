using System;
using UnityEngine;

namespace VintageJournal
{
    public sealed partial class CraftWorkspaceService
    {
        public bool InitializeAtelierSocial()
        {
            if (Document.atelierSocial != null) return true;
            var candidate = Document.DeepClone(); candidate.atelierSocial = new AtelierSocialData { initialized = true }; candidate.atelierSocialEnabled = true;
            return Commit(candidate, "准备本机邮局连接", false);
        }
        public bool ConfigureSocial(string url, string name, string id, string code, string token)
        {
            url = (url ?? string.Empty).Trim().TrimEnd('/');
            if (!AtelierSocialData.ValidServerUrl(url)) return Fail("连接需使用本机HTTP或HTTPS完整地址。");
            var candidate = Document.DeepClone(); candidate.atelierSocial ??= new AtelierSocialData(); candidate.atelierSocialEnabled = true;
            var data = candidate.atelierSocial; data.initialized = true; data.serverUrl = url;
            data.name = name; data.playerId = id; data.friendCode = code; data.token = token;
            return Commit(candidate, "保存本机邮局身份", false);
        }
        /// <summary>Local durable receipt first; caller may acknowledge to server only after true.</summary>
        public bool ClaimSocialGift(SocialEntry entry)
        {
            var current = Document.atelierSocial;
            if (current == null || entry == null || string.IsNullOrEmpty(entry.id) || entry.recipientId != current.playerId || entry.status != "approved") return Fail("回礼不属于当前身份或尚未审核。");
            if (current.claimedGiftIds.Contains(entry.id)) return Flush();
            if (entry.acked) return Fail("这封回礼已在另一个存档领取，不会重新生成。");
            if (entry.kind != "letter" && entry.kind != "fulfillment") return Fail("这不是可收取的回信。");
            bool hasArtwork = entry.kind == "letter" && !string.IsNullOrEmpty(entry.pngBase64);
            if (hasArtwork && (current.gifts.Count >= 12 || entry.pngBase64.Length > 200000)) return Fail("本机最多保留12份好友贴纸，每张PNG需小于150KB。");
            if (hasArtwork)
            {
                if (!SocialMaterialAssets.ValidPng(entry.pngBase64, out var checkedTexture)) return Fail("回礼不是可用的256像素PNG。");
                if(Application.isPlaying) UnityEngine.Object.Destroy(checkedTexture); else UnityEngine.Object.DestroyImmediate(checkedTexture);
            }
            var candidate = Document.DeepClone(); var data = candidate.atelierSocial;
            data.claimedGiftIds.Add(entry.id);
            if (hasArtwork)
            {
                string asset = "social-gift-" + entry.id;
                data.gifts.Add(new SocialGiftArtwork { assetId = asset, giftId = entry.id, name = entry.title, pngBase64 = entry.pngBase64 });
                candidate.craftWorkspace.items.Add(new CraftItemRecord { instanceId = "dream-social-" + entry.id, assetId = asset,
                    kind = CraftItemKind.Sticker, owner = CraftOwner.Stock, containerId = "dream-social-stock:" + entry.id, sizeMeters = new Vector2(.04f, .04f) });
            }
            return Commit(candidate, "收好好友来信与礼物", false) && Flush();
        }
    }
}
