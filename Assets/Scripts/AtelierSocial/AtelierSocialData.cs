using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    [Serializable] public sealed class SocialUser { public string id, name, code, token; public bool banned; }
    [Serializable] public sealed class SocialFriendRequest { public string id; public SocialUser from; }
    [Serializable] public sealed class SocialReport { public string contentId, reporterId, reason, createdUtc; }
    [Serializable] public sealed class SocialEntry
    {
        public string id, ownerId, ownerName, kind, title, text, pngBase64, status, createdUtc, recipientId, parentId, acceptedBy, fulfilledId;
        public int likes; public bool liked, acked;
    }
    [Serializable] public sealed class SocialEnvelope
    {
        public string id, name, code, token, error;
        public bool ok;
        public SocialEntry[] entries = Array.Empty<SocialEntry>();
        public SocialUser[] users = Array.Empty<SocialUser>();
        public SocialFriendRequest[] requests = Array.Empty<SocialFriendRequest>();
        public SocialReport[] reports = Array.Empty<SocialReport>();
    }
    [Serializable] public sealed class SocialPayload
    {
        public string name, title, text, pngBase64, recipientId, requestKey, code, requestId, contentId, reason, playerId, decision;
        public bool blocked;
    }
    [Serializable] public sealed class SocialGiftArtwork { public string assetId, giftId, name, pngBase64; }
    [Serializable] public sealed class AtelierSocialData
    {
        public int schemaVersion = 1;
        public bool initialized;
        public string serverUrl = "http://127.0.0.1:18747", playerId, name, friendCode, token;
        public List<string> claimedGiftIds = new List<string>();
        public List<SocialGiftArtwork> gifts = new List<SocialGiftArtwork>();
        public AtelierSocialData Clone() => JsonUtility.FromJson<AtelierSocialData>(JsonUtility.ToJson(this));
        public static bool ValidServerUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/") return false;
            return uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback);
        }
        public static bool SameOrigin(string a,string b)=>ValidServerUrl(a)&&ValidServerUrl(b)&&
            string.Equals(new Uri(a).GetLeftPart(UriPartial.Authority),new Uri(b).GetLeftPart(UriPartial.Authority),StringComparison.OrdinalIgnoreCase);
        public static bool Validate(JournalDocument document, out string error)
        {
            error = string.Empty; var data = document?.atelierSocial; if (data == null) return true;
            if (data.schemaVersion != 1 || !ValidServerUrl(data.serverUrl) || data.claimedGiftIds == null || data.gifts == null ||
                data.claimedGiftIds.Count > 2000 || data.gifts.Count > 12 || (data.token?.Length ?? 0) > 100 ||
                (data.name?.Length ?? 0) > 24 || (data.playerId?.Length ?? 0) > 64)
            { error = "社交分支版本、连接或容量无效。"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in data.claimedGiftIds) if (string.IsNullOrEmpty(id) || !ids.Add(id)) { error = "礼物领取记录重复。"; return false; }
            ids.Clear();
            foreach (var gift in data.gifts)
                if (gift == null || string.IsNullOrEmpty(gift.assetId) || !ids.Add(gift.assetId) || !data.claimedGiftIds.Contains(gift.giftId) ||
                    string.IsNullOrEmpty(gift.pngBase64) || gift.pngBase64.Length > 200000 ||
                    document.craftWorkspace?.items == null || !document.craftWorkspace.items.Exists(i => i.instanceId == "dream-social-" + gift.giftId && i.assetId == gift.assetId))
                { error = "礼物图案、墓碑或唯一归属不一致。"; return false; }
            return true;
        }
    }
}
