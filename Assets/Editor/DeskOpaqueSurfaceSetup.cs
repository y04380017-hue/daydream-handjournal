using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>Audit first, then replace only genuinely transparent desk/mailbox slots with derived opaque assets.</summary>
    public static class DeskOpaqueSurfaceSetup
    {
        const string Folder="Assets/Daydream/ReferenceRoom/OpaqueSurfaces";
        [Serializable] public sealed class SurfaceRow
        {
            public string path,sourceMaterial,sourceShader,resultMaterial,resultShader;
            public int sourceQueue,resultQueue,boundaryEdges,nonManifoldEdges;
            public float sourceSurface,sourceZWrite,sourceAlpha,resultSurface,resultZWrite,resultAlpha;
            public bool sourceSamplesSceneColor,sourceFixedZWriteOff,sourceDepthAlways,resultOpaque,materialReplaced,backplateAdded;
        }
        [Serializable] public sealed class LayerRow { public string path,shader;public int queue;public bool samplesSceneColor,zTestAlways,zWriteOff; }
        [Serializable] public sealed class Report
        { public string utc;public SurfaceRow[] surfaces;public LayerRow[] sceneryLayers;public string conclusion; }

        [MenuItem("Vintage Journal/Daydream/Audit and solidify desk and mailbox")]
        public static void Install()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");Apply();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        public static void Apply()
        {
            var desk=GameObject.Find("Desk");var commission=Object.FindFirstObjectByType<CommissionDeskController>();
            if(!desk||!commission||!commission.mailboxRoot)throw new InvalidOperationException("Solid-surface setup requires the authored desk and explicit mailbox binding.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var marker=desk.GetComponent<DeskOpaqueSurfaceBindings>();if(!marker)marker=desk.AddComponent<DeskOpaqueSurfaceBindings>();
            var deskBodies=desk.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponent<MeshFilter>()&&!r.GetComponent<TextMesh>()).Cast<Renderer>().ToArray();
            var mailboxBodies=commission.mailboxRoot.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponent<MeshFilter>()&&!r.GetComponent<TextMesh>()).Cast<Renderer>().ToArray();
            var rows=new List<SurfaceRow>();var plates=new List<Renderer>(marker.addedBackplates.Where(r=>r));
            var replacements=new Dictionary<Material,Material>();
            foreach(var renderer in deskBodies.Concat(mailboxBodies).Distinct())
            {
                if(plates.Contains(renderer))continue;
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                CountMeshEdges(mesh,out int boundary,out int nonManifold);
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i];if(!source)continue;
                    // Preserve the original source evidence on repeat setup runs after a slot already uses its derived asset.
                    var original=OriginalSource(source);string shaderCode=ShaderText(original.shader);
                    var row=new SurfaceRow{path=PathOf(renderer.transform),sourceMaterial=AssetDatabase.GetAssetPath(original),sourceShader=original.shader.name,
                        sourceQueue=original.renderQueue,sourceSurface=Float(original,"_Surface"),sourceZWrite=Float(original,"_ZWrite"),sourceAlpha=ColorOf(original).a,materialReplaced=original!=source,
                        sourceSamplesSceneColor=shaderCode.Contains("SampleSceneColor"),sourceFixedZWriteOff=shaderCode.Contains("ZWrite Off"),sourceDepthAlways=shaderCode.Contains("ZTest Always"),boundaryEdges=boundary,nonManifoldEdges=nonManifold};
                    if(!IsOpaque(source))
                    {
                        if(!replacements.TryGetValue(source,out var replacement)){replacement=OpaqueCopy(source);replacements.Add(source,replacement);}
                        materials[i]=replacement;changed=true;row.materialReplaced=true;
                    }
                    var result=materials[i];row.resultMaterial=AssetDatabase.GetAssetPath(result);row.resultShader=result.shader.name;row.resultQueue=result.renderQueue;
                    row.resultSurface=Float(result,"_Surface");row.resultZWrite=Float(result,"_ZWrite");row.resultAlpha=ColorOf(result).a;row.resultOpaque=IsOpaque(result);
                    // A closed opaque desk does not need another slab. Add a separate inner solid only if the actual top mesh has open edges.
                    bool top=renderer.transform.IsChildOf(desk.transform)&&renderer.bounds.size.x>.7f&&renderer.bounds.size.z>.4f&&renderer.bounds.size.y<.2f;
                    if(top&&boundary>0&&!renderer.GetComponentsInChildren<DeskSolidBackplate>(true).Any())
                    {
                        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Independent solid tabletop backplate";cube.transform.SetParent(renderer.transform,false);
                        var scale=renderer.transform.lossyScale;var inset=new Vector3(.002f/Mathf.Max(.001f,Mathf.Abs(scale.x)),.002f/Mathf.Max(.001f,Mathf.Abs(scale.y)),.002f/Mathf.Max(.001f,Mathf.Abs(scale.z)));
                        cube.transform.localPosition=mesh.bounds.center;cube.transform.localScale=Vector3.Max(mesh.bounds.size-inset*2,Vector3.one*.001f);
                        Object.DestroyImmediate(cube.GetComponent<Collider>());cube.AddComponent<DeskSolidBackplate>();
                        var skin=cube.GetComponent<Renderer>();skin.sharedMaterial=result;plates.Add(skin);row.backplateAdded=true;
                    }
                    rows.Add(row);
                }
                if(changed){Unpack(renderer.gameObject);renderer.sharedMaterials=materials;EditorUtility.SetDirty(renderer);}
            }
            marker.deskBodies=deskBodies;marker.mailboxBodies=mailboxBodies;marker.addedBackplates=plates.ToArray();EditorUtility.SetDirty(marker);
            var layers=Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).SelectMany(r=>r.sharedMaterials.Where(m=>m&&m.shader).Select(m=>new{renderer=r,material=m,code=ShaderText(m.shader)}))
                .Where(x=>x.code.Contains("SampleSceneColor")||x.code.Contains("ZTest Always"))
                .Select(x=>new LayerRow{path=PathOf(x.renderer.transform),shader=x.material.shader.name,queue=x.material.renderQueue,samplesSceneColor=x.code.Contains("SampleSceneColor"),zTestAlways=x.code.Contains("ZTest Always"),zWriteOff=x.code.Contains("ZWrite Off")}).ToArray();
            string output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"docs","reference-room");Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"opaque-surface-audit.json"),JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),surfaces=rows.ToArray(),sceneryLayers=layers,
                conclusion="Source shader/queue/depth and welded mesh edges are recorded. Existing opaque wood is retained. Only transparent desk/mailbox slots use separate URP opaque copies; source materials and other glass surfaces remain unchanged. GPU occlusion tests are separate evidence."},true));
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(desk.scene);
            Debug.Log("[DeskOpaque] Audited "+rows.Count+" slots; replaced="+rows.Count(r=>r.materialReplaced)+"; added inner solids="+rows.Count(r=>r.backplateAdded));
        }
        public static bool IsOpaque(Material material)
        {
            if(!material||!material.shader||material.renderQueue>=3000)return false;
            if(material.GetTag("RenderType",false,"").IndexOf("Transparent",StringComparison.OrdinalIgnoreCase)>=0)return false;
            if(material.HasProperty("_Surface")&&material.GetFloat("_Surface")>.01f)return false;
            if(material.HasProperty("_ZWrite")&&material.GetFloat("_ZWrite")<.99f)return false;
            if(material.HasProperty("_SrcBlend")&&Mathf.Abs(material.GetFloat("_SrcBlend")-(float)BlendMode.One)>.01f)return false;
            if(material.HasProperty("_DstBlend")&&Mathf.Abs(material.GetFloat("_DstBlend")-(float)BlendMode.Zero)>.01f)return false;
            if(ColorOf(material).a<.999f)return false;
            string code=ShaderText(material.shader);
            return !code.Contains("ZWrite Off")&&!code.Contains("SampleSceneColor");
        }
        static Material OpaqueCopy(Material source)
        {
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if(string.IsNullOrEmpty(guid))throw new InvalidOperationException("Cannot derive persistent opaque material from an untracked runtime material.");
            string path=Folder+"/Opaque_"+guid+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            var copy=new Material(source){name="Opaque derived "+source.name};copy.shader=Shader.Find("Universal Render Pipeline/Lit");
            if(!copy.shader)throw new InvalidOperationException("URP Lit is required for the solid mailbox.");
            var color=ColorOf(source);color.a=1;copy.SetColor("_BaseColor",color);
            copy.SetFloat("_Surface",0);copy.SetFloat("_Blend",0);copy.SetFloat("_AlphaClip",0);copy.SetFloat("_ZWrite",1);
            copy.SetFloat("_SrcBlend",(float)BlendMode.One);copy.SetFloat("_DstBlend",(float)BlendMode.Zero);
            copy.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);copy.SetFloat("_DstBlendAlpha",(float)BlendMode.Zero);copy.SetFloat("_Cull",(float)CullMode.Back);
            copy.SetOverrideTag("RenderType","Opaque");copy.renderQueue=(int)RenderQueue.Geometry;
            copy.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");copy.DisableKeyword("_ALPHATEST_ON");
            copy.SetShaderPassEnabled("DepthOnly",true);copy.SetShaderPassEnabled("DepthNormals",true);copy.SetShaderPassEnabled("ShadowCaster",true);
            if(existing){EditorUtility.CopySerialized(copy,existing);Object.DestroyImmediate(copy);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(copy,path);return copy;
        }
        static Material OriginalSource(Material material)
        {
            string path=AssetDatabase.GetAssetPath(material);
            if(!path.StartsWith(Folder+"/Opaque_",StringComparison.Ordinal))return material;
            string guid=Path.GetFileNameWithoutExtension(path).Substring("Opaque_".Length);
            var original=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));return original?original:material;
        }
        static float Float(Material material,string key)=>material.HasProperty(key)?material.GetFloat(key):-1;
        static Color ColorOf(Material material)=>material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):material.HasProperty("_Color")?material.GetColor("_Color"):Color.white;
        static readonly Dictionary<Shader,string> shaderSources=new Dictionary<Shader,string>();
        static string ShaderText(Shader shader)
        {
            if(!shader)return "";if(shaderSources.TryGetValue(shader,out var cached))return cached;
            string path=AssetDatabase.GetAssetPath(shader);
            if(path.StartsWith("Packages/",StringComparison.Ordinal))
            {var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);if(package!=null)path=Path.Combine(package.resolvedPath,path.Substring(package.assetPath.Length+1));}
            if(!Path.IsPathRooted(path))path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,path);
            string text=File.Exists(path)?File.ReadAllText(path):"";shaderSources[shader]=text;return text;
        }
        static void CountMeshEdges(Mesh mesh,out int boundary,out int nonManifold)
        {
            boundary=nonManifold=-1;if(!mesh||!mesh.isReadable)return;
            var unique=new Dictionary<string,int>();var mapping=new int[mesh.vertexCount];var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                var p=vertices[i];string key=Mathf.RoundToInt(p.x*100000)+","+Mathf.RoundToInt(p.y*100000)+","+Mathf.RoundToInt(p.z*100000);
                if(!unique.TryGetValue(key,out int index)){index=unique.Count;unique.Add(key,index);}mapping[i]=index;
            }
            var edges=new Dictionary<ulong,int>();var triangles=mesh.triangles;
            void Add(int a,int b){a=mapping[a];b=mapping[b];if(a==b)return;uint low=(uint)Mathf.Min(a,b),high=(uint)Mathf.Max(a,b);ulong key=((ulong)low<<32)|high;edges[key]=edges.TryGetValue(key,out int count)?count+1:1;}
            for(int i=0;i<triangles.Length;i+=3){Add(triangles[i],triangles[i+1]);Add(triangles[i+1],triangles[i+2]);Add(triangles[i+2],triangles[i]);}
            boundary=edges.Values.Count(v=>v==1);nonManifold=edges.Values.Count(v=>v>2);
        }
        static string PathOf(Transform value)=>value.parent?PathOf(value.parent)+"/"+value.name:value.name;
        static void Unpack(GameObject value)
        {var root=PrefabUtility.GetOutermostPrefabInstanceRoot(value);if(root)PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);}
    }
}
