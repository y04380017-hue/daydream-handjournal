using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Replaceable cover and paper appearance. No input, collider or camera changes.</summary>
    public sealed class StudioPresentation : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        readonly Dictionary<string, Texture2D> papers = new Dictionary<string, Texture2D>();
        JournalDocument lastDocument;int lastSpread=-1;string coverKey;
        Texture2D coverTexture;Transform charmRoot;TextMesh coverTitle;Font coverFont;Material charmMaterial;
        void LateUpdate()
        {
            if(!workspace||!workspace.Ready||workspace.journal.HasPaperPreview)return;
            var doc=workspace.Service.Document;var data=doc.personalStudio;if(data==null)return;
            if(doc==lastDocument&&doc.ActiveSpread==lastSpread)return;
            lastDocument=doc;lastSpread=doc.ActiveSpread;
            var work=workspace.journal.GetComponent<NotebookWorkbench>();
            if(work&&work.binderOnly!=null&&doc.ActiveNotebook==null)
            {
                string key=data.coverMaterial+data.coverColor+data.title+data.charm;
                if(key!=coverKey)
                {
                    coverKey=key;if(coverTexture)Destroy(coverTexture);
                    coverTexture=new Texture2D(128,128,TextureFormat.RGBA32,false);var pixels=new Color[128*128];Color baseColor=StudioGraphics.Colors[data.coverColor];
                    for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                    {float grain=data.coverMaterial=="棉布"?((x%4==0||y%4==0)?-.045f:.015f):data.coverMaterial=="软木"?Mathf.Sin(x*13.17f+y*7.51f)*.05f:Mathf.Sin(x*.7f+y*3.1f)*.012f;pixels[y*128+x]=new Color(baseColor.r+grain,baseColor.g+grain,baseColor.b+grain,1);}
                    coverTexture.SetPixels(pixels);coverTexture.Apply();
                }
                var cover=work.binderOnly.Where(r=>r).OrderByDescending(r=>r.bounds.size.x*r.bounds.size.z).FirstOrDefault();
                if(cover){var block=new MaterialPropertyBlock();cover.GetPropertyBlock(block);block.SetColor("_BaseColor",Color.white);block.SetTexture("_BaseMap",coverTexture);cover.SetPropertyBlock(block);}
            }
            if(!charmRoot)
            {
                charmRoot=new GameObject("Private journal cover details").transform;charmRoot.SetParent(workspace.journal.transform,false);
                var titleObject=new GameObject("Player cover title");titleObject.transform.SetParent(charmRoot,false);coverTitle=titleObject.AddComponent<TextMesh>();
                coverFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},48);coverTitle.font=coverFont;coverTitle.GetComponent<MeshRenderer>().sharedMaterial=coverFont.material;
                coverTitle.fontSize=48;coverTitle.characterSize=.0015f;coverTitle.anchor=TextAnchor.MiddleCenter;coverTitle.alignment=TextAlignment.Center;coverTitle.color=DaydreamPalette.Ink;
                var charm=GameObject.CreatePrimitive(PrimitiveType.Sphere);charm.transform.SetParent(charmRoot,false);charm.transform.localPosition=new Vector3(.044f,0,0);charm.transform.localScale=Vector3.one*.008f;
                charmMaterial=new Material(workspace.journal.pageView.rightRenderer.sharedMaterial){name="Private cover charm",hideFlags=HideFlags.DontSave};charmMaterial.SetTexture("_BaseMap",null);charm.GetComponent<Renderer>().sharedMaterial=charmMaterial;
                var collider=charm.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            }
            charmRoot.gameObject.SetActive(doc.ActiveNotebook==null);
            if(doc.ActiveNotebook==null)
            {
                var anchor=workspace.journal.pageView.GetSurface(JournalSide.Right);var size=workspace.journal.pageView.SurfaceSize(JournalSide.Right);
                charmRoot.SetPositionAndRotation(anchor.TransformPoint(new Vector3(size.x*.48f,-.018f,-.003f)),anchor.rotation);
                charmRoot.localScale=Vector3.one;
                coverFont.RequestCharactersInTexture(data.title,48,FontStyle.Normal);coverTitle.text=data.title;var charm=charmRoot.GetChild(1);charm.gameObject.SetActive(data.charm>0);
                if(data.charm>0){var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",StudioGraphics.Colors[(data.coverColor+data.charm)%6]);charm.GetComponent<Renderer>().SetPropertyBlock(block);}
            }
            foreach(JournalSide side in new[]{JournalSide.Left,JournalSide.Right})
            {
                var page=doc.GetPage(side);var note=data.pageNotes.Find(n=>n.pageId==page.pageId);
                if(note==null||note.paperColor==0)continue;
                string key=page.paperId+"/"+note.paperColor;
                if(!papers.TryGetValue(key,out var texture)||!texture)
                {
                    var source=workspace.journal.Catalog.Find(page.paperId)?.texture;if(!source)continue;
                    texture=StudioGraphics.Readable(source,256,360);var pixels=texture.GetPixels();Color tint=StudioGraphics.Colors[note.paperColor];
                    for(int i=0;i<pixels.Length;i++)pixels[i]=Color.Lerp(pixels[i],tint,.15f);
                    texture.SetPixels(pixels);texture.Apply();papers[key]=texture;
                }
                workspace.journal.pageView.SetPaper(side,texture);
            }
        }
        void OnDestroy(){if(charmRoot)Destroy(charmRoot.gameObject);if(coverFont)Destroy(coverFont);if(coverTexture)Destroy(coverTexture);if(charmMaterial)Destroy(charmMaterial);foreach(var texture in papers.Values)if(texture)Destroy(texture);}
    }
}
