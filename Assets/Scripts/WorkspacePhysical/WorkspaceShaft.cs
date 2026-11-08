using UnityEngine;
namespace VintageJournal
{
    /// <summary>Thin horizontal removable spindle. Its slot is accessible only after withdrawal.</summary>
    public sealed class WorkspaceShaft : MonoBehaviour
    {
        public string shaftId;
        public Transform rod;
        public Vector3 closedLocalPosition;
        public Vector3 slideAxis=Vector3.right;
        public float travel=.045f;
        public PhysicalStorageSlot slot;
        [SerializeField,Range(0,1)] float openAmount;
        public float OpenAmount=>openAmount;
        public bool IsExtended=>openAmount>.9f;
        public bool IsDragging{get;private set;}
        Vector2 startPointer,screenAxis;
        Vector3 startWorld,dragAxisWorld;
        Plane dragPlane;
        float startAmount;
        bool dragged;
        public bool SetOpenAmount(float value)
        {
            if(!float.IsFinite(value)||!rod)return false;
            openAmount=Mathf.Clamp01(value);
            rod.localPosition=closedLocalPosition+slideAxis.normalized*Mathf.Max(.001f,travel)*openAmount;
            return true;
        }
        public bool SetExtended(bool value)=>SetOpenAmount(value?1:0);
        public bool Toggle()=>SetExtended(!IsExtended);
        public bool BeginDrag(Vector2 screen,Camera lens)
        {
            if(!rod||!lens)return false;
            startPointer=screen;startAmount=openAmount;dragged=false;
            Vector3 axis=rod.parent?rod.parent.TransformDirection(slideAxis.normalized):slideAxis.normalized;
            screenAxis=(Vector2)(lens.WorldToScreenPoint(rod.position+axis*travel)-lens.WorldToScreenPoint(rod.position));
            dragAxisWorld=axis;Vector3 normal=Vector3.ProjectOnPlane(lens.transform.forward,axis);
            if(normal.sqrMagnitude<.001f)normal=Vector3.ProjectOnPlane(lens.transform.up,axis);
            var ray=lens.ScreenPointToRay(screen);Vector3 anchor=rod.position;
            if(Physics.Raycast(ray,out var hit,3)&&hit.collider.GetComponentInParent<WorkspaceShaft>()==this)anchor=hit.point;
            dragPlane=new Plane(normal.normalized,anchor);
            IsDragging=screenAxis.sqrMagnitude>4&&dragPlane.Raycast(ray,out float distance);
            if(IsDragging){dragPlane.Raycast(ray,out float distance2);startWorld=ray.GetPoint(distance2);}return IsDragging;
        }
        public void DragTo(Vector2 screen,Camera lens)
        {
            if(!IsDragging)return;Vector2 delta=screen-startPointer;dragged|=delta.sqrMagnitude>25;
            var ray=lens.ScreenPointToRay(screen);
            if(dragPlane.Raycast(ray,out float distance))SetOpenAmount(startAmount+Vector3.Dot(ray.GetPoint(distance)-startWorld,dragAxisWorld)/Mathf.Max(.001f,travel));
        }
        public void EndDrag(){if(!IsDragging)return;IsDragging=false;if(!dragged)Toggle();Debug.Log("[WorkspaceShaft] "+shaftId+":"+IsExtended);}
        void OnDisable(){IsDragging=false;}
    }
}
