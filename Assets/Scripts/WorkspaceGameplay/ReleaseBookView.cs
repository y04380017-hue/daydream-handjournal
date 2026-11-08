using System;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Replaceable real 3D book binding; anchors follow the same lower-left XY frame as journal pages.</summary>
    public sealed class ReleaseBookView : MonoBehaviour
    {
        public string bookId;
        public PhysicalInteractable physical;
        public Transform cover,hinge,spreadRoot;
        public Transform[] pageAnchors=Array.Empty<Transform>();
        public Vector2 pageSize=new Vector2(.105f,.148f);
        public MeshRenderer[] pages=Array.Empty<MeshRenderer>();
        Quaternion closedHinge;
        bool initialized;
        public bool IsOpen {get;private set;}
        public int FirstPage {get;private set;}

        void Awake()
        {
            if(hinge)closedHinge=hinge.localRotation;initialized=true;
        }
        void LateUpdate()
        {RefreshCollider();}

        void RefreshCollider()
        {
            if(!physical||!physical.interactionCollider)return;
            var footprint=physical.interactionCollider;footprint.enabled=physical.IsAccessible;
            if(!IsOpen){if(physical.dimensions)physical.dimensions.ApplyCollider(footprint);return;}
            bool first=true;Bounds bounds=default;
            foreach(var anchor in pageAnchors)
            {
                if(!anchor)continue;var localSize=LocalPageSize(anchor,pageSize);
                foreach(var corner in new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right})
                {
                    var p=transform.InverseTransformPoint(anchor.TransformPoint(new Vector3(localSize.x*corner.x,localSize.y*corner.y,0)));
                    if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                }
            }
            if(!first){footprint.center=bounds.center;footprint.size=Vector3.Max(bounds.size+new Vector3(.006f,.004f,.006f),new Vector3(.01f,.008f,.01f));}
        }

        public void Apply(CraftReleaseBookData data)
        {
            if(!initialized)Awake();
            IsOpen=data!=null&&data.isOpen;FirstPage=data!=null?data.currentPage:0;
            if(data!=null&&data.pageSizeMeters.x>0&&data.pageSizeMeters.y>0)pageSize=data.pageSizeMeters;
            if(spreadRoot)spreadRoot.gameObject.SetActive(IsOpen);
            else foreach(var page in pages)if(page)page.gameObject.SetActive(IsOpen);
            if(hinge)hinge.localRotation=closedHinge*Quaternion.AngleAxis(IsOpen?180:0,Vector3.forward);
            if(physical && physical.interactionCollider)physical.interactionCollider.enabled=!IsOpen && physical.IsAccessible;
            RefreshCollider();
        }

        public Transform AnchorFor(CraftReleaseBookData book,string pageId)
        {
            if(!IsOpen||book==null||book.pages==null)return null;
            for(int side=0;side<pageAnchors.Length;side++)
            {
                int index=book.currentPage+side;
                if(index>=0&&index<book.pages.Count&&book.pages[index].pageId==pageId)return pageAnchors[side];
            }
            return null;
        }

        public bool TryHit(CraftReleaseBookData book,Ray ray,out string pageId,out Vector2 uv,out float distance)
        {
            pageId=null;uv=default;distance=float.MaxValue;
            if(!IsOpen||book==null||book.pages==null)return false;
            for(int side=0;side<pageAnchors.Length;side++)
            {
                var anchor=pageAnchors[side];int index=book.currentPage+side;
                if(!anchor||index<0||index>=book.pages.Count)continue;
                var plane=new Plane(anchor.forward,anchor.position);
                if(!plane.Raycast(ray,out float t)||t<0||t>=distance)continue;
                Vector3 p=anchor.InverseTransformPoint(ray.GetPoint(t));
                Vector2 localSize=LocalPageSize(anchor,pageSize);
                Vector2 candidate=new Vector2(p.x/localSize.x,p.y/localSize.y);
                if(candidate.x<0||candidate.x>1||candidate.y<0||candidate.y>1)continue;
                distance=t;uv=candidate;pageId=book.pages[index].pageId;
            }
            return pageId!=null;
        }

        public static Vector2 LocalPageSize(Transform anchor,Vector2 metres)
        {return new Vector2(metres.x/Mathf.Max(.00001f,anchor.lossyScale.x),metres.y/Mathf.Max(.00001f,anchor.lossyScale.y));}
    }
}
