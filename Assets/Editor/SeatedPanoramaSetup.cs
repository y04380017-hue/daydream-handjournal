using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>Reuses existing 3D artwork behind the seated origin. No interaction, stock or camera objects are cloned.</summary>
    public static class SeatedPanoramaSetup
    {
        public const string RootName="Seated rear panorama";
        public static void Apply()
        {
            var camera=Object.FindFirstObjectByType<DeskCamera>();
            if(!camera||!camera.profile)throw new InvalidOperationException("A seated camera profile is required for rear scenery.");
            var profile=camera.profile;
            profile.seatedLookEnabled=true;
            if(profile.seatedLookEdgeFraction<.01f)profile.seatedLookEdgeFraction=.035f;
            if(profile.seatedYawDegreesPerSecond<20)profile.seatedYawDegreesPerSecond=110;
            if(profile.seatedPitchDegreesPerSecond<5)profile.seatedPitchDegreesPerSecond=42;
            if(profile.minimumSeatedPitch>=0)profile.minimumSeatedPitch=-26;
            if(profile.maximumSeatedPitch<=0)profile.maximumSeatedPitch=18;
            if(profile.collectionForwardHalfAngle<35)profile.collectionForwardHalfAngle=80;
            EditorUtility.SetDirty(profile);
            var sources=Object.FindObjectsByType<DreamSkyPasser>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(p=>p.visual&&!p.GetComponentInParent<ScenicOnlyMarker>())
                .OrderBy(p=>p.visual.GetComponentsInChildren<Renderer>(true).Length).ThenBy(p=>p.name,StringComparer.Ordinal).ToArray();
            if(sources.Length<3)throw new InvalidOperationException("Existing geometric sky artwork is missing.");
            var root=GameObject.Find(RootName)?.transform;
            if(!root)root=new GameObject(RootName).transform;
            root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.localScale=Vector3.one;
            float[] u={.24f,.50f,.76f,.50f},v={.72f,.76f,.70f,.53f};
            var backward=Quaternion.AngleAxis(180,Vector3.up)*Quaternion.LookRotation(profile.seatedTarget-profile.seatedPosition,Vector3.up);
            float h=Mathf.Tan(profile.seatedFieldOfView*Mathf.Deg2Rad*.5f);
            for(int index=0;index<4;index++){
                string name="Rear scenic artwork "+(index+1);var visual=root.Find(name);
                if(!visual){
                    var copy=Object.Instantiate(sources[index%sources.Length].visual.gameObject,root,false);copy.name=name;visual=copy.transform;
                    // Strip component capabilities from the copy, never alter the source art or its asset files.
                    foreach(var collider in copy.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                    foreach(var rigidbody in copy.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(rigidbody);
                    foreach(var behaviour in copy.GetComponentsInChildren<Behaviour>(true))Object.DestroyImmediate(behaviour);
                    copy.AddComponent<ScenicOnlyMarker>();
                    var motion=copy.AddComponent<DaydreamFloat>();motion.bobMetres=.018f;motion.bobPeriod=8+index;motion.swayDegrees=1.4f;motion.phase=.5f+index*.8f;
                }
                visual.gameObject.SetActive(true);visual.localPosition=Vector3.zero;visual.localRotation=Quaternion.identity;visual.localScale=Vector3.one;
                var renderers=visual.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;
                var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                float scale=.45f/Mathf.Max(.001f,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z)));
                visual.localScale=Vector3.one*scale;
                var ray=backward*new Vector3((u[index]*2-1)*h*(16f/9f),(v[index]*2-1)*h,1);
                var position=profile.seatedPosition+ray.normalized*(index==3?4.2f:3.1f);
                bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                visual.position+=position-bounds.center;
                foreach(var renderer in renderers){renderer.enabled=true;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
                EditorUtility.SetDirty(visual);
            }
            EditorUtility.SetDirty(root);
            Debug.Log("[SeatedPanorama] Four geometric rear keepsakes; scenic-only, no colliders or inventory bindings.");
        }
    }
}
