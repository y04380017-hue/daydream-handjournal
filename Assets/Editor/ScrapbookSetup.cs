using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace VintageJournal.Editor
{
    public static class ScrapbookSetup
    {
        [MenuItem("Vintage Journal/Stage 3/Install scrapbook into existing scene")]
        public static void Install()
        {
            Directory.CreateDirectory("docs/stage3-logs");DecorationCatalogBuilder.BuildCatalog();
            var catalog=DecorationCatalog.Load();
            var prefab=PrefabUtility.LoadPrefabContents("Assets/Prefabs/OpenJournal.prefab");
            try{Attach(prefab,catalog);PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Prefabs/OpenJournal.prefab");}finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var note=PrefabUtility.LoadPrefabContents("Assets/Prefabs/NoteBox.prefab");
            try{BindNote(note);PrefabUtility.SaveAsPrefabAsset(note,"Assets/Prefabs/NoteBox.prefab");}finally{PrefabUtility.UnloadPrefabContents(note);}
            var scene=EditorSceneManager.OpenScene(DeskSceneBuilder.ScenePath,OpenSceneMode.Single);
            Attach(GameObject.Find("OpenJournal"),catalog);BindNote(GameObject.Find("NoteBox"));
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.WriteAllText("docs/stage3-logs/setup.txt","Original scene retained. OpenJournal has independent scrapbook controller/input/UI/renderer; NoteBox has selectable collider. Catalog uses original 12 tapes,20 stickers,8 notes.\n");
            Debug.Log("[ScrapbookSetup] Installed into original scene and prefabs.");
        }
        static void Attach(GameObject root,DecorationCatalog catalog)
        {
            var renderer=root.GetComponent<DecorationRenderer>()??root.AddComponent<DecorationRenderer>();renderer.catalog=catalog;renderer.journal=root.GetComponent<JournalController>();
            var book=root.GetComponent<ScrapbookController>()??root.AddComponent<ScrapbookController>();book.journal=renderer.journal;book.visuals=renderer;book.Catalog=catalog;
            var input=root.GetComponent<ScrapbookInput>()??root.AddComponent<ScrapbookInput>();input.book=book;
            var ui=root.GetComponent<ScrapbookUI>()??root.AddComponent<ScrapbookUI>();ui.book=book;
        }
        static void BindNote(GameObject root)
        {
            var item=root.GetComponent<DeskItem>()??root.AddComponent<DeskItem>();item.kind=DeskItemKind.Note;item.displayName="复古便签收纳盒";
            var collider=root.GetComponent<BoxCollider>();if(!collider)collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0,.04f,0);collider.size=new Vector3(.23f,.085f,.2f);
        }
    }
}
