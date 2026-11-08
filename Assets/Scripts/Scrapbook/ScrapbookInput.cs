using UnityEngine;
namespace VintageJournal
{
    [DefaultExecutionOrder(-5)]
    public sealed class ScrapbookInput:MonoBehaviour
    {
        public ScrapbookController book;
        ScrapbookUI ui;Vector2 startUv,startMouse,offset;float startScale,startAngle,startDistance,startLength;bool dragFromBrowser,tapeDrag;int lastLogSpread=-1;bool lastBusy;
        void Start(){if(!book)book=GetComponent<ScrapbookController>();ui=GetComponent<ScrapbookUI>();}
        public void StartAssetDrag(string id){if(book.Choose(id)){dragFromBrowser=true;startMouse=Input.mousePosition;}}
        void Update()
        {
            if(RuntimeInputGate.BlocksLegacyInput||RuntimeInputGate.ConsumedThisFrame)return;
            if(!book||!book.journal||book.journal.Document==null)return;
            if(!Application.isFocused){if(book.IsTransforming)book.CancelTransform();book.visuals.ClearPreview();return;}
            if(!book.journal.IsEditing)return;
            bool busy=book.journal.IsBusy;
            if(!busy&&(lastBusy||lastLogSpread!=book.journal.Document.ActiveSpread)){LogPagePoints();lastLogSpread=book.journal.Document.ActiveSpread;}
            lastBusy=busy;
            if(Input.GetKeyDown(KeyCode.Escape)||Input.GetMouseButtonDown(1)&&book.Chosen!=null){if(book.Cancel()){book.ConsumeInput();dragFromBrowser=false;tapeDrag=false;return;}}
            if(busy||book.journal.HasPaperPreview)return;
            bool ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            if(ctrl&&Input.GetKeyDown(KeyCode.Z)){book.Undo();return;}if(ctrl&&Input.GetKeyDown(KeyCode.Y)){book.Redo();return;}
            if(book.State==ScrapbookState.Selected)
            {
                if(Input.GetKeyDown(KeyCode.Delete)){book.DeleteSelected();return;}
                float step=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)?15:5;
                if(Input.GetKeyDown(KeyCode.Q)||Input.GetKeyDown(KeyCode.E)){book.BeginTransform("rotate");book.RotateTo(book.SelectedData.rotationDegrees+(Input.GetKeyDown(KeyCode.Q)?-step:step));book.CommitTransform();LogHandles();return;}
                if(Input.GetKeyDown(KeyCode.LeftBracket)||Input.GetKeyDown(KeyCode.RightBracket)){book.BeginTransform("scale");book.ScaleTo(book.SelectedData.scale.x+(Input.GetKeyDown(KeyCode.LeftBracket)?-.1f:.1f));book.CommitTransform();LogHandles();return;}
            }
            Vector2 mouse=Input.mousePosition;
            bool over=ui&&ui.OverControls(mouse);
            if(book.Chosen!=null&&book.State==ScrapbookState.Placing)
            {
                JournalSide side=JournalSide.Left;Vector2 uv=Vector2.zero;
                bool valid=!over&&book.TryPageHit(mouse,out side,out uv);
                if(valid)book.PreviewAt(side,uv,true);else book.visuals.ClearPreview();
                if(tapeDrag&&Input.GetMouseButton(0)&&book.UvOnSide(mouse,book.ActiveSide,out var end))book.SetLength(startLength+Mathf.Abs(end.x-startUv.x)*4);
                if(Input.GetMouseButtonDown(0)&&valid&&Time.frameCount>book.InputBlockedUntilFrame)
                {
                    if(book.Chosen.supportsLengthAdjustment){tapeDrag=true;startUv=uv;startLength=book.Ghost.length;}
                    else {book.Place(side,uv);LogHandles();}
                }
                if(Input.GetMouseButtonUp(0))
                {
                    if(valid&&(tapeDrag||dragFromBrowser&&Vector2.Distance(mouse,startMouse)>12)){book.Place(side,uv);LogHandles();}
                    tapeDrag=false;dragFromBrowser=false;
                }
                return;
            }
            if(book.IsTransforming)
            {
                int transformButton=book.State==ScrapbookState.Dragging?1:0;
                if(Input.GetMouseButton(transformButton)&&book.UvOnSide(mouse,book.ActiveSide,out var uv))
                {
                    if(book.State==ScrapbookState.Dragging)book.MoveTo(uv+offset);
                    else
                    {
                        Vector2 physical=Vector2.Scale(uv-book.SelectedData.position,book.journal.pageView.SurfaceSize(book.ActiveSide));
                        if(book.State==ScrapbookState.Rotating){float a=Mathf.Atan2(physical.y,physical.x)*Mathf.Rad2Deg-startAngle;bool snap=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);book.RotateTo(snap?Mathf.Round(a/15)*15:a);}
                        else if(transformIsLength)book.SetLength(Mathf.Max(.3f,physical.magnitude/Mathf.Max(.001f,startDistance)*startLength));
                        else book.ScaleTo(startScale*physical.magnitude/Mathf.Max(.001f,startDistance));
                    }
                }
                if(Input.GetMouseButtonUp(transformButton)){book.CommitTransform();LogHandles();}
                return;
            }
            if(over||book.BrowserOpen||Time.frameCount<=book.InputBlockedUntilFrame||(!Input.GetMouseButtonDown(0)&&!Input.GetMouseButtonDown(1)))return;
            if(book.SelectedData!=null&&Input.GetMouseButtonDown(0))
            {
                foreach(string handle in new[]{"delete","rotate","scale","length"})
                {
                    if(handle=="length"&&!book.Catalog.Find(book.SelectedData.assetId).supportsLengthAdjustment)continue;
                    if(Vector2.Distance(mouse,HandlePoint(handle))<16*Screen.width/1920f)
                    {
                        if(handle=="delete"){book.DeleteSelected();return;}
                        if(book.BeginTransform(handle=="rotate"?"rotate":handle=="length"?"length":"scale"))
                        {
                            transformIsLength=handle=="length";book.UvOnSide(mouse,book.ActiveSide,out var uv);
                            Vector2 p=Vector2.Scale(uv-book.SelectedData.position,book.journal.pageView.SurfaceSize(book.ActiveSide));
                            startDistance=p.magnitude;startAngle=Mathf.Atan2(p.y,p.x)*Mathf.Rad2Deg-book.SelectedData.rotationDegrees;startScale=book.SelectedData.scale.x;startLength=book.SelectedData.length;
                        }
                        return;
                    }
                }
            }
            if(book.TryPageHit(mouse,out var s,out var point))
            {
                var id=book.HitDecoration(s,point);
                if(id!=null&&book.Select(id,s))
                {offset=book.SelectedData.position-point;if(Input.GetMouseButtonDown(1))book.BeginTransform("move");LogHandles();}
                else {book.Deselect();book.SetActiveSide(s);}
            }
        }
        bool transformIsLength;
        public Vector2 ScreenFor(JournalSide side,Vector2 uv)=>book.journal.cameraRig.Lens.WorldToScreenPoint(book.journal.pageView.WorldFromNormalized(side,uv));
        public Vector2 HandlePoint(string handle)
        {
            return ControlCorner(handle=="scale"?Vector2.one:handle=="delete"?new Vector2(-1,1):handle=="length"?Vector2.right:new Vector2(0,1.65f));
        }
        public Vector2 ControlCorner(Vector2 signs)
        {
            var d=book.SelectedData;if(d==null)return Vector2.zero;var def=book.Catalog.Find(d.assetId);var page=book.journal.pageView.SurfaceSize(book.ActiveSide);
            var center=ScreenFor(book.ActiveSide,d.position);
            var x=ScreenFor(book.ActiveSide,d.position+DecorationGeometry.Offset(def,d,new Vector2(.5f,0),page))-center;
            var y=ScreenFor(book.ActiveSide,d.position+DecorationGeometry.Offset(def,d,new Vector2(0,.5f),page))-center;
            float unit=Screen.width/1920f;
            x=x.normalized*Mathf.Max(x.magnitude,40*unit);y=y.normalized*Mathf.Max(y.magnitude,35*unit);
            return center+x*signs.x+y*signs.y;
        }
        [System.Serializable]class HandleInfo{public string id;public float centerX,centerY,hitX,hitY,rotateX,rotateY,scaleX,scaleY,deleteX,deleteY,lengthX,lengthY;}
        public void LogHandles()
        {
            if(book.SelectedData==null)return;var c=ScreenFor(book.ActiveSide,book.SelectedData.position);var r=HandlePoint("rotate");var s=HandlePoint("scale");var d=HandlePoint("delete");var l=HandlePoint("length");
            var data=book.SelectedData;var def=book.Catalog.Find(data.assetId);Vector2 hit=c;bool found=false;
            for(int ring=0;ring<9&&!found;ring++)for(int n=0;n<16&&!found;n++)
            {float a=n*Mathf.PI/8;var local=new Vector2(.5f,.5f)+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*ring*.05f;if(def.HitAlpha(local)){hit=ScreenFor(book.ActiveSide,data.position+DecorationGeometry.Offset(def,data,local-new Vector2(.5f,.5f),book.journal.pageView.SurfaceSize(book.ActiveSide)));found=true;}}
            Debug.Log("[Scrapbook] Handles "+JsonUtility.ToJson(new HandleInfo{id=book.SelectedId,centerX=c.x/Screen.width,centerY=1-c.y/Screen.height,hitX=hit.x/Screen.width,hitY=1-hit.y/Screen.height,rotateX=r.x/Screen.width,rotateY=1-r.y/Screen.height,scaleX=s.x/Screen.width,scaleY=1-s.y/Screen.height,deleteX=d.x/Screen.width,deleteY=1-d.y/Screen.height,lengthX=l.x/Screen.width,lengthY=1-l.y/Screen.height}));
        }
        [System.Serializable]class PageInfo{public string name;public float x,y;}
        void LogPagePoints(){foreach(JournalSide s in new[]{JournalSide.Left,JournalSide.Right}){var p=ScreenFor(s,new Vector2(.53f,.52f));Debug.Log("[Scrapbook] PagePoint "+JsonUtility.ToJson(new PageInfo{name=s.ToString(),x=p.x/Screen.width,y=1-p.y/Screen.height}));}}
    }
}
