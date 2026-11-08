using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Visual-only: the pink CRT letter terminal's screen glows and pulses while letters or replies are unread.
    /// Reads CommissionDeskController state; click handling stays on the mailbox collider.
    /// </summary>
    public sealed class LetterTerminalScreen : MonoBehaviour
    {
        public Renderer screen;
        public Color idle = new Color(.62f, .66f, .86f, 1), unread = new Color(1f, .93f, .97f, 1);
        MaterialPropertyBlock block;
        public bool Lit { get; private set; }

        void Update()
        {
            if (!screen) return; block ??= new MaterialPropertyBlock();
            var desk = CommissionDeskController.Instance;
            Lit = desk && desk.Ready && desk.Service.UnreadCount > 0;
            float pulse = Lit ? .85f + .15f * Mathf.Sin(Time.unscaledTime * 3.2f) : 1;
            var c = Lit ? unread * pulse : idle; c.a = 1;
            screen.GetPropertyBlock(block); block.SetColor("_BaseColor", c); screen.SetPropertyBlock(block);
        }
    }
}
