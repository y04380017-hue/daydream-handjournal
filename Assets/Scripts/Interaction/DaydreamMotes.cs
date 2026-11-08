using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// A few soft light motes drifting upward around/behind the floating desk. Built from camera-facing quads
    /// (no particle module, no colliders). Purely decorative and paced by the shared DaydreamBreath phase.
    /// </summary>
    public sealed class DaydreamMotes : MonoBehaviour
    {
        public Material material;
        public int count = 42;
        public Vector3 size = new Vector3(2.6f, 1.1f, 1.2f);
        public Vector2 diameter = new Vector2(.006f, .016f);
        public Vector2 riseSpeed = new Vector2(.012f, .03f);
        public Color[] colors = { new Color(1f, .91f, .66f), new Color(1f, .78f, .86f), new Color(.75f, .9f, 1f) };
        readonly List<Transform> motes = new List<Transform>();
        readonly List<Vector4> seeds = new List<Vector4>(); // x speed, y sway phase, z base alpha, w size
        Camera lens; MaterialPropertyBlock block;
        public int ActiveCount => motes.Count;
        /// <summary>Set by NightDreamcore: night motes drift at ~30 % speed, shrink to dust-size and turn cream.</summary>
        public float NightAmount { get; set; }
        public Color nightColor = new Color(1f, .945f, .878f, 1f);

        void Start()
        {
            lens = Camera.main; block = new MaterialPropertyBlock(); var rand = new System.Random(17);
            for (int i = 0; i < count; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.name = "Mote"; q.transform.SetParent(transform, false);
                var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                float d = Mathf.Lerp(diameter.x, diameter.y, (float)rand.NextDouble());
                q.transform.localScale = Vector3.one * d;
                q.transform.localPosition = new Vector3(((float)rand.NextDouble() - .5f) * size.x, (float)rand.NextDouble() * size.y, ((float)rand.NextDouble() - .5f) * size.z);
                block.SetColor("_BaseColor", colors[i % colors.Length]); r.SetPropertyBlock(block);
                motes.Add(q.transform); seeds.Add(new Vector4(Mathf.Lerp(riseSpeed.x, riseSpeed.y, (float)rand.NextDouble()), (float)rand.NextDouble() * 6.3f, .55f + .45f * (float)rand.NextDouble(), d));
            }
        }

        void Update()
        {
            if (!lens) lens = Camera.main;
            float breath = DaydreamBreath.Phase, dt = Time.deltaTime * Mathf.Lerp(1, .3f, NightAmount), t = Time.time;
            for (int i = 0; i < motes.Count; i++)
            {
                var m = motes[i]; var s = seeds[i]; var p = m.localPosition;
                p.y += s.x * dt * (1 + .35f * breath);
                p.x += Mathf.Sin(t * .4f + s.y) * .004f * dt;
                if (p.y > size.y) p.y -= size.y;
                m.localPosition = p; m.localScale = Vector3.one * s.w * Mathf.Lerp(1, .4f, NightAmount);
                if (lens) m.rotation = Quaternion.LookRotation(m.position - lens.transform.position);
                float edge = Mathf.Clamp01(Mathf.Min(p.y, size.y - p.y) / (size.y * .2f));
                var r = m.GetComponent<MeshRenderer>(); r.GetPropertyBlock(block);
                var c = Color.Lerp(colors[i % colors.Length], nightColor, NightAmount); c.a = s.z * edge * (.8f + .2f * breath) * Mathf.Lerp(1, .75f, NightAmount); block.SetColor("_BaseColor", c); r.SetPropertyBlock(block);
            }
        }
    }
}
