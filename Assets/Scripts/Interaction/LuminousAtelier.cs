using UnityEngine;

namespace VintageJournal
{
    /// <summary>Presentation bindings for the translucent scenery and the warm pool around the desk.</summary>
    public sealed class LuminousAtelier : MonoBehaviour
    {
        public Renderer[] phaseSurfaces;
        public Renderer[] daytimeKeepsakes;
        public GameObject nightFixtures;
        public Light leftSoftbox, overheadLantern, edgeLight;
        public float leftPower=.34f, overheadPower=.28f, edgePower=.16f;
        public Color skyAmbient=new Color(.53f,.40f,.34f), sideAmbient=new Color(.56f,.39f,.26f), floorAmbient=new Color(.35f,.24f,.21f);
        public Color nightFill=new Color(1,.82f,.68f), nightKey=new Color(1,.85f,.68f), nightFog=new Color(.10f,.12f,.22f);
        public DreamSkyPasser[] nightMail;
        MaterialPropertyBlock block;
        static readonly int Night=Shader.PropertyToID("_Night");
        float previous=-1;
        public void Apply(float blend)
        {
            if (Mathf.Approximately(previous,blend)) return;
            previous=blend;
            block ??=new MaterialPropertyBlock();
            foreach(var surface in phaseSurfaces)
            {
                if(!surface) continue;
                surface.GetPropertyBlock(block);
                block.SetFloat(Night,blend);
                surface.SetPropertyBlock(block);
            }
            foreach(var part in daytimeKeepsakes) if(part)part.enabled=blend<.5f;
            if(nightFixtures) nightFixtures.SetActive(blend>.015f);
            Power(leftSoftbox,leftPower*blend); Power(overheadLantern,overheadPower*blend); Power(edgeLight,edgePower*blend);
        }
        static void Power(Light lamp,float intensity)
        {
            if(!lamp)return;
            lamp.intensity=intensity;
            lamp.enabled=intensity>.0005f;
        }
    }
}
