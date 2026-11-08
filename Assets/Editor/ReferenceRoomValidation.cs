using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace VintageJournal.Editor
{
    [InitializeOnLoad]public static class ReferenceRoomValidation
    {
        const string Pending="VintageJournal.ReferenceRoom.Pending";static bool waiting;static double started;
        static ReferenceRoomValidation(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;}
        public static void Data(){AtelierFullValidation.Data();DeskUsabilityDataChecks.Run();ReferenceRoomDataChecks.Run();}
        public static void Prepare(){DeskReferenceSetup.Apply();SeatedPanoramaSetup.Apply();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Data();}
        public static void Final(){Prepare();DeskUsabilityLegacySnapshotCheck.Run();DeskSceneBuilder.BuildWindows();}
        public static void Begin(){if(!Environment.GetCommandLineArgs().Contains("--desk-revision-validation"))throw new InvalidOperationException("Use an isolated validation save.");Data();EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");waiting=true;started=EditorApplication.timeSinceStartup;SessionState.SetBool(Pending,true);}
        static void Tick(){if(waiting&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&EditorApplication.timeSinceStartup-started>2){waiting=false;EditorApplication.isPlaying=true;}}
        static void Changed(PlayModeStateChange s){if(s!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;SessionState.SetBool(Pending,false);var p=new GameObject("Reference room Play evidence").AddComponent<ReferenceRoomProbe>();p.output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"docs/reference-room/play");p.completed=pass=>{if(Application.isBatchMode)EditorApplication.Exit(pass?0:1);else EditorApplication.isPlaying=false;};}
    }
}
