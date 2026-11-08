using UnityEngine;

namespace VintageJournal
{
    /// <summary>The shared router supplies mouse coordinates only after input ownership has been resolved.</summary>
    public sealed class SeatedLookMouseAdapter
    {
        public bool IsNeutral(Vector2 pointer,Vector2 screenSize,DeskCameraProfile profile)
        {
            if(!profile||screenSize.x<=0||screenSize.y<=0)return false;
            float edge=Mathf.Clamp(profile.seatedLookEdgeFraction,.01f,.15f);
            return pointer.x>screenSize.x*edge&&pointer.x<screenSize.x*(1-edge)&&pointer.y>screenSize.y*edge&&pointer.y<screenSize.y*(1-edge);
        }
        public bool TrySample(Vector2 pointer,Vector2 screenSize,float deltaSeconds,DeskCameraProfile profile,out DeskLookIntent intent)
        {
            intent=default;
            if(!profile||!profile.seatedLookEnabled||screenSize.x<=0||screenSize.y<=0||deltaSeconds<=0||
                pointer.x<0||pointer.y<0||pointer.x>screenSize.x||pointer.y>screenSize.y)return false;
            var normalized=new Vector2(pointer.x/screenSize.x,pointer.y/screenSize.y);
            float edge=Mathf.Clamp(profile.seatedLookEdgeFraction,.01f,.15f);
            float yaw=Edge(normalized.x,edge)*profile.seatedYawDegreesPerSecond;
            // Unity screen Y increases upwards: the top edge tilts up, the bottom edge tilts down.
            float pitch=-Edge(normalized.y,edge)*profile.seatedPitchDegreesPerSecond;
            if(Mathf.Abs(yaw)<.001f&&Mathf.Abs(pitch)<.001f)return false;
            intent=new DeskLookIntent(new Vector2(yaw,pitch)*Mathf.Min(deltaSeconds,.10f));return true;
        }
        static float Edge(float value,float band)
        {
            if(value<band)return -(1-value/band);
            if(value>1-band)return (value-(1-band))/band;
            return 0;
        }
    }
}
