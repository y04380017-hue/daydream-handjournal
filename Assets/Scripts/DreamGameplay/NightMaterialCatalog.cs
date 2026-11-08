using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Stable material definitions. Presentation can change without changing earned instance IDs.</summary>
    public static class NightMaterialCatalog
    {
        public static readonly string[] CommonAssetIds = { "night-glow-ink", "night-star-sticker", "night-lantern-paper" };
        public static readonly string[] DayAssetIds = { "day-iridescent-ink", "day-origami-paper", "day-cloud-washi", "day-lined-paper" };
        public static readonly string[] AfternoonAssetIds = { "afternoon-flower-sticker", "afternoon-balloon-ink", "afternoon-leaf-sticker", "afternoon-gold-foil" };
        public static readonly string[] AllAssetIds = { "night-glow-ink", "night-star-sticker", "night-lantern-paper", "night-moonlit-star", "night-forgotten-dream", "day-iridescent-ink", "day-origami-paper", "day-cloud-washi", "day-lined-paper", "day-pearl-paper", "afternoon-flower-sticker", "afternoon-balloon-ink", "afternoon-leaf-sticker", "afternoon-gold-foil", "afternoon-sunset-foil", "season-sakura", "season-firework", "season-mooncake", "season-snowflake", "season-newyear" };
        public static string[] ForSetting(int setting) => setting == 1 ? DayAssetIds : setting == 2 ? AfternoonAssetIds : CommonAssetIds;
        public static bool IsPaper(string id) => id == "night-lantern-paper" || id == "day-origami-paper" || id == "day-lined-paper" || id=="day-pearl-paper";
        public static int SettingOf(string id) => Array.IndexOf(DayAssetIds, id) >= 0 ? 1 : Array.IndexOf(AfternoonAssetIds, id) >= 0 ? 2 : 0;
        static readonly Dictionary<string, DecorationDefinition> definitions = new Dictionary<string, DecorationDefinition>(StringComparer.Ordinal);
        static readonly string[] GlowTags = { "静谧", "魔法", "微光" };
        static readonly string[] StarTags = { "梦幻", "遥远", "希望" };
        static readonly string[] LanternTags = { "思念", "温暖", "微光" };
        static readonly string[] DreamTags = { "怀旧", "梦幻", "思念" };
        static readonly string[] MoonTags = { "梦幻", "静谧", "遥远" };

        public static bool IsKnown(string assetId) => Array.IndexOf(AllAssetIds, assetId) >= 0;

        public static string NameFor(string assetId)
        {
            switch (assetId)
            {
                case "day-iridescent-ink": return "虹彩墨印";
                case "day-origami-paper": return "纸鹤折纸";
                case "day-cloud-washi": return "云白和纸条";
                case "day-lined-paper": return "旧日横线纸";
                case "day-pearl-paper": return "珍珠光折纸";
                case "afternoon-flower-sticker": return "压花贴纸";
                case "afternoon-balloon-ink": return "气球色墨印";
                case "afternoon-leaf-sticker": return "叶片花瓣贴纸";
                case "afternoon-gold-foil": return "午后金箔";
                case "afternoon-sunset-foil": return "晚霞玫瑰金箔";
                case "season-sakura": return "春日樱花";
                case "season-firework": return "夏祭烟花";
                case "season-mooncake": return "团圆月饼";
                case "season-snowflake": return "初雪结晶";
                case "season-newyear": return "新年小灯";
                case "night-glow-ink": return "萤光墨印";
                case "night-star-sticker": return "近星贴纸";
                case "night-lantern-paper": return "灯笼和纸";
                case "night-forgotten-dream": return "被遗忘的梦";
                case "night-moonlit-star": return "月光星簇";
                default: return assetId ?? string.Empty;
            }
        }

        public static string[] TagsFor(string assetId)
        {
            string[] tags;
            switch (assetId)
            {
                case "day-iridescent-ink": return new[] { "梦幻", "轻盈", "玩心" };
                case "day-origami-paper": return new[] { "希望", "细腻" };
                case "day-cloud-washi": return new[] { "柔软", "静谧", "轻盈" };
                case "day-lined-paper": return new[] { "日常", "怀旧" };
                case "day-pearl-paper": return new[] { "细腻", "梦幻", "轻盈" };
                case "afternoon-flower-sticker": return new[] { "温柔", "短暂" };
                case "afternoon-balloon-ink": return new[] { "温暖", "欢喜" };
                case "afternoon-leaf-sticker": return new[] { "怀旧", "消逝" };
                case "afternoon-gold-foil": return new[] { "温暖", "珍贵" };
                case "afternoon-sunset-foil": return new[] { "温暖", "怀旧", "珍贵" };
                case "season-sakura": return new[] { "温柔", "短暂", "希望" };
                case "season-firework": return new[] { "欢喜", "微光", "短暂" };
                case "season-mooncake": return new[] { "思念", "温暖", "珍贵" };
                case "season-snowflake": return new[] { "静谧", "梦幻", "柔软" };
                case "season-newyear": return new[] { "希望", "温暖", "微光" };
                case "night-glow-ink": tags = GlowTags; break;
                case "night-star-sticker": tags = StarTags; break;
                case "night-lantern-paper": tags = LanternTags; break;
                case "night-forgotten-dream": tags = DreamTags; break;
                case "night-moonlit-star": tags = MoonTags; break;
                default: return Array.Empty<string>();
            }
            return (string[])tags.Clone();
        }

        public static Color ColorFor(string assetId)
        {
            switch (assetId)
            {
                case "night-glow-ink": return new Color(.50f, .90f, .78f);
                case "night-lantern-paper": return new Color(1, .77f, .49f);
                case "night-forgotten-dream": return new Color(.77f, .68f, .95f);
                case "night-moonlit-star": return new Color(.73f, .80f, 1);
                default: return new Color(1, .87f, .59f);
            }
        }

        /// <summary>Collected materials deliberately stay outside the unlimited legacy digital-tool asset list.</summary>
        public static DecorationDefinition Find(string assetId)
        {
            if (!IsKnown(assetId)) return null;
            if (definitions.TryGetValue(assetId, out var existing) && existing.texture) return existing;
            Texture2D texture = DrawTexture(assetId, out var alphaMask);
            bool paper = IsPaper(assetId);
            var definition = new DecorationDefinition
            {
                assetId = assetId, displayName = NameFor(assetId),
                category = paper ? DecorationCategory.Note : DecorationCategory.Sticker,
                texture = texture, thumbnail = texture,
                defaultSize = paper ? new Vector2(.31f, .29f) : new Vector2(.24f, .17f),
                minScale = .35f, maxScale = 2, supportsTint = false, supportsLengthAdjustment = false,
                alphaMask = alphaMask, maskWidth = 64, maskHeight = 64
            };
            definitions[assetId] = definition;
            return definition;
        }

        static Texture2D DrawTexture(string assetId, out byte[] alphaMask)
        {
            const int size = 256;
            var pixels = new Color[size * size];
            alphaMask = new byte[64 * 64];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1);
                Color color = Color.clear;
                switch (assetId)
                {
                    case "day-iridescent-ink":
                    case "afternoon-balloon-ink":
                        Dot(ref color,p,new Vector2(-.18f,.1f),.48f,new Color(.96f,.85f,.98f));
                        Dot(ref color,p,new Vector2(.17f,.15f),.36f,new Color(.65f,.87f,.93f));
                        Dot(ref color,p,new Vector2(.11f,-.3f),.31f,new Color(1,.82f,.77f));
                        break;
                    case "day-origami-paper":
                        Paint(ref color,new Color(.77f,.87f,.96f),Mathf.Max(Mathf.Abs(p.x)+Mathf.Abs(p.y)*.55f-.76f, -p.y-.58f));
                        Paint(ref color,new Color(.96f,.77f,.84f),Mathf.Max(Mathf.Abs(p.x-.2f)+Mathf.Abs(p.y+.06f)*.9f-.43f,p.y-.35f));
                        break;
                    case "day-cloud-washi":
                        for(int i=0;i<3;i++)Dot(ref color,p,new Vector2(-.43f+i*.43f,-.02f),.35f,new Color(.91f,.95f,1));
                        break;
                    case "day-lined-paper":
                    case "day-pearl-paper":
                        Paint(ref color,new Color(1,.96f,.86f),Mathf.Max(Mathf.Abs(p.x)-.7f,Mathf.Abs(p.y)-.86f));
                        if(assetId=="day-pearl-paper"&&color.a>0)color=Color.Lerp(color,new Color(.87f,.82f,.98f,color.a),(Mathf.Sin(p.x*9+p.y*6)+1)*.22f);
                        if(Mathf.Abs(p.x)<.65f&&Mathf.Abs(p.y)<.8f&&Mathf.Abs(Mathf.Sin(p.y*19))<.09f)color=new Color(.64f,.77f,.84f);
                        break;
                    case "afternoon-flower-sticker":
                    case "season-sakura":
                        for(int i=0;i<5;i++){float a=i*Mathf.PI*2/5;Dot(ref color,p,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.32f,.26f,new Color(.99f,.69f,.78f));}
                        Dot(ref color,p,Vector2.zero,.13f,new Color(1,.88f,.53f));break;
                    case "afternoon-leaf-sticker":
                        Paint(ref color,new Color(.77f,.58f,.35f),new Vector2(p.x*.8f,p.y*1.25f).magnitude-.65f);
                        if(Mathf.Abs(p.x)<.035f&&Mathf.Abs(p.y)<.54f)color=new Color(.97f,.86f,.61f);break;
                    case "afternoon-gold-foil":
                    case "afternoon-sunset-foil":
                        Paint(ref color,new Color(.96f,.80f,.39f),Mathf.Max(Mathf.Abs(p.x)-.65f,Mathf.Abs(p.y)-.7f)+Mathf.Sin(p.y*40)*.025f);
                        if(assetId=="afternoon-sunset-foil"&&color.a>0)color=Color.Lerp(color,new Color(.96f,.61f,.69f,color.a),(p.y+1)*.35f);
                        break;
                    case "season-firework":
                        for(int i=0;i<12;i++){float a=i*Mathf.PI/6;Dot(ref color,p,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.57f,.08f,i%2==0?new Color(1,.72f,.72f):new Color(.75f,.78f,1));}
                        Star(ref color,p,Vector2.zero,.26f,new Color(1,.86f,.55f));break;
                    case "season-mooncake":
                        Dot(ref color,p,Vector2.zero,.72f,new Color(.84f,.59f,.34f));Dot(ref color,p,Vector2.zero,.59f,new Color(1,.81f,.48f));
                        Star(ref color,p,Vector2.zero,.34f,new Color(.88f,.59f,.36f));break;
                    case "season-snowflake":
                        Star(ref color,p,Vector2.zero,.75f,new Color(.75f,.89f,1));Star(ref color,p,Vector2.zero,.4f,new Color(.97f,.99f,1));break;
                    case "season-newyear":
                        Paint(ref color,new Color(.98f,.64f,.61f),Mathf.Max(Mathf.Abs(p.x)-.53f,Mathf.Abs(p.y)-.7f));
                        Star(ref color,p,Vector2.zero,.31f,new Color(1,.9f,.6f));break;
                    case "night-glow-ink":
                        // A translucent crescent and small firefly marks, not a rectangular digital tile.
                        float moon = Mathf.Max((p - new Vector2(-.05f, .04f)).magnitude - .55f,
                            .46f - (p - new Vector2(.22f, .20f)).magnitude);
                        Paint(ref color, new Color(.94f, .99f, .89f), moon - .035f);
                        Paint(ref color, new Color(.44f, .78f, .70f), moon);
                        Paint(ref color, new Color(.70f, .92f, .83f), moon + .045f);
                        Star(ref color, p, new Vector2(.44f, -.27f), .14f, new Color(.99f, .87f, .50f));
                        Dot(ref color, p, new Vector2(.33f, .51f), .055f, new Color(.54f, .80f, .78f));
                        Dot(ref color, p, new Vector2(-.55f, -.43f), .045f, new Color(.54f, .80f, .78f));
                        break;
                    case "night-lantern-paper":
                        float edge = Mathf.Max(Mathf.Abs(p.x) - .73f, Mathf.Abs(p.y) - .88f) + .007f * Mathf.Sin(p.y * 83);
                        Paint(ref color, new Color(1, .96f, .83f), edge);
                        Paint(ref color, new Color(.96f, .80f, .56f), Mathf.Max(Mathf.Abs(p.x) - .67f, Mathf.Abs(p.y) - .82f));
                        Paint(ref color, new Color(1, .91f, .71f), Mathf.Max(Mathf.Abs(p.x) - .65f, Mathf.Abs(p.y) - .80f));
                        if (Mathf.Abs(p.x) < .64f && Mathf.Abs(p.y) < .79f)
                        {
                            float grain = Mathf.Sin(x * 1.73f + y * 3.19f) * .018f;
                            color.r += grain; color.g += grain; color.b += grain;
                            // Quiet horizontal paper fibres and a little embossed lantern seal.
                            if (Mathf.Abs(Mathf.Sin(p.y * 19)) < .075f) color = Color.Lerp(color, new Color(.80f, .57f, .40f), .16f);
                        }
                        Dot(ref color, p, new Vector2(0, .31f), .23f, new Color(.88f, .58f, .36f));
                        Dot(ref color, p, new Vector2(0, .31f), .20f, new Color(1, .84f, .57f));
                        Star(ref color, p, new Vector2(0, .31f), .12f, new Color(.99f, .95f, .77f));
                        break;
                    case "night-forgotten-dream":
                        // A little impossible door on three cloud-like steps.
                        float door = Mathf.Max(Mathf.Abs(p.x) - .29f, Mathf.Abs(p.y - .11f) - .58f);
                        Paint(ref color, new Color(.99f, .96f, 1), door - .045f);
                        Paint(ref color, new Color(.45f, .40f, .65f), door);
                        Paint(ref color, new Color(.78f, .73f, .94f), Mathf.Max(Mathf.Abs(p.x) - .245f, Mathf.Abs(p.y - .11f) - .53f));
                        Paint(ref color, new Color(.59f, .59f, .82f), Mathf.Max(Mathf.Abs(p.x) - .20f, Mathf.Abs(p.y - .14f) - .44f));
                        Dot(ref color, p, new Vector2(.14f, .05f), .028f, new Color(1, .88f, .57f));
                        for (int i = 0; i < 3; i++)
                            Paint(ref color, i % 2 == 0 ? new Color(.88f, .83f, .98f) : new Color(.73f, .78f, .94f),
                                Mathf.Max(Mathf.Abs(p.x) - (.33f + .08f * i), Mathf.Abs(p.y + .52f + .11f * i) - .045f));
                        Star(ref color, p, new Vector2(-.52f, .49f), .11f, new Color(1, .87f, .59f));
                        Star(ref color, p, new Vector2(.51f, .20f), .09f, new Color(.70f, .87f, .94f));
                        break;
                    case "night-moonlit-star":
                        Star(ref color, p, new Vector2(-.15f, .08f), .53f, new Color(.73f, .75f, .96f));
                        Star(ref color, p, new Vector2(.48f, .46f), .19f, new Color(1, .88f, .61f));
                        Star(ref color, p, new Vector2(.44f, -.45f), .24f, new Color(.67f, .86f, .95f));
                        Dot(ref color, p, new Vector2(-.54f, -.51f), .035f, new Color(.66f, .72f, .91f));
                        break;
                    default:
                        Star(ref color, p, Vector2.zero, .75f, new Color(1, .83f, .43f));
                        Star(ref color, p, new Vector2(-.08f, .08f), .45f, new Color(1, .91f, .68f), false);
                        break;
                }
                pixels[y * size + x] = color;
                if (x % 4 == 2 && y % 4 == 2) alphaMask[(y / 4) * 64 + x / 4] = (byte)Mathf.RoundToInt(color.a * 255);
            }
            bool transparent = false, opaque = false;
            foreach (byte alpha in alphaMask) { transparent |= alpha == 0; opaque |= alpha == 255; }
            if (!transparent || !opaque)
                throw new InvalidOperationException("Night material must contain both clear and opaque pixels: " + assetId);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Collected " + assetId, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            texture.SetPixels(pixels); texture.Apply(false, true);
            return texture;
        }

        static void Dot(ref Color target, Vector2 p, Vector2 centre, float radius, Color color)
        { Paint(ref target, color, (p - centre).magnitude - radius); }
        static void Paint(ref Color target, Color color, float distance)
        {
            float a = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.006f, .009f, distance));
            if (a <= 0) return;
            float alpha = a * color.a;
            float combined = alpha + target.a * (1 - alpha);
            if (combined <= 0) return;
            var mixed = (color * alpha + target * (target.a * (1 - alpha))) / combined;
            mixed.a = combined; target = mixed;
        }
        static void Star(ref Color target, Vector2 p, Vector2 centre, float radius, Color color, bool outline = true)
        {
            Vector2 q = p - centre;
            float angle = Mathf.Atan2(q.y, q.x) - Mathf.PI * .5f;
            float boundary = radius * (.68f + .32f * Mathf.Cos(angle * 5));
            float distance = q.magnitude - boundary;
            if (outline) Paint(ref target, new Color(1, .98f, .92f), distance - .034f);
            Paint(ref target, color, distance);
        }
    }
}
