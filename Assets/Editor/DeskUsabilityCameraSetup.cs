using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace VintageJournal.Editor
{
    /// <summary>Called by the incremental workspace authoring pass; never rebuilds the scene.</summary>
    public static class DeskUsabilityCameraSetup
    {
        public static void Apply()
        {
            var camera=Object.FindFirstObjectByType<DeskCamera>();
            if(!camera||!camera.profile)throw new System.InvalidOperationException("A configured DeskCamera is required.");
            var profile=camera.profile;
            profile.seatedPosition=new Vector3(0,1.28f,-.82f);profile.seatedTarget=new Vector3(0,.78f,-.02f);profile.seatedFieldOfView=60;
            profile.journalPosition=new Vector3(.018f,1.13f,-.36f);profile.journalTarget=new Vector3(.018f,.771f,-.125f);profile.journalFieldOfView=43;
            profile.openJournalAtDesk=false;profile.minimumHorizontalAspect=16f/9f;profile.deskViews=DeskCameraProfile.DefaultDeskViews();
            DisableHiddenRoomColliders();
            foreach(var passer in Object.FindObjectsByType<DreamSkyPasser>(FindObjectsInactive.Include,FindObjectsSortMode.None)){
                CalibratePasser(passer,profile);EditorUtility.SetDirty(passer);
                if(PrefabUtility.IsPartOfPrefabInstance(passer))PrefabUtility.RecordPrefabInstancePropertyModifications(passer);
            }
            CalibrateFrames(profile);
            EditorUtility.SetDirty(profile);EditorUtility.SetDirty(camera);
            if(PrefabUtility.IsPartOfPrefabInstance(camera))PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        }
        static void DisableHiddenRoomColliders()
        {
            var room=GameObject.Find("RoomAndWindow");if(!room)return;
            var audit=new List<string>{"Scene authoring audit: archived invisible room geometry must not intercept floating-material rays. Objects/assets are retained; physical desk and storage are outside this room root."};
            foreach(var collider in room.GetComponentsInChildren<Collider>(true)){
                // This is an authored visual-shell correction, not a runtime rule to raycast through real furniture.
                var renderer=collider.GetComponent<Renderer>();
                if(!renderer||renderer.enabled||collider.GetComponentInParent<PhysicalInteractable>()||collider.GetComponentInParent<PhysicalContainer>())continue;
                bool wasEnabled=collider.enabled;collider.enabled=false;EditorUtility.SetDirty(collider);
                if(PrefabUtility.IsPartOfPrefabInstance(collider))PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                audit.Add(collider.name+" | "+collider.GetType().Name+" | previouslyEnabled="+wasEnabled+" | enabled=false");
            }
            Directory.CreateDirectory("docs/desk-usability");File.WriteAllLines("docs/desk-usability/hidden-room-collider-audit.txt",audit);
        }
        [System.Serializable] sealed class FrameAudit
        {
            public string name;
            public Vector3 adapterPosition,adapterScale,boundsCenter,boundsSize;
            public Vector3[] viewportCorners;
            public float minU,maxU,minV,maxV;
            public bool insideViewportAndBelowHeader;
        }
        [System.Serializable] sealed class FrameAuditSet {public string scope;public FrameAudit[] frames;}
        static Vector3 Project(DeskCameraProfile profile,Vector3 world)
        {
            var rotation=Quaternion.LookRotation(profile.seatedTarget-profile.seatedPosition,Vector3.up);
            var local=Quaternion.Inverse(rotation)*(world-profile.seatedPosition);
            float h=Mathf.Tan(profile.seatedFieldOfView*Mathf.Deg2Rad*.5f);
            return new Vector3(.5f+local.x/(2*local.z*h*(16f/9f)),.5f+local.y/(2*local.z*h),local.z);
        }
        static Bounds FrameBounds(Transform frame)
        {
            var renderers=frame.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)throw new System.InvalidOperationException("A floating frame has no renderer: "+frame.name);
            var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;
        }
        static void CalibrateFrames(DeskCameraProfile profile)
        {
            var room=GameObject.Find("RoomAndWindow")?.transform;if(!room)return;
            var audits=new List<FrameAudit>();
            string[] names={"Field study 1","Field study 2","BotanicalFrame"};float[] centers={.62f,.74f,.86f};
            for(int index=0;index<names.Length;index++){
                var frame=room.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==names[index]);if(!frame)continue;
                string adapterName="Desk frame size adapter "+(index+1);
                var adapter=room.Find(adapterName);
                if(!adapter){adapter=new GameObject(adapterName).transform;adapter.SetParent(room,false);frame.SetParent(adapter,true);}
                // The art root retains its authored local transform. Reset the independent adapter before measuring,
                // so re-running this pass never compounds a previous scale or height change.
                adapter.localPosition=Vector3.zero;adapter.localRotation=Quaternion.identity;adapter.localScale=Vector3.one;
                var authored=FrameBounds(frame);float scale=.27f/Mathf.Max(.001f,authored.size.y);adapter.localScale=Vector3.one*scale;
                var resized=FrameBounds(frame);
                var rotation=Quaternion.LookRotation(profile.seatedTarget-profile.seatedPosition,Vector3.up);
                float h=Mathf.Tan(profile.seatedFieldOfView*Mathf.Deg2Rad*.5f);
                var direction=rotation*new Vector3((centers[index]*2-1)*h*16f/9f,(.81f*2-1)*h,1);
                var target=profile.seatedPosition+direction*((1.105f-profile.seatedPosition.z)/direction.z);
                adapter.position+=target-resized.center;
                var bounds=FrameBounds(frame);var corners=new List<Vector3>();
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                    corners.Add(Project(profile,bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))));
                var audit=new FrameAudit{name=names[index],adapterPosition=adapter.position,adapterScale=adapter.localScale,boundsCenter=bounds.center,boundsSize=bounds.size,viewportCorners=corners.ToArray(),minU=corners.Min(p=>p.x),maxU=corners.Max(p=>p.x),minV=corners.Min(p=>p.y),maxV=corners.Max(p=>p.y)};
                audit.insideViewportAndBelowHeader=corners.All(p=>p.z>0&&p.x>.02f&&p.x<.98f&&p.y>.15f&&p.y<.90f);
                audits.Add(audit);EditorUtility.SetDirty(adapter);
                foreach(var collider in frame.GetComponentsInChildren<Collider>(true))if(collider.enabled){collider.enabled=false;EditorUtility.SetDirty(collider);}
                if(!audit.insideViewportAndBelowHeader)throw new System.InvalidOperationException("Floating frame is outside its header-safe rectangle: "+names[index]);
            }
            Directory.CreateDirectory("docs/desk-usability");
            File.WriteAllText("docs/desk-usability/frame-viewport-audit.json",JsonUtility.ToJson(new FrameAuditSet{scope="Authored renderer-bounds eight-corner projection for the configured default 16:9 seated view; actual rendered visibility is verified separately in Play/Windows.",frames=audits.ToArray()},true));
        }
        static void CalibratePasser(DreamSkyPasser passer,DeskCameraProfile profile)
        {
            if(!passer.visual)return;
            var rotation=Quaternion.LookRotation(profile.seatedTarget-profile.seatedPosition,Vector3.up);
            Vector3 World(float u,float v,float z){float h=Mathf.Tan(profile.seatedFieldOfView*Mathf.Deg2Rad*.5f);var direction=rotation*new Vector3((u*2-1)*h*16f/9f,(v*2-1)*h,1);return profile.seatedPosition+direction*((z-profile.seatedPosition.z)/direction.z);}
            float depth=Mathf.Max(2.8f,(passer.from.z+passer.to.z)*.5f);
            bool right=passer.from.x>passer.to.x;
            float v=passer.nightOnly ? .72f : .70f;
            var from=World(right?1.08f:-.08f,v,depth);var to=World(right?-.08f:1.08f,v+.025f,depth);
            passer.from=passer.transform.parent?passer.transform.parent.InverseTransformPoint(from):from;
            passer.to=passer.transform.parent?passer.transform.parent.InverseTransformPoint(to):to;
            passer.bob=Mathf.Min(passer.bob,.025f);
        }
    }
}
