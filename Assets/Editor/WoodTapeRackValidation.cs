using System.Linq;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static partial class WorkspaceStage4BValidation
    {
        static void ValidateWoodRack()
        {
            var rack=GameObject.Find("Wooden washi atelier");
            if(!rack)return;
            var old=GameObject.Find("WorkspaceStage4B").transform.Find("Archived previous left rack appearance");
            Check("Wood01","Previous animal and acrylic rack presentation is inactive",old&&!old.gameObject.activeInHierarchy&&
                !old.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled&&c.gameObject.activeInHierarchy),"Old source assets retained outside active presentation.");
            Check("Wood02","One cabinet holds all existing devices",rack.GetComponentsInChildren<WorkspaceShaft>().Length==15&&
                rack.GetComponentsInChildren<WorkspaceDrawer>().Length==3,"Three five-spindle rows, two tape trays and one release-book drawer.");
            var rolls=items.Where(i=>i.itemType==PhysicalItemType.Tape).ToArray();
            Check("Wood03","All 15 real rolls are upright in original reserved home IDs",rolls.Length==15&&rolls.All(i=>
                i.homeSlot && i.homeSlot.slotId=="stage4b-home-"+i.itemInstanceId&&Mathf.Abs(Vector3.Dot(i.transform.up,Vector3.right))>.99f),"No artwork replicas or duplicate display rolls.");
            bool overlaps=false;
            for(int i=0;i<rolls.Length;i++)for(int j=i+1;j<rolls.Length;j++)
                overlaps|=Physics.ComputePenetration(rolls[i].interactionCollider,rolls[i].transform.position,rolls[i].transform.rotation,
                    rolls[j].interactionCollider,rolls[j].transform.position,rolls[j].transform.rotation,out _,out float depth)&&depth>.0005f;
            Check("Wood04","Upright tape stock does not overlap",!overlaps,"Geometry checked across all pairs at actual world scale.");
            Check("Wood05","No transparent acrylic material remains in active rack geometry",rack.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).All(r=>
                r.sharedMaterials.All(m=>m && (!m.HasProperty("_Surface")||m.GetFloat("_Surface")==0))),"Opaque wood and satin fittings; original animals are archived.");
            Check("Wood06","Storage keys remain compatible with previous saves",shafts.All(s=>"stage4b-"+s.shaftId==s.slot.slotId.Replace("-slot-","-shaft-"))&&
                drawers.Select(d=>d.drawerId).OrderBy(s=>s).SequenceEqual(new[]{"drawer-release-books","drawer-tape-middle","drawer-tape-top"}),"All former slot/device keys retained despite the new appearance and world poses.");
        }
    }
}
