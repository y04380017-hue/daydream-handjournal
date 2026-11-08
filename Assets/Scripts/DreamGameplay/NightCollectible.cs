using UnityEngine;

namespace VintageJournal
{
    /// <summary>A replaceable view bound to a persisted catch token. It never reads hardware input or owns inventory.</summary>
    public sealed class NightCollectible : MonoBehaviour
    {
        public string TokenId { get; private set; }
        public int VisualKind { get; private set; }
        public int Rarity { get; private set; }
        public Collider HitCollider { get; private set; }
        public Vector3 Home { get; private set; }
        float phase;

        public void Bind(string tokenId, int kind, int rarity, Vector3 home, int slot)
        {
            TokenId = tokenId; VisualKind = kind; Rarity = rarity; Home = home;
            phase = slot * 1.73f;
            HitCollider = GetComponent<Collider>();
            transform.localPosition = home;
        }

        public void Sample(float elapsed)
        {
            // A bounded loop is always catchable and never expires or leaves the seated view.
            transform.localPosition = Home + new Vector3(Mathf.Sin(elapsed * .21f + phase) * .020f,
                Mathf.Sin(elapsed * .47f + phase) * .012f, Mathf.Cos(elapsed * .24f + phase) * .009f);
            transform.localRotation = Quaternion.Euler(0, Mathf.Sin(elapsed * .29f + phase) * 16, Mathf.Sin(elapsed * .38f + phase) * 5);
        }
    }
}
