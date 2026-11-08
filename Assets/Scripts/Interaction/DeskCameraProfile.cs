using UnityEngine;
namespace VintageJournal
{
    public enum DeskViewKind { Desk, Overview, Journal, TapeRack, RightStorage, LeftDrawer, RightDrawer }

    [System.Serializable]
    public sealed class DeskViewPreset
    {
        public DeskViewKind kind;
        public string label;
        public Vector3 position, target;
        [Range(25,75)] public float fieldOfView = 50;
        public DeskViewPreset(DeskViewKind kind,string label,Vector3 position,Vector3 target,float fieldOfView)
        {this.kind=kind;this.label=label;this.position=position;this.target=target;this.fieldOfView=fieldOfView;}
    }

    [CreateAssetMenu(menuName="Vintage Journal/Desk Camera Profile")]
    public sealed class DeskCameraProfile : ScriptableObject
    {
        public Vector3 seatedPosition = new Vector3(0,1.48f,-.88f);
        public Vector3 seatedTarget = new Vector3(0,.76f,.025f);
        public float seatedFieldOfView = 52;
        public float minimumHorizontalAspect;
        public Vector3 journalPosition = new Vector3(0,1.14f,-.34f);
        public Vector3 journalTarget = new Vector3(0,.77f,-.06f);
        public float journalFieldOfView = 42;
        public bool openJournalAtDesk;
        public float transitionSeconds = .85f;
        public float panExtentX=.085f, panExtentZ=.05f;
        [Header("Controlled seated views; never a free camera")]
        public DeskViewPreset[] deskViews = DefaultDeskViews();
        [Header("Seated 360-degree mouse look; the viewpoint never walks or orbits")]
        public bool seatedLookEnabled = true;
        [Range(.01f,.15f)] public float seatedLookEdgeFraction = .035f;
        [Range(20,240)] public float seatedYawDegreesPerSecond = 110;
        [Range(5,120)] public float seatedPitchDegreesPerSecond = 42;
        [Range(-60,0)] public float minimumSeatedPitch = -26;
        [Range(0,50)] public float maximumSeatedPitch = 18;
        [Range(35,89)] public float collectionForwardHalfAngle = 80;

        public static DeskViewPreset[] DefaultDeskViews() => new[] {
            new DeskViewPreset(DeskViewKind.Overview,"设施总览",new Vector3(0,1.48f,-1.04f),new Vector3(0,.78f,.02f),60),
            new DeskViewPreset(DeskViewKind.Journal,"手账特写",new Vector3(.018f,1.13f,-.36f),new Vector3(.018f,.771f,-.125f),43),
            new DeskViewPreset(DeskViewKind.TapeRack,"左侧胶带架",new Vector3(-.32f,1.24f,-.61f),new Vector3(-.49f,.89f,.15f),46),
            new DeskViewPreset(DeskViewKind.RightStorage,"右侧本册与便签",new Vector3(.30f,1.24f,-.61f),new Vector3(.43f,.85f,.17f),47),
            new DeskViewPreset(DeskViewKind.LeftDrawer,"左抽屉 · 胶带内页",new Vector3(-.30f,1.00f,-1.00f),new Vector3(-.24f,.64f,-.478f),48),
            new DeskViewPreset(DeskViewKind.RightDrawer,"右抽屉 · 文具",new Vector3(.30f,1.00f,-1.00f),new Vector3(.24f,.64f,-.478f),48)
        };

        public DeskViewPreset FindDeskView(DeskViewKind kind)
        {
            if(kind==DeskViewKind.Desk)return new DeskViewPreset(kind,"桌面近景",seatedPosition,seatedTarget,seatedFieldOfView);
            if(deskViews!=null)foreach(var view in deskViews)if(view!=null&&view.kind==kind)return view;
            foreach(var view in DefaultDeskViews())if(view.kind==kind)return view;
            return null;
        }
    }
}
