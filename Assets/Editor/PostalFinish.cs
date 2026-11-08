using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static partial class LuminousAtelierSetup
    {
        public static void Tune()
        {
            var scene=EditorSceneManager.OpenScene(Scene);
            var cycle=Object.FindFirstObjectByType<DreamTimeController>();var environment=cycle.atmosphere;
            // URP's first material import validates emission keywords against the GI flags.
            foreach(string file in Directory.GetFiles(Folder+"Materials","*.mat"))
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(file.Replace('\\','/'));
                if(m.shader.name!="Universal Render Pipeline/Lit"||m.GetColor("_EmissionColor").maxColorComponent<=0)continue;
                m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;m.EnableKeyword("_EMISSION");EditorUtility.SetDirty(m);
            }
            // Each reverse side of folded paper needs its own normal.
            foreach(string file in Directory.GetFiles(Folder+"Meshes","*.asset"))
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(file.Replace('\\','/'));
                if(!mesh||!(mesh.name.StartsWith("Hull ")||mesh.name=="Pennant"||mesh.name=="Rose tail fins"||mesh.name=="Printed mountains"||mesh.name=="Perforated ivory stamp"))continue;
                var points=mesh.vertices;var triangles=mesh.triangles;
                mesh.Clear();mesh.vertices=triangles.Select(i=>points[i]).ToArray();mesh.triangles=Enumerable.Range(0,triangles.Length).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            }
            var distantClouds=GameObject.Find("Floating Sky Atelier/Clouds");
            if(distantClouds)environment.daytimeKeepsakes=environment.daytimeKeepsakes.Concat(distantClouds.GetComponentsInChildren<Renderer>().Where(r=>r.enabled)).Distinct().ToArray();
            var arches=GameObject.Find("Daydream Studio P2").GetComponentsInChildren<DaydreamFloat>().SelectMany(a=>a.GetComponentsInChildren<Renderer>()).Where(r=>r.enabled);
            environment.daytimeKeepsakes=environment.daytimeKeepsakes.Concat(arches).Distinct().ToArray();
            environment.leftPower=.23f;environment.overheadPower=.19f;environment.edgePower=.14f;
            environment.skyAmbient=new Color(.51f,.43f,.40f);environment.sideAmbient=new Color(.54f,.43f,.32f);
            var lamps=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Materials/Honey lantern.mat");
            lamps.SetColor("_EmissionColor",Hex("FFBD79").linear*1.05f);EditorUtility.SetDirty(lamps);
            var master=environment.transform;
            if(!master.Find("Glazed platform skin"))
            {
                var layer=MaterialAsset("Reflected pool light","Daydream/Pearlescent glaze","FFF4FC");
                var floor=Part(master,"Glazed platform skin",PrimitiveType.Quad,new Vector3(.02f,.003f,.05f),new Vector3(2.8f,2.2f,1),layer);
                floor.localRotation=Quaternion.Euler(90,0,0);
                var resin=MaterialAsset("Rose postbox glass","Daydream/Cast rose resin","F6C3C9");
                var box=Object.FindFirstObjectByType<CommissionDeskController>().mailboxRoot;
                var shell=box.Find("Rounded postbox").GetComponent<Renderer>();var roof=box.Find("Domed roof").GetComponent<Renderer>();
                resin.SetColor("_BaseColor",shell.sharedMaterial.color);shell.sharedMaterial=resin;roof.sharedMaterial=resin;
                foreach(var r in new[]{shell,roof}){if(PrefabUtility.IsPartOfPrefabInstance(r))PrefabUtility.RecordPrefabInstancePropertyModifications(r);EditorUtility.SetDirty(r);}
                environment.phaseSurfaces=environment.phaseSurfaces.Concat(new[]{floor.GetComponent<Renderer>(),shell,roof}).ToArray();
            }
            EditorUtility.SetDirty(environment);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
