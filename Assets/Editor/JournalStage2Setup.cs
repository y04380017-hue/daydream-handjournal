using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace VintageJournal.Editor
{
    public static class JournalStage2Setup
    {
        [MenuItem("Vintage Journal/Install journal editing into existing scene")]
        public static void Install()
        {
            Directory.CreateDirectory("docs/stage2-logs");
            const string catalogPath="Assets/Resources/JournalPaperCatalog.asset";
            var catalog=AssetDatabase.LoadAssetAtPath<JournalPaperCatalog>(catalogPath);
            if(!catalog){catalog=ScriptableObject.CreateInstance<JournalPaperCatalog>();AssetDatabase.CreateAsset(catalog,catalogPath);}
            catalog.papers=new JournalPaperDefinition[24];
            for(int i=0;i<24;i++)catalog.papers[i]=new JournalPaperDefinition {
                id="page_"+(i+1).ToString("00"),displayName="内页 "+(i+1).ToString("00"),
                texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/JournalPages/PAGE_"+(i+1).ToString("00")+".png")};
            EditorUtility.SetDirty(catalog);
            const string prefabPath="Assets/Prefabs/OpenJournal.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var view=root.GetComponent<JournalPageView>()??root.AddComponent<JournalPageView>();
                view.leftRenderer=root.transform.Find("Left decorated page").GetComponent<Renderer>();
                view.rightRenderer=root.transform.Find("Right decorated page").GetComponent<Renderer>();
                var flip=root.GetComponent<JournalFlipController>()??root.AddComponent<JournalFlipController>();
                var controller=root.GetComponent<JournalController>()??root.AddComponent<JournalController>();
                controller.Catalog=catalog;controller.pageView=view;controller.flip=flip;
                var ui=root.GetComponent<JournalEditorUI>()??root.AddComponent<JournalEditorUI>();ui.journal=controller;
                PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            var scene=EditorSceneManager.OpenScene(DeskSceneBuilder.ScenePath,OpenSceneMode.Single);
            var rig=UnityEngine.Object.FindFirstObjectByType<DeskCamera>();
            rig.inspectionPosition=new Vector3(0,1.8f,-.43f);rig.inspectionTarget=new Vector3(0,.823f,-.26f);
            rig.transitionSeconds=.65f;rig.inspectionFieldOfView=42;
            var journal=UnityEngine.Object.FindFirstObjectByType<JournalController>();
            if(!journal)
            {
                // Phase-one scene contains the saved original object, not a linked prefab instance.
                var sceneBook=GameObject.Find("OpenJournal");
                if(!sceneBook)throw new InvalidOperationException("Existing journal object is missing.");
                var view=sceneBook.AddComponent<JournalPageView>();
                view.leftRenderer=sceneBook.transform.Find("Left decorated page").GetComponent<Renderer>();
                view.rightRenderer=sceneBook.transform.Find("Right decorated page").GetComponent<Renderer>();
                var flip=sceneBook.AddComponent<JournalFlipController>();
                journal=sceneBook.AddComponent<JournalController>();journal.Catalog=catalog;journal.pageView=view;journal.flip=flip;
                sceneBook.AddComponent<JournalEditorUI>().journal=journal;
            }
            journal.cameraRig=rig;
            EditorUtility.SetDirty(rig);EditorUtility.SetDirty(journal);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("docs/stage2-logs/setup.txt","Updated existing scene and OpenJournal prefab; retained original meshes, images, other prefabs, and GUIDs.\n24 ScriptableObject paper definitions, 24 independent page instances at runtime.\n");
            Debug.Log("[JournalSetup] Updated existing scene and journal prefab.");
        }
    }
}
