using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace VintageJournal.Editor
{
    public static class PhysicalWorkspaceIntegration
    {
        [MenuItem("Vintage Journal/Stage 4/Build and connect physical workspace")]
        public static void BuildAndConnect(){PhysicalWorkspaceSetup.Build();Configure();}
        public static void Configure()
        {
            var controller=Object.FindFirstObjectByType<PhysicalInteractionController>();
            if(!controller)throw new System.InvalidOperationException("Build PhysicalWorkspaceSetup first.");
            var input=controller.GetComponent<PhysicalMouseInputAdapter>();
            if(!input)input=controller.gameObject.AddComponent<PhysicalMouseInputAdapter>();
            var hud=controller.GetComponent<PhysicalWorkspaceHUD>();
            if(!hud)hud=controller.gameObject.AddComponent<PhysicalWorkspaceHUD>();
            input.controller=controller;input.lens=Camera.main;
            controller.interactionOrigin=input.lens.transform;
            input.desk=Object.FindFirstObjectByType<DeskUI>();input.journal=Object.FindFirstObjectByType<JournalController>();input.hud=hud;
            hud.controller=controller;hud.input=input;hud.desk=input.desk;hud.journal=input.journal;
            hud.containers=Object.FindObjectsByType<PhysicalContainer>(FindObjectsSortMode.None).Where(c=>c.canClose).OrderBy(c=>c.name).ToArray();
            Directory.CreateDirectory("Assets/Data/Physical");
            const string path="Assets/Data/Physical/DeskCameraProfile.asset";
            var profile=AssetDatabase.LoadAssetAtPath<DeskCameraProfile>(path);
            if(!profile){profile=ScriptableObject.CreateInstance<DeskCameraProfile>();AssetDatabase.CreateAsset(profile,path);}
            var rig=Object.FindFirstObjectByType<DeskCamera>();rig.profile=profile;
            rig.transform.position=profile.seatedPosition;rig.transform.LookAt(profile.seatedTarget);
            rig.GetComponent<Camera>().fieldOfView=profile.seatedFieldOfView;
            EditorUtility.SetDirty(profile);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Stage4] Connected mouse intentions, physical HUD and camera profile.");
        }
    }
}
