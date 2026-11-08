using System;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Explicit authoring references for the solid desk/mailbox audit; no inventory or input behaviour.</summary>
    public sealed class DeskOpaqueSurfaceBindings : MonoBehaviour
    {
        public Renderer[] deskBodies=Array.Empty<Renderer>();
        public Renderer[] mailboxBodies=Array.Empty<Renderer>();
        public Renderer[] addedBackplates=Array.Empty<Renderer>();
    }
}
