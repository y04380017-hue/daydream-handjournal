using UnityEngine;
namespace VintageJournal
{
    /// <summary>A bounded prismatic joint driven by pointer or future gesture intent.</summary>
    public sealed class WorkspaceDrawer : MonoBehaviour
    {
        public string drawerId;
        public Transform slide;
        public Vector3 closedLocalPosition;
        public Vector3 slideAxis=Vector3.back;
        public float travel=.16f;
        public PhysicalContainer container;
        [SerializeField,Range(0,1)] float openAmount;
        public float OpenAmount=>openAmount;
        public bool IsDragging{get;private set;}
        Vector2 startPointer,screenAxis;
        Vector3 startWorld,dragAxisWorld;
        Plane dragPlane;
        float startAmount;
        bool dragged;
        public bool SetOpenAmount(float value)
        {
            if(!float.IsFinite(value)||!slide)return false;
            value=Mathf.Clamp01(value);
            if(container)
            {
                bool accessible=value>.75f;
                if(!accessible && container.isOpen && !container.SetOpen(false))return false;
                if(accessible&&!container.isOpen)container.SetOpen(true);
            }
            openAmount=value;
            slide.localPosition=closedLocalPosition+slideAxis.normalized*Mathf.Max(.001f,travel)*value;
            return true;
        }
        public bool Toggle()=>SetOpenAmount(openAmount>.5f?0:1);
        public bool BeginDrag(Vector2 screen,Camera lens)
        {
            if(!slide||!lens)return false;
            startPointer=screen;startAmount=openAmount;dragged=false;
            Vector3 axis=slide.parent?slide.parent.TransformDirection(slideAxis.normalized):slideAxis.normalized;
            screenAxis=(Vector2)(lens.WorldToScreenPoint(slide.position+axis*travel)-lens.WorldToScreenPoint(slide.position));
            dragAxisWorld=axis;Vector3 normal=Vector3.ProjectOnPlane(lens.transform.forward,axis);
            if(normal.sqrMagnitude<.001f)normal=Vector3.ProjectOnPlane(lens.transform.up,axis);
            var ray=lens.ScreenPointToRay(screen);Vector3 anchor=slide.position;
            if(Physics.Raycast(ray,out var hit,3)&&hit.collider.GetComponentInParent<WorkspaceDrawer>()==this)anchor=hit.point;
            dragPlane=new Plane(normal.normalized,anchor);
            IsDragging=screenAxis.sqrMagnitude>4&&dragPlane.Raycast(ray,out float distance);
            if(IsDragging){dragPlane.Raycast(ray,out float distance2);startWorld=ray.GetPoint(distance2);}return IsDragging;
        }
        public void DragTo(Vector2 screen,Camera lens)
        {
            if(!IsDragging)return;
            Vector2 delta=screen-startPointer;dragged|=delta.sqrMagnitude>25;
            var ray=lens.ScreenPointToRay(screen);
            if(dragPlane.Raycast(ray,out float distance))SetOpenAmount(startAmount+Vector3.Dot(ray.GetPoint(distance)-startWorld,dragAxisWorld)/Mathf.Max(.001f,travel));
        }
        public void EndDrag(){if(!IsDragging)return;IsDragging=false;if(!dragged)Toggle();Debug.Log("[WorkspaceDrawer] "+drawerId+":"+openAmount.ToString("F3"));}
        void Start(){SetOpenAmount(openAmount);}
        void OnDisable(){IsDragging=false;}
    }
}
