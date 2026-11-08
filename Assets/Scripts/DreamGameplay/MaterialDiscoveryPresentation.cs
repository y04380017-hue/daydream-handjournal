using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>A non-modal material card and a world-space selection ring. Owns no inventory or hardware input.</summary>
    public sealed class MaterialDiscoveryPresentation : MonoBehaviour
    {
        public static MaterialDiscoveryPresentation Instance { get; private set; }
        public WorkspaceGameplayController workspace;
        public string AssetId { get; private set; }
        public string TokenId { get; private set; }
        public string EarnedInstanceId { get; private set; }
        public string Introduction => introduction;
        public bool IsReward => reward;
        public Texture PreviewTexture { get; private set; }
        public bool IsOpen { get; private set; }
        public bool HaloVisible => haloRoot && haloRoot.gameObject.activeSelf;
        public Vector3 HaloTargetWorldPosition { get; private set; }
        public bool UsesPaperPlane { get; private set; }
        public Vector3 PaperPlaneOrigin { get; private set; }
        public Vector3 HaloPlaneNormal { get; private set; }
        /// <summary>Conservative signed clearance including the wider soft ribbon's half-width.</summary>
        public float MinimumPaperPlaneClearance { get; private set; }
        public Vector3[] HaloWorldPoints
        {
            get
            {
                if(!HaloVisible||!haloCore)return Array.Empty<Vector3>();
                var points=new Vector3[haloCore.positionCount];
                for(int i=0;i<points.Length;i++)points[i]=haloRoot.TransformPoint(haloCore.GetPosition(i));
                return points;
            }
        }
        public Rect CardRect { get; private set; } = new Rect(24,586,540,230);
        string title, introduction, observedSelection;
        string selectionInstance;
        int observedRevision=-1;
        bool reward;
        bool cardFits=true;
        float haloUntil;
        Collider targetCollider;
        Renderer targetRenderer;
        PhysicalInteractable physicalTarget;
        Bounds fallbackBounds;
        Transform haloRoot;
        LineRenderer haloCore, haloSoft;
        Material coreMaterial, softMaterial;
        Font font;
        GUIStyle label, heading, close;
        bool Available => workspace && workspace.Ready && workspace.desk.HasBegun && !workspace.desk.IsBlocking &&
            !AtelierTutorial.Blocking && !(AtelierHub.Instance && AtelierHub.Instance.IsOpen) &&
            !(CommissionDeskController.Instance && CommissionDeskController.Instance.PanelOpen) &&
            !(DreamGameplayController.Instance && DreamGameplayController.Instance.BagOpen) &&
            !workspace.scrapbook.BrowserOpen && workspace.scrapbook.Chosen==null &&
            !(DrawerStockIndex.Instance && DrawerStockIndex.Instance.Expanded && DrawerStockIndex.Instance.IsVisible) &&
            !(DeskViewControls.Instance && DeskViewControls.Instance.Expanded) && !workspace.InteractionBusy;

        void Awake() { Instance=this; }
        public bool Contains(Vector2 pixel) => Available && IsOpen && cardFits &&
            CardRect.Contains(new Vector2(pixel.x*1920/Screen.width,(Screen.height-pixel.y)*1080/Screen.height));
        public void Close() { IsOpen=false; workspace?.desk.ConsumePointer(); RuntimeInputGate.Consume(); }

        public void ShowReward(DreamCatchToken token, Collider source)
        {
            if(token==null||!NightMaterialCatalog.IsKnown(token.assetId))return;
            AssetId=token.assetId;TokenId=token.tokenId;EarnedInstanceId="dream-catch-"+token.tokenId;
            title=NightMaterialCatalog.NameFor(AssetId);PreviewTexture=NightMaterialCatalog.Find(AssetId)?.thumbnail;
            string rarity=token.rarity>1?"稀有":token.rarity>0?"特别":"普通";
            introduction="来源："+AtelierProgressCatalog.SettingName(token.setting)+"漂浮礼物 · "+rarity+"\n"+
                "氛围："+string.Join(" / ",NightMaterialCatalog.TagsFor(AssetId))+"\n"+Description(AssetId)+"\n已收进素材袋 ×1；贴下、揭取仍是同一份。";
            reward=true;selectionInstance=null;observedSelection=CurrentSelectionKey();observedRevision=workspace.SelectionRevision;
            targetCollider=source;targetRenderer=null;physicalTarget=null;
            fallbackBounds=source?source.bounds:new Bounds(workspace.PointerWorld,Vector3.one*.05f);
            haloUntil=Time.unscaledTime+1.4f;IsOpen=true;
            Debug.Log("[MaterialDiscovery] Reward token="+TokenId+" asset="+AssetId+" instance="+EarnedInstanceId);
        }

        string CurrentSelectionKey()
        {
            // Legacy page selection is authoritative while editing; a previously selected desk
            // roll must not keep its card when the player selects a decoration in the journal.
            if(workspace.journal.IsEditing)
            {
                string pageSelection=workspace.scrapbook.SelectedId??workspace.SelectedMaterialId;
                return string.IsNullOrEmpty(pageSelection)?null:"material:"+pageSelection;
            }
            var physical=workspace.SelectedPhysical;
            if(physical&&physical.gameObject.activeInHierarchy&&physical.CurrentState!=PhysicalItemState.Disabled&&
                (physical.itemType==PhysicalItemType.Tape||physical.itemType==PhysicalItemType.Sticker||physical.itemType==PhysicalItemType.Note||physical.itemType==PhysicalItemType.LoosePage))
                return "physical:"+physical.itemInstanceId;
            string id=workspace.SelectedMaterialId??workspace.scrapbook.SelectedId;
            return string.IsNullOrEmpty(id)?null:"material:"+id;
        }
        void Update()
        {
            if(!workspace||!workspace.Ready)return;
            string key=CurrentSelectionKey();
            if(key!=observedSelection||observedRevision!=workspace.SelectionRevision)
            {
                observedSelection=key;observedRevision=workspace.SelectionRevision;
                if(key!=null)SelectMaterial(key);
                else if(!reward){IsOpen=false;selectionInstance=null;}
            }
            bool show=Available && (reward?Time.unscaledTime<haloUntil:selectionInstance!=null);
            if(!show){if(haloRoot)haloRoot.gameObject.SetActive(false);return;}
            var bounds=TargetBounds();
            var camera=workspace.journal.cameraRig;
            if(!camera||!camera.CanCollectDirection(bounds.center)){if(haloRoot)haloRoot.gameObject.SetActive(false);return;}
            EnsureHalo();haloRoot.gameObject.SetActive(true);
            HaloTargetWorldPosition=bounds.center;
            var lens=workspace.lens.transform;
            UsesPaperPlane=TryPaperPlane(out var planeRotation,out var planeNormal,out var planeOrigin);
            Quaternion ringRotation=UsesPaperPlane?planeRotation:lens.rotation;
            HaloPlaneNormal=UsesPaperPlane?planeNormal:-lens.forward;
            PaperPlaneOrigin=UsesPaperPlane?planeOrigin:bounds.center;
            Vector3 centre=UsesPaperPlane?bounds.center-planeNormal*Vector3.Dot(bounds.center-planeOrigin,planeNormal)+planeNormal*.0045f:
                bounds.center-lens.forward*.004f;
            haloRoot.SetPositionAndRotation(centre,ringRotation);
            Vector3 e=bounds.extents;
            // An ellipse around rectangular paper must clear its corners; a 1.45 factor
            // exceeds sqrt(2), without changing the underlying material's metric size.
            float padding=UsesPaperPlane?1.45f:1.15f;
            float rx=Mathf.Max(.016f,ProjectedExtent(e,ringRotation*Vector3.right)*padding+.003f);
            float ry=Mathf.Max(.016f,ProjectedExtent(e,ringRotation*Vector3.up)*padding+.003f);
            MinimumPaperPlaneClearance=UsesPaperPlane?float.MaxValue:0;
            for(int i=0;i<72;i++)
            {
                float angle=i*Mathf.PI*2/72;var p=new Vector3(Mathf.Cos(angle)*rx,Mathf.Sin(angle)*ry,0);haloCore.SetPosition(i,p);haloSoft.SetPosition(i,p);
                if(UsesPaperPlane)MinimumPaperPlaneClearance=Mathf.Min(MinimumPaperPlaneClearance,
                    Vector3.Dot(haloRoot.TransformPoint(p)-planeOrigin,planeNormal));
            }
            if(UsesPaperPlane)MinimumPaperPlaneClearance-=Mathf.Max(haloCore.widthMultiplier,haloSoft.widthMultiplier)*.5f;
            float pulse=.82f+.18f*Mathf.Sin(Time.unscaledTime*4);
            coreMaterial.SetColor("_BaseColor",new Color(.98f,.85f,.42f,1));
            softMaterial.SetColor("_BaseColor",new Color(.72f,1,.86f,.26f*pulse));
            // Keep the card away from the selected world object so clicking it never prevents a subsequent drag.
            var sourceArea=VirtualBounds(bounds);
            cardFits=false;
            var candidates=workspace.journal.IsEditing?
                new[]{new Rect(600,174,720,238),new Rect(1356,586,540,230),new Rect(24,586,540,230)}:
                new[]{new Rect(24,586,540,230),new Rect(600,174,720,238),new Rect(1356,586,540,230)};
            foreach(var candidate in candidates)
                if(!candidate.Overlaps(sourceArea)&&ClearOfExistingControls(candidate)){CardRect=candidate;cardFits=true;break;}
        }
        bool TryPaperPlane(out Quaternion rotation,out Vector3 normal,out Vector3 origin)
        {
            rotation=Quaternion.identity;normal=Vector3.up;origin=default;
            if(reward)return false;
            if(physicalTarget&&targetCollider&&
                (physicalTarget.itemType==PhysicalItemType.Note||physicalTarget.itemType==PhysicalItemType.Sticker||physicalTarget.itemType==PhysicalItemType.LoosePage))
            {
                normal=physicalTarget.transform.up.normalized;
                var box=physicalTarget.interactionCollider;
                origin=box.transform.TransformPoint(box.center+Vector3.up*box.size.y*.5f);
                rotation=physicalTarget.transform.rotation*Quaternion.Euler(90,0,0);
                return true;
            }
            if(targetRenderer&&targetRenderer.gameObject.activeInHierarchy)
            {
                rotation=targetRenderer.transform.rotation;normal=-targetRenderer.transform.forward;
                origin=targetRenderer.transform.position;
                return true;
            }
            return false;
        }
        bool ClearOfExistingControls(Rect candidate)
        {
            if(workspace.journal.IsEditing&&workspace.scrapbook.SelectedData!=null&&candidate.Overlaps(new Rect(24,180,300,650)))return false;
            bool details=workspace.SelectedMaterialIsReleaseBook||(workspace.SelectedPhysical&&
                (workspace.SelectedPhysical.itemType==PhysicalItemType.LoosePage||workspace.SelectedPhysical.itemType==PhysicalItemType.TravelersNotebook));
            if(details&&candidate.Overlaps(new Rect(1510,175,386,490)))return false;
            return !candidate.Overlaps(DeskViewControls.ToggleRect)&&!candidate.Overlaps(DeskViewControls.MouseLookRect)&&
                !candidate.Overlaps(DeskViewControls.ForwardRect)&&!candidate.Overlaps(DeskViewControls.LookHintRect)&&
                !candidate.Overlaps(DrawerStockIndex.EntryRect);
        }
        Rect VirtualBounds(Bounds bounds)
        {
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);bool found=false;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var point=workspace.lens.WorldToScreenPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                if(point.z<=0)continue;var p=new Vector2(point.x*1920/Screen.width,(Screen.height-point.y)*1080/Screen.height);
                min=Vector2.Min(min,p);max=Vector2.Max(max,p);found=true;
            }
            return found?Rect.MinMaxRect(min.x-20,min.y-20,max.x+20,max.y+20):new Rect(-100,-100,1,1);
        }
        static float ProjectedExtent(Vector3 extents,Vector3 axis) => Mathf.Abs(axis.x)*extents.x+Mathf.Abs(axis.y)*extents.y+Mathf.Abs(axis.z)*extents.z;

        void SelectMaterial(string key)
        {
            reward=false;TokenId=null;EarnedInstanceId=null;selectionInstance=key;
            var physical=key.StartsWith("physical:",StringComparison.Ordinal)?workspace.SelectedPhysical:null;
            string id=key.Substring(key.IndexOf(':')+1);
            var item=workspace.Service.GetItem(id);
            AssetId=physical?physical.assetId:item?.assetId;
            if(string.IsNullOrEmpty(AssetId)){IsOpen=false;selectionInstance=null;return;}
            var definition=workspace.scrapbook.Catalog.Find(AssetId);
            PreviewTexture=definition?.thumbnail?definition.thumbnail:definition?.texture;
            title=physical?physical.displayName:definition?.displayName??NightMaterialCatalog.NameFor(AssetId);
            if(physical&&physical.itemType==PhysicalItemType.LoosePage)
            {
                var binding=physical.GetComponent<LoosePageBinding>();
                var paper=workspace.journal.Catalog.Find(binding?binding.paperId:physical.assetId);
                PreviewTexture=paper?.texture;introduction="活页内纸\n"+(paper?.displayName??title)+"\n左键选中保持原位；右拖到桌面。\n在中央本子的换纸菜单中选用这款内页。";
            }
            else if(physical&&physical.itemType==PhysicalItemType.Tape)
                introduction="纸胶带卷 · 连续图案示例\n向桌前拉出胶带后选择2–30厘米，确认再贴入页面。\n整卷移动需先抽出轴杆，再横向右拖。";
            else if(NightMaterialCatalog.IsKnown(AssetId))
                introduction="特别创作素材\n氛围："+string.Join(" / ",NightMaterialCatalog.TagsFor(AssetId))+"\n"+Description(AssetId)+"\n右拖移动或揭取；有限材料不会因查看而复制。";
            else introduction=(item?.kind==CraftItemKind.Note?"便签":"贴纸 / 纸艺素材")+"\n左键选中保持原位，右拖到打开的页面。\n贴好后仍可揭取、跨本移动或按ESC取消；查看图案不会消耗材料。";
            targetCollider=physical?physical.interactionCollider:null;
            physicalTarget=physical;
            targetRenderer=physical?null:workspace.materialRenderer.GetRenderer(id)??workspace.scrapbook.visuals.GetRenderer(id);
            fallbackBounds=targetCollider?targetCollider.bounds:targetRenderer?targetRenderer.bounds:new Bounds(workspace.PointerWorld,Vector3.one*.04f);
            IsOpen=PreviewTexture;
            Debug.Log("[MaterialDiscovery] Selected instance="+id+" asset="+AssetId+" preview="+(PreviewTexture?"yes":"missing"));
        }
        Bounds TargetBounds() => targetCollider&&targetCollider.enabled&&targetCollider.gameObject.activeInHierarchy?
            targetCollider.bounds:targetRenderer&&targetRenderer.gameObject.activeInHierarchy?targetRenderer.bounds:fallbackBounds;
        void EnsureHalo()
        {
            if(haloRoot)return;
            haloRoot=new GameObject("Material world-space selection halo").transform;haloRoot.SetParent(transform,false);
            coreMaterial=HaloMaterial("Discovery halo core",new Color(1,.86f,.45f,1));
            softMaterial=HaloMaterial("Discovery halo soft rim",new Color(.72f,1,.86f,.26f));
            haloCore=Ring("Actual-object selection ring",.0022f,coreMaterial);haloSoft=Ring("Soft halo rim",.007f,softMaterial);
        }
        Material HaloMaterial(string name,Color color)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            var material=new Material(shader){name=name,hideFlags=HideFlags.DontSave,renderQueue=3011};
            material.SetFloat("_Surface",1);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.One);
            material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor",color);material.SetColor("_EmissionColor",new Color(color.r,color.g,color.b)*.4f);material.EnableKeyword("_EMISSION");
            return material;
        }
        LineRenderer Ring(string name,float width,Material material)
        {
            var line=new GameObject(name).AddComponent<LineRenderer>();line.transform.SetParent(haloRoot,false);
            line.useWorldSpace=false;line.loop=true;line.positionCount=72;line.widthMultiplier=width;line.sharedMaterial=material;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.generateLightingData=true;line.numCornerVertices=3;
            return line;
        }
        void OnGUI()
        {
            if(!Available||!IsOpen||!cardFits)return;
            if(!font){font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},20);label=new GUIStyle(GUI.skin.label){font=font,fontSize=19,wordWrap=true};heading=new GUIStyle(label){fontSize=24};close=new GUIStyle(GUI.skin.button){font=font,fontSize=20};}
            var old=GUI.matrix;int depth=GUI.depth;var color=GUI.color;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.depth=-174;
            try
            {
                GUI.color=DaydreamPalette.Surface(DaydreamPalette.Cream);GUI.DrawTexture(CardRect,Texture2D.whiteTexture);GUI.color=Color.white;
                label.normal.textColor=heading.normal.textColor=DaydreamPalette.Text(DaydreamPalette.Ink);
                GUI.Label(new Rect(CardRect.x+16,CardRect.y+9,CardRect.width-78,36),title,heading);
                if(GUI.Button(new Rect(CardRect.xMax-54,CardRect.y+10,38,34),"×",close))Close();
                if(PreviewTexture)GUI.DrawTexture(new Rect(CardRect.x+18,CardRect.y+54,140,154),PreviewTexture,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(CardRect.x+178,CardRect.y+54,CardRect.width-196,CardRect.height-62),introduction,label);
            }
            finally{GUI.matrix=old;GUI.depth=depth;GUI.color=color;}
        }
        public static string Description(string id)
        {
            switch(id)
            {
                case "night-glow-ink":return "萤火留下的柔光墨印，适合把安静的心情印进夜页。";
                case "night-star-sticker":return "一小片有暖色星芒的贴纸，给文字旁留一点希望。";
                case "night-lantern-paper":return "带纸灯纹理的和纸，像一封温暖的远方来信。";
                case "night-moonlit-star":return "稀有的月光星簇，适合纪念遥远而明亮的瞬间。";
                case "night-forgotten-dream":return "从门与阶梯之间拾到的梦纹，留下怀旧的细节。";
                case "day-iridescent-ink":return "泡泡折射出的虹彩墨印，把轻盈色光留在纸上。";
                case "day-origami-paper":return "纸鹤带来的折纸纹样，可以作为小纸片贴入页面。";
                case "day-cloud-washi":return "小云变成柔软的云白和纸条，适合给文字做衬底。";
                case "day-lined-paper":return "像从旧本子取下的横线纸，记录一件平常的小事。";
                case "day-pearl-paper":return "珍珠光折纸，在细腻的纸纹里藏着一层淡淡虹彩。";
                case "afternoon-flower-sticker":return "蒲公英与花瓣留下的压花贴纸，收藏短暂的温柔。";
                case "afternoon-balloon-ink":return "从气球借来的温暖墨色，印下一点轻快的欢喜。";
                case "afternoon-leaf-sticker":return "一枚带叶脉的贴纸，适合纪念慢慢经过的季节。";
                case "afternoon-gold-foil":return "午后金尘留下的小片金箔，为珍贵的一句话点光。";
                case "afternoon-sunset-foil":return "晚霞色的玫瑰金箔，留住一天最后的暖意。";
                default:return "这一季来访的纸艺图案，可像普通贴纸一样贴入和转移。";
            }
        }
        void OnDestroy(){if(Instance==this)Instance=null;if(haloRoot)Destroy(haloRoot.gameObject);if(coreMaterial)Destroy(coreMaterial);if(softMaterial)Destroy(softMaterial);if(font)Destroy(font);}
    }
}
