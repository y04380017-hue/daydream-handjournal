using UnityEngine;

namespace VintageJournal
{
    /// <summary>A sky decoration's explicit collection capability; the persisted token remains the inventory identity.</summary>
    public sealed class FloatingMaterialTarget : MonoBehaviour
    {
        public DreamSkyPasser passer;
        public BoxCollider hitCollider;
        public int LastClaimPass {get;private set;}=-1;
        public int LastClaimSetting {get;private set;}=-1;
        public string LastClaimTokenId {get;private set;}
        public string LastClaimAssetId {get;private set;}
        public bool CanClaim(int setting)=>passer&&passer.IsVisible&&
            !GetComponentInParent<ScenicOnlyMarker>()&&
            (LastClaimSetting!=setting||LastClaimPass!=passer.PassesStarted);
        public void MarkClaimed(int setting,string tokenId=null,string assetId=null){LastClaimSetting=setting;LastClaimPass=passer?passer.PassesStarted:-1;LastClaimTokenId=tokenId;LastClaimAssetId=assetId;}
        void Update(){if(hitCollider)hitCollider.enabled=passer&&passer.IsVisible&&!GetComponentInParent<ScenicOnlyMarker>();}

        public static FloatingMaterialTarget Bind(DreamSkyPasser passer)
        {
            if(!passer||!passer.visual||passer.GetComponentInParent<ScenicOnlyMarker>()||passer.visual.GetComponentInParent<ScenicOnlyMarker>())return null;
            var target=passer.visual.GetComponent<FloatingMaterialTarget>();if(target)return target;
            target=passer.visual.gameObject.AddComponent<FloatingMaterialTarget>();target.passer=passer;
            // Dimension adaptation of replaceable art, not an inventory rule. All ownership still uses token IDs.
            bool any=false;Bounds bounds=default;
            foreach(var renderer in passer.visual.GetComponentsInChildren<Renderer>(true)){
                if(renderer is LineRenderer)continue;
                if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);
            }
            var collider=passer.visual.gameObject.AddComponent<BoxCollider>();target.hitCollider=collider;
            if(any){
                collider.center=passer.visual.InverseTransformPoint(bounds.center);
                var scale=passer.visual.lossyScale;collider.size=new Vector3(bounds.size.x/Mathf.Max(.001f,Mathf.Abs(scale.x)),bounds.size.y/Mathf.Max(.001f,Mathf.Abs(scale.y)),bounds.size.z/Mathf.Max(.001f,Mathf.Abs(scale.z)));
            }else collider.size=new Vector3(.12f,.12f,.04f);
            var size=collider.size;size.x=Mathf.Max(.045f,size.x);size.y=Mathf.Max(.045f,size.y);size.z=Mathf.Max(.025f,size.z);collider.size=size;
            collider.isTrigger=false;collider.enabled=passer.IsVisible;
            return target;
        }
    }
}
