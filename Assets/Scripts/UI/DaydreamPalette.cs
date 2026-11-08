using UnityEngine;

namespace VintageJournal
{
    /// <summary>Single source of the daydream UI colours. Scenes and IMGUI skins read from here instead of hard-coding tones.</summary>
    public static class DaydreamPalette
    {
        public static readonly Color Cream = new Color(.992f, .978f, .955f, .97f);
        public static readonly Color Paper = new Color(.985f, .972f, .948f, .97f);
        public static readonly Color Card = new Color(1f, .995f, .985f, 1f);
        public static readonly Color Ink = new Color(.22f, .27f, .33f, 1f);
        public static readonly Color Muted = new Color(.45f, .51f, .56f, 1f);
        public static readonly Color Mint = new Color(.62f, .85f, .78f, 1f);
        public static readonly Color MintDeep = new Color(.33f, .62f, .55f, 1f);
        public static readonly Color Sky = new Color(.67f, .84f, .93f, 1f);
        public static readonly Color SkyDeep = new Color(.36f, .58f, .74f, 1f);
        public static readonly Color Peach = new Color(.96f, .74f, .66f, 1f);
        public static readonly Color Lilac = new Color(.81f, .77f, .92f, 1f);
        public static readonly Color Pollen = new Color(.96f, .84f, .48f, 1f);
        public static readonly Color Satin = new Color(.79f, .70f, .52f, 1f);
        public static readonly Color Shadow = new Color(.30f, .40f, .55f, .12f);
        public static readonly Color Veil = new Color(.86f, .92f, .97f, .30f);
        public static readonly Color StarBlue = new Color(.30f, .36f, .62f, 1f);
        public static readonly Color GridLine = new Color(.55f, .62f, .86f, .07f);

        // ---- Night dreamcore UI (2026-11-07 spec): panels sit inside the dream instead of on top of it ----
        public static readonly Color NightPanel = new Color(.957f, .945f, .980f, .78f);   // #F4F1FA @ 78 %
        public static readonly Color NightBorder = new Color(.851f, .827f, .933f, 1f);    // #D9D3EE
        public static readonly Color NightGlow = new Color(.910f, .886f, .961f, .30f);    // #E8E2F5 @ 30 %
        public static readonly Color NightInk = new Color(.231f, .247f, .431f, 1f);       // #3B3F6E
        /// <summary>0 by day, 1 at night; follows the dreamcore light stage.</summary>
        public static float Night => NightDreamcore.UiAmount;
        /// <summary>Light paper/cream surfaces turn into the translucent night panel; coloured surfaces keep their hue.</summary>
        public static Color Surface(Color day)
        {
            float n = Night; if (n <= 0) return day;
            float lum = .2126f * day.r + .7152f * day.g + .0722f * day.b;
            return lum > .9f ? Color.Lerp(day, NightPanel, n) : Color.Lerp(day, new Color(day.r, day.g, day.b, day.a * .9f), n);
        }
        public static Color Text(Color day) => Color.Lerp(day, NightInk, Night);
        public static Color Border(Color day) => Color.Lerp(day, NightBorder, Night);
        /// <summary>Soft 8 px outer glow drawn as fading rings (IMGUI has no blur).</summary>
        public static void OuterGlow(Rect r, float strength = 1)
        {
            float n = Night * strength; if (n <= .01f) return;
            var old = GUI.color;
            for (int i = 1; i <= 4; i++)
            {
                float g = i * 2; GUI.color = new Color(NightGlow.r, NightGlow.g, NightGlow.b, NightGlow.a * n * (1 - (i - 1) / 4f) * .55f);
                var o = new Rect(r.x - g, r.y - g, r.width + 2 * g, r.height + 2 * g);
                GUI.DrawTexture(new Rect(o.x, o.y, o.width, 2), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(o.x, o.yMax - 2, o.width, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(o.x, o.y + 2, 2, o.height - 4), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(o.xMax - 2, o.y + 2, 2, o.height - 4), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        /// <summary>Very faint square grid behind IMGUI panels (echoes the studio tiles and the sky-grid wall reference).</summary>
        public static void Grid(Rect r, float step = 22f)
        {
            var old = GUI.color; GUI.color = GridLine;
            for (float x = r.x + step; x < r.xMax - 2; x += step) GUI.DrawTexture(new Rect(x, r.y + 4, 1, r.height - 8), Texture2D.whiteTexture);
            for (float y = r.y + step; y < r.yMax - 2; y += step) GUI.DrawTexture(new Rect(r.x + 4, y, r.width - 8, 1), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
