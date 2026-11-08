using UnityEngine;

namespace VintageJournal
{
    /// <summary>Fixed seated camera with a reversible, eased journal inspection.</summary>
    [DisallowMultipleComponent]
    public sealed class DeskCamera : MonoBehaviour
    {
        public DeskCameraProfile profile;
        public Vector3 inspectionPosition = new Vector3(0, 1.55f, -.9f);
        public Vector3 inspectionTarget = new Vector3(0, .80f, -.22f);
        [Range(.25f, 2f)] public float transitionSeconds = .85f;
        [Range(25f, 70f)] public float inspectionFieldOfView = 42f;
        public bool IsInspecting { get; private set; }
        public bool IsTransitioning { get; private set; }
        public Vector3 DefaultPosition { get; private set; }
        public float Zoom { get; private set; } = 1;
        public Vector2 PanOffset { get; private set; }
        public Camera Lens => lens;
        public float LayoutOffsetX {get;set;}
        public DeskViewKind ActiveDeskView {get;private set;} = DeskViewKind.Desk;
        public bool MouseLookEnabled {get;private set;} = true;
        public bool MouseLookAwaitingNeutral {get;private set;}
        public float SeatedYawDegrees {get;private set;}
        public float SeatedPitchDegrees {get;private set;}
        public bool HasSeatedLookOffset=>Mathf.Abs(SeatedYawDegrees)>.05f||Mathf.Abs(SeatedPitchDegrees)>.05f;
        public Vector3 SeatedForward=>Vector3.ProjectOnPlane(defaultRotation*Vector3.forward,Vector3.up).normalized;
        public bool CanLook=>profile&&profile.seatedLookEnabled&&MouseLookEnabled&&!IsInspecting&&!IsTransitioning&&ActiveDeskView==DeskViewKind.Desk;
        public bool ForwardFacing {
            get {
                if(IsTransitioning)return false;
                var direction=Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
                float cone=profile?profile.collectionForwardHalfAngle:80;
                return direction.sqrMagnitude>.0001f&&Vector3.Dot(SeatedForward,direction)>=Mathf.Cos(cone*Mathf.Deg2Rad);
            }
        }
        public bool CanCollectDirection(Vector3 worldPoint)
        {
            var direction=Vector3.ProjectOnPlane(worldPoint-DefaultPosition,Vector3.up).normalized;
            float cone=profile?profile.collectionForwardHalfAngle:80;
            return ForwardFacing&&direction.sqrMagnitude>.0001f&&Vector3.Dot(SeatedForward,direction)>=Mathf.Cos(cone*Mathf.Deg2Rad);
        }

        private Quaternion defaultRotation;
        private float defaultFieldOfView;
        private Vector3 fromPosition, toPosition;
        private Quaternion fromRotation, toRotation;
        private float fromFov, toFov, progress;
        private Camera lens;
        private float activeDeskFieldOfView;

        private void Awake()
        {
            lens = GetComponent<Camera>();
            if(profile)
            {
                transform.position=profile.seatedPosition;
                transform.rotation=Quaternion.LookRotation(profile.seatedTarget-profile.seatedPosition,Vector3.up);
                lens.fieldOfView=profile.seatedFieldOfView;
                inspectionPosition=profile.journalPosition;inspectionTarget=profile.journalTarget;
                inspectionFieldOfView=profile.journalFieldOfView;transitionSeconds=profile.transitionSeconds;
            }
            DefaultPosition = transform.position;
            defaultRotation = transform.rotation;
            defaultFieldOfView = lens ? lens.fieldOfView : 48f;
            activeDeskFieldOfView = defaultFieldOfView;
            if(!GetComponent<DeskViewControls>())gameObject.AddComponent<DeskViewControls>().cameraRig=this;
        }

        public void Inspect()
        {
            if (IsInspecting) return;
            IsInspecting = true;
            ResetView();
            var direction = inspectionTarget - inspectionPosition;
            var rotation = direction.sqrMagnitude > .00001f
                ? Quaternion.LookRotation(direction, Vector3.up) : defaultRotation;
            if (profile && profile.openJournalAtDesk) Transition(DefaultPosition, defaultRotation, defaultFieldOfView);
            else Transition(inspectionPosition, rotation, inspectionFieldOfView);
            Debug.Log("[Desk] Camera:Inspect");
        }

        public void Return()
        {
            if (!IsInspecting && !IsTransitioning && ActiveDeskView==DeskViewKind.Desk && !HasSeatedLookOffset) return;
            var ui = FindFirstObjectByType<DeskUI>();
            if (ui) ui.ConsumePointer();
            IsInspecting = false;
            ActiveDeskView=DeskViewKind.Desk;activeDeskFieldOfView=defaultFieldOfView;
            ResetView();
            Transition(DefaultPosition, defaultRotation, defaultFieldOfView);
            Debug.Log("[Desk] Camera:Return");
        }

        /// <summary>Input-independent named view request. Only this controller writes the actual camera.</summary>
        public bool SelectDeskView(DeskViewKind kind)
        {
            if(IsInspecting||IsTransitioning||!profile)return false;
            var view=profile.FindDeskView(kind);if(view==null)return false;
            var direction=view.target-view.position;if(direction.sqrMagnitude<.00001f)return false;
            ResetView();ActiveDeskView=kind;activeDeskFieldOfView=view.fieldOfView;
            Transition(view.position,Quaternion.LookRotation(direction,Vector3.up),view.fieldOfView);
            Debug.Log("[Desk] Camera:View:"+kind);
            return true;
        }

        public void SetMouseLookEnabled(bool enabled){MouseLookEnabled=enabled;if(enabled)MouseLookAwaitingNeutral=true;}
        public void ArmMouseLook(){MouseLookAwaitingNeutral=false;}
        /// <summary>Accepts a unified look intent. Rotation is applied only here, without changing the seated origin.</summary>
        public bool SubmitLook(DeskLookIntent intent)
        {
            if(intent.kind==DeskLookIntentKind.ResetForward){Return();return true;}
            if(intent.kind!=DeskLookIntentKind.Look||!CanLook||!Finite(intent.degrees.x)||!Finite(intent.degrees.y))return false;
            SeatedYawDegrees=Mathf.Repeat(SeatedYawDegrees+intent.degrees.x+180,360)-180;
            SeatedPitchDegrees=Mathf.Clamp(SeatedPitchDegrees+intent.degrees.y,profile.minimumSeatedPitch,profile.maximumSeatedPitch);
            return true;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        private void Transition(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            fromPosition = transform.position;
            fromRotation = transform.rotation;
            fromFov = lens ? lens.fieldOfView : defaultFieldOfView;
            toPosition = position;
            toRotation = rotation;
            toFov = FrameFieldOfView(fieldOfView);
            progress = 0;
            IsTransitioning = true;
        }

        float FrameFieldOfView(float vertical)
        {
            if (!profile || !lens || lens.aspect >= profile.minimumHorizontalAspect || lens.aspect <= 0) return vertical;
            return Mathf.Rad2Deg * 2 * Mathf.Atan(Mathf.Tan(vertical * Mathf.Deg2Rad * .5f) * profile.minimumHorizontalAspect / lens.aspect);
        }

        private void Update()
        {
            if (!IsTransitioning)
            {
                if (!IsInspecting && lens) lens.fieldOfView = FrameFieldOfView(activeDeskFieldOfView);
                if(!IsInspecting&&ActiveDeskView==DeskViewKind.Desk)
                    transform.rotation=Quaternion.AngleAxis(SeatedYawDegrees,Vector3.up)*defaultRotation*Quaternion.AngleAxis(SeatedPitchDegrees,Vector3.right);
                if (IsInspecting)
                {
                    float t=1-Mathf.Exp(-Time.unscaledDeltaTime*15);
                    float focus = profile && profile.openJournalAtDesk ? Mathf.SmoothStep(0, 1, (Zoom-1)*1.65f) : 1;
                    Vector3 position = Vector3.Lerp(DefaultPosition, inspectionPosition, focus);
                    transform.position = Vector3.Lerp(transform.position, position + new Vector3(PanOffset.x+LayoutOffsetX,0,PanOffset.y),t);
                    Quaternion rotation = Quaternion.LookRotation(inspectionTarget-inspectionPosition,Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation,Quaternion.Slerp(defaultRotation,rotation,focus),t);
                    lens.fieldOfView = Mathf.Lerp(lens.fieldOfView,FrameFieldOfView(Mathf.Lerp(defaultFieldOfView,inspectionFieldOfView,focus)/Zoom),t);
                }
                return;
            }
            progress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / Mathf.Max(.05f, transitionSeconds));
            float eased = progress * progress * (3f - 2f * progress);
            transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, eased),
                Quaternion.Slerp(fromRotation, toRotation, eased));
            if (lens) lens.fieldOfView = Mathf.Lerp(fromFov, toFov, eased);
            if (progress >= 1f)
            {
                IsTransitioning = false;
                if (!IsInspecting)
                {
                    var input = FindFirstObjectByType<DeskInteraction>();
                    if (input && input.ui && input.ui.HasBegun && !input.ui.IsBlocking) input.LogVisibleTargets();
                }
            }
        }

        public void ZoomBy(float delta)
        {
            if (!IsInspecting || IsTransitioning) return;
            Zoom=Mathf.Clamp(Zoom+delta,1,2); ClampPan();
        }
        public void Pan(Vector2 delta)
        {
            if (!IsInspecting || IsTransitioning || Zoom<=1.01f) return;
            PanOffset+=delta; ClampPan();
        }
        void ClampPan()
        {
            float t=Zoom-1;
            float x=profile?profile.panExtentX:.22f,z=profile?profile.panExtentZ:.13f;
            PanOffset=new Vector2(Mathf.Clamp(PanOffset.x,-x*t,x*t),Mathf.Clamp(PanOffset.y,-z*t,z*t));
        }
        public void ResetView(){ Zoom=1; PanOffset=Vector2.zero; LayoutOffsetX=0; SeatedYawDegrees=0;SeatedPitchDegrees=0;MouseLookAwaitingNeutral=true; }
    }
}
