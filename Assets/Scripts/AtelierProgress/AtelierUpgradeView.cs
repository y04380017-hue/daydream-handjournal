using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Purchased objects are an additive decorative layer. No work surface or camera is changed.</summary>
    public sealed class AtelierUpgradeView:MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        readonly Dictionary<string,GameObject> views=new Dictionary<string,GameObject>();
        readonly List<Material> materials=new List<Material>();
        Material mint,gold,paper;
        void Start()
        {
            if(!workspace)workspace=GetComponent<WorkspaceGameplayController>();
            mint=Make(new Color(.58f,.84f,.76f));gold=Make(new Color(.94f,.79f,.48f));paper=Make(new Color(.96f,.92f,.84f));
        }
        Material Make(Color color)
        {var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){hideFlags=HideFlags.DontSave};material.SetColor("_BaseColor",color);materials.Add(material);return material;}
        void Update()
        {
            if(!workspace||!workspace.Ready||workspace.Service.Document.atelierProgress==null)return;
            foreach(var purchase in workspace.Service.Document.atelierProgress.purchases)
            {
                if(purchase.id.StartsWith("unlock-")||views.ContainsKey(purchase.id))continue;
                var root=new GameObject("Purchased atelier decoration");root.transform.SetParent(transform,true);views.Add(purchase.id,root);
                if(purchase.id=="desk-lamp")
                {
                    root.transform.position=new Vector3(-.61f,.752f,-.18f);
                    Part(root.transform,PrimitiveType.Cylinder,new Vector3(0,.003f,0),new Vector3(.034f,.003f,.034f),mint);
                    Part(root.transform,PrimitiveType.Cylinder,new Vector3(0,.035f,0),new Vector3(.003f,.032f,.003f),gold);
                    Part(root.transform,PrimitiveType.Sphere,new Vector3(0,.07f,0),new Vector3(.042f,.026f,.042f),paper);
                }
                if(purchase.id=="memory-shelf")
                {
                    root.transform.position=new Vector3(-.64f,1.04f,.39f);
                    Part(root.transform,PrimitiveType.Cube,Vector3.zero,new Vector3(.11f,.008f,.045f),paper);
                    for(int i=0;i<3;i++)Part(root.transform,PrimitiveType.Cube,new Vector3((i-1)*.029f,.026f,0),new Vector3(.019f,.043f,.006f),i%2==0?mint:gold);
                }
                if(purchase.id=="sky-charm")
                {
                    root.transform.position=new Vector3(.81f,1.08f,.45f);
                    Part(root.transform,PrimitiveType.Sphere,Vector3.zero,new Vector3(.039f,.026f,.039f),mint);
                    for(int i=0;i<3;i++)Part(root.transform,PrimitiveType.Cylinder,new Vector3((i-1)*.013f,-.029f,0),new Vector3(.003f,.021f,.003f),gold);
                }
            }
        }
        static void Part(Transform parent,PrimitiveType type,Vector3 position,Vector3 size,Material material)
        {var item=GameObject.CreatePrimitive(type);item.transform.SetParent(parent,false);item.transform.localPosition=position;item.transform.localScale=size;item.GetComponent<Renderer>().sharedMaterial=material;var hit=item.GetComponent<Collider>();hit.enabled=false;Destroy(hit);}
        void OnDestroy(){foreach(var view in views.Values)if(view)Destroy(view);foreach(var material in materials)if(material)Destroy(material);}
    }
}
