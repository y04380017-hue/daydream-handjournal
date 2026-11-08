using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Metric textured surfaces for real release-book pages and held material. Owns no inventory state.</summary>
    public sealed class CraftMaterialRenderer : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        sealed class Visual
        {public GameObject root;public Mesh mesh;public MeshRenderer renderer;public Material material;}
        readonly Dictionary<string,Visual> visuals=new Dictionary<string,Visual>();
        readonly HashSet<string> live=new HashSet<string>();
        Visual held,lengthPreview;
        public int VisibleCount=>visuals.Values.Count(v=>v.root&&v.root.activeSelf);
        public Transform GetVisual(string id)=>visuals.TryGetValue(id,out var visual)?visual.root.transform:null;
        public Renderer GetRenderer(string id)=>visuals.TryGetValue(id,out var visual)?visual.renderer:null;

        public void Refresh()
        {
            if(!workspace||!workspace.Ready)return;
            live.Clear();
            foreach(var item in workspace.Service.Items)
            {
                if(item.owner!=CraftOwner.ReleaseBookPage)continue;
                var view=workspace.releaseBooks.FirstOrDefault(v=>v&&v.bookId==item.containerId);
                var book=workspace.Service.FindBook(item.containerId);
                var anchor=view?view.AnchorFor(book,item.pageId):null;
                if(!anchor)continue;
                var definition=workspace.scrapbook.Catalog.Find(item.assetId);if(definition==null||!definition.texture)continue;
                live.Add(item.instanceId);
                if(!visuals.TryGetValue(item.instanceId,out var visual)){visual=Create("Release material "+item.instanceId);visuals.Add(item.instanceId,visual);}
                DrawAttached(visual,item,anchor,view.pageSize,definition,item.position,item.rotationDegrees,item.scale,1,false);
            }
            foreach(var pair in visuals)pair.Value.root.SetActive(live.Contains(pair.Key));
        }

        void LateUpdate()
        {
            if(!workspace||!workspace.Ready)return;
            foreach(var item in workspace.Service.Items)
            {
                if(!visuals.TryGetValue(item.instanceId,out var visual)||!visual.root.activeSelf)continue;
                bool selected=workspace.SelectedMaterialId==item.instanceId;
                SetCorner(visual,selected?.0012f:0);
                visual.material.SetColor("_BaseColor",selected?new Color(1,.97f,.86f,1):Color.white);
            }
            DrawHeld();DrawLength();
        }

        void DrawHeld()
        {
            var item=workspace.Service.HeldItem;
            if(item==null){if(held!=null)held.root.SetActive(false);return;}
            var definition=workspace.scrapbook.Catalog.Find(item.assetId);if(definition==null)return;
            if(held==null)held=Create("Held material preview");
            held.root.SetActive(true);var placement=workspace.PreviewPlacement;
            Transform anchor=null;Vector2 page=default;
            if(placement!=null&&placement.owner==CraftOwner.ReleaseBookPage)
            {
                var view=workspace.releaseBooks.FirstOrDefault(v=>v&&v.bookId==placement.containerId);
                if(view){anchor=view.AnchorFor(workspace.Service.FindBook(view.bookId),placement.pageId);page=view.pageSize;}
            }
            else if(placement!=null&&placement.owner==CraftOwner.JournalPage)
                foreach(JournalSide side in new[]{JournalSide.Left,JournalSide.Right})
                    if(workspace.journal.Document.GetPage(side).pageId==placement.pageId){anchor=workspace.journal.pageView.GetSurface(side);page=workspace.JournalMetres(side);}
            if(anchor)
                DrawAttached(held,item,anchor,page,definition,placement.position,item.rotationDegrees,item.scale,.65f,true);
            else
            {
                held.root.transform.SetParent(null,true);
                held.root.transform.SetPositionAndRotation(workspace.PointerWorld+Vector3.up*.009f,Quaternion.Euler(90,0,item.rotationDegrees));
                held.root.transform.localScale=Vector3.one;
                ApplyArt(held,definition,WorkspaceGameplayController.MetricSize(item)*item.scale,.8f,item.kind==CraftItemKind.TapeStrip);
            }
            held.material.SetColor("_BaseColor",placement!=null&&!workspace.PreviewFits?new Color(1,.48f,.38f):Color.white);
            SetCorner(held,.002f);
            if(Time.unscaledTime<workspace.TearAnimationUntil)
            {
                float pulse=Mathf.Sin((workspace.TearAnimationUntil-Time.unscaledTime)/.22f*Mathf.PI);
                held.root.transform.position+=Vector3.up*pulse*.009f;
            }
        }

        void DrawLength()
        {
            if(!workspace.TapeLengthSelecting||!workspace.SelectedTape){if(lengthPreview!=null)lengthPreview.root.SetActive(false);return;}
            var definition=workspace.scrapbook.Catalog.Find(workspace.SelectedTape.assetId);if(definition==null)return;
            if(lengthPreview==null)lengthPreview=Create("Continuous tape length preview");
            lengthPreview.root.SetActive(true);lengthPreview.root.transform.SetParent(null,true);
            var surface=workspace.physical.surface.transform;
            Vector3 forward=-surface.forward;
            lengthPreview.root.transform.SetPositionAndRotation(workspace.TapePreviewOrigin+forward*workspace.TapeLengthMeters*.5f,
                Quaternion.LookRotation(-surface.up,Vector3.Cross(-surface.up,forward)));
            lengthPreview.root.transform.localScale=Vector3.one;
            float width=workspace.SelectedTape.dimensions?workspace.SelectedTape.dimensions.size.y:.015f;
            ApplyArt(lengthPreview,definition,new Vector2(workspace.TapeLengthMeters,width),.9f,true);SetCorner(lengthPreview,.0008f);
        }

        void DrawAttached(Visual visual,CraftItemRecord item,Transform anchor,Vector2 metres,DecorationDefinition definition,
            Vector2 uv,float rotation,float scale,float opacity,bool preview)
        {
            visual.root.SetActive(true);visual.root.transform.SetParent(anchor,false);
            var local=ReleaseBookView.LocalPageSize(anchor,metres);
            visual.root.transform.localPosition=new Vector3(uv.x*local.x,uv.y*local.y,-.0014f-Mathf.Max(0,item.sortOrder)*.00002f-(preview?.0008f:0));
            visual.root.transform.localRotation=Quaternion.Euler(0,0,rotation);visual.root.transform.localScale=Vector3.one;
            Vector2 size=WorkspaceGameplayController.MetricSize(item)*scale;
            var rootScale=visual.root.transform.lossyScale;
            ApplyArt(visual,definition,new Vector2(size.x/Mathf.Max(.00001f,rootScale.x),size.y/Mathf.Max(.00001f,rootScale.y)),opacity,item.kind==CraftItemKind.TapeStrip);
            // Texture repetition uses physical aspect, independent of a replacement hierarchy's display scale.
            SetRepeat(visual,definition,size,item.kind==CraftItemKind.TapeStrip);
            visual.renderer.sortingOrder=Mathf.Clamp(item.sortOrder,0,650)+1;
        }

        Visual Create(string name)
        {
            var v=new Visual{root=new GameObject(name)};
            v.mesh=new Mesh{name=name+" independent quad"};SetCorner(v,0);
            v.root.AddComponent<MeshFilter>().sharedMesh=v.mesh;
            v.renderer=v.root.AddComponent<MeshRenderer>();v.renderer.shadowCastingMode=ShadowCastingMode.Off;v.renderer.receiveShadows=false;
            Shader shader=workspace.scrapbook.Catalog.paperShader;
            if(!shader)shader=Shader.Find("VintageJournal/ScrapbookPaper");
            if(!shader)shader=Shader.Find("Universal Render Pipeline/Unlit");
            v.material=new Material(shader){name=name+" runtime material"};v.renderer.sharedMaterial=v.material;
            return v;
        }

        static void ApplyArt(Visual v,DecorationDefinition definition,Vector2 size,float opacity,bool tape)
        {
            v.root.transform.localScale=new Vector3(size.x,size.y,1);
            v.material.SetTexture("_BaseMap",definition.texture);v.material.SetFloat("_IsTape",tape?1:0);
            v.material.SetFloat("_Opacity",opacity);v.material.SetColor("_BaseColor",Color.white);
            v.material.SetFloat("_Shadow",0);SetRepeat(v,definition,size,tape);
            v.material.renderQueue=3100;
        }
        static void SetRepeat(Visual visual,DecorationDefinition definition,Vector2 size,bool tape)
        {float aspect=definition.texture&&definition.texture.height>0?(float)definition.texture.width/definition.texture.height:1;visual.material.SetFloat("_UVRepeat",tape?size.x/Mathf.Max(.00001f,size.y)/aspect:1);}
        static void SetCorner(Visual visual,float height)
        {
            visual.mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,-height),new Vector3(.5f,-.5f,0)};
            visual.mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};visual.mesh.triangles=new[]{0,1,2,0,2,3};visual.mesh.RecalculateNormals();visual.mesh.RecalculateBounds();
        }
        void OnDestroy()
        {foreach(var visual in visuals.Values)Dispose(visual);Dispose(held);Dispose(lengthPreview);}
        static void Dispose(Visual visual)
        {if(visual==null)return;if(visual.root)Destroy(visual.root);if(visual.material)Destroy(visual.material);if(visual.mesh)Destroy(visual.mesh);}
    }
}
