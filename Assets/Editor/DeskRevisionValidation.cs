using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VintageJournal.Editor
{
    [InitializeOnLoad]
    public static class DeskRevisionValidation
    {
        const string Pending="DeskRevision.PlayValidation";
        static bool waiting;static double started;
        static DeskRevisionValidation(){EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Tick;}
        public static void Begin()
        {
            if(!Environment.GetCommandLineArgs().Contains("--desk-revision-validation"))throw new InvalidOperationException("Isolated save flag required.");
            if(Environment.GetCommandLineArgs().Contains("--luminous-validation"))
                foreach(var guid in AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Daydream/LuminousPost"}))
                {
                    var shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                    if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader compilation failed: "+shader.name);
                }
            if(!CraftDataTests.RunDataChecks()||!JournalDataTests.RunDataChecks()||!DecorationDataTests.RunDataChecks()||!CommissionDataTests.RunDataChecks())throw new InvalidOperationException("Legacy data suites failed.");
            var save=Path.Combine(Application.persistentDataPath,"DeskRevisionValidation","journal-v1.json");Directory.CreateDirectory(Path.GetDirectoryName(save));
            if(File.Exists(save))File.Copy(save,save+".before-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
            File.WriteAllText(save,JsonUtility.ToJson(JournalDocument.CreateDefault(JournalPaperCatalog.Load()),true));
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");SessionState.SetBool(Pending,true);waiting=true;started=EditorApplication.timeSinceStartup;
        }
        static void Tick(){if(waiting&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&EditorApplication.timeSinceStartup-started>2){waiting=false;EditorApplication.isPlaying=true;}}
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
            SessionState.SetBool(Pending,false);var probe=new GameObject("Desk revision Play acceptance").AddComponent<DeskRevisionProbe>();
            probe.output=Path.GetFullPath(Environment.GetCommandLineArgs().Contains("--luminous-validation")?"docs/luminous-post/play":"docs/desk-revision/play");probe.completed=pass=>{if(Application.isBatchMode)EditorApplication.Exit(pass?0:1);else EditorApplication.isPlaying=false;};
        }
    }
}
