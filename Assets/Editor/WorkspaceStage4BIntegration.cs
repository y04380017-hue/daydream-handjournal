using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace VintageJournal.Editor
{
    public static class WorkspaceStage4BIntegration
    {
        [MenuItem("Vintage Journal/Stage 4B/Build and connect workspace")]
        public static void BuildAndConnect(){WorkspaceStage4BSetup.Build();Configure();}
        public static void Configure()
        {
            var root=GameObject.Find("WorkspaceStage4B");if(!root)throw new InvalidOperationException("Install Stage4B fixtures first.");
            var physical=Object.FindFirstObjectByType<PhysicalInteractionController>();
            var coordinator=root.GetComponent<WorkspacePhysicalCoordinator>();if(!coordinator)coordinator=root.AddComponent<WorkspacePhysicalCoordinator>();
            coordinator.physical=physical;
            var workspace=root.GetComponent<WorkspaceGameplayController>();if(!workspace)workspace=root.AddComponent<WorkspaceGameplayController>();
            workspace.journal=Object.FindFirstObjectByType<JournalController>();workspace.scrapbook=Object.FindFirstObjectByType<ScrapbookController>();
            workspace.physical=physical;workspace.coordinator=coordinator;workspace.desk=Object.FindFirstObjectByType<DeskUI>();workspace.lens=Camera.main;
            workspace.releaseBooks=Object.FindObjectsByType<ReleaseBookView>(FindObjectsSortMode.None);
            var router=root.GetComponent<WorkspaceInputRouter>();if(!router)router=root.AddComponent<WorkspaceInputRouter>();router.workspace=workspace;
            var renderer=root.GetComponent<CraftMaterialRenderer>();if(!renderer)renderer=root.AddComponent<CraftMaterialRenderer>();renderer.workspace=workspace;workspace.materialRenderer=renderer;
            var hud=root.GetComponent<WorkspaceGameplayHUD>();if(!hud)hud=root.AddComponent<WorkspaceGameplayHUD>();hud.workspace=workspace;workspace.hud=hud;
            foreach(var old in Object.FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsInactive.Include,FindObjectsSortMode.None))old.enabled=false;
            foreach(var old in Object.FindObjectsByType<PhysicalWorkspaceHUD>(FindObjectsInactive.Include,FindObjectsSortMode.None))old.enabled=false;
            physical.interactionOrigin=Camera.main.transform;
            EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);AssetDatabase.SaveAssets();
            Debug.Log("[Stage4B] Connected unified input, shared ownership store and 3D release-book views.");
        }
        public static void DataChecks()
        {
            bool journal=JournalDataTests.RunDataChecks(),decorations=DecorationDataTests.RunDataChecks(),craft=CraftDataTests.RunDataChecks();
            if(!journal||!decorations||!craft)
                throw new InvalidOperationException("Stage4B data regressions failed.");
        }
        public static void PrepareNativeFixture()
        {
            string path=Path.Combine(Application.persistentDataPath,"Stage4BNativeValidation","journal-v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));Directory.CreateDirectory("docs/stage4b-logs");
            if(File.Exists(path))File.Copy(path,path+".before-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"),false);
            var catalog=JournalPaperCatalog.Load();var store=new JournalSaveService(catalog,path);
            if(!store.Save(JournalDocument.CreateDefault(catalog)))throw new InvalidOperationException(store.LastMessage);
            File.WriteAllText("docs/stage4b-logs/native-fixture.txt",DateTime.UtcNow.ToString("O")+"\n"+path+"\nIsolated native validation save; normal player journal untouched.");
        }
    }
}
