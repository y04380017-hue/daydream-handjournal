using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VintageJournal.Editor
{
    [InitializeOnLoad]
    public static class DeskUsabilityValidation
    {
        const string Pending = "VintageJournal.DeskUsability.Pending";
        static bool waiting; static double started;
        static DeskUsabilityValidation() { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Changed; }
        public static void Prepare() { DeskUsabilitySetup.Apply(); Data(); }
        public static void FinalCandidate() { Prepare(); DeskUsabilityLegacySnapshotCheck.Run(); DeskSceneBuilder.BuildWindows(); }
        public static void Data() { AtelierFullValidation.Data(); DeskUsabilityDataChecks.Run(); }
        public static void Begin()
        {
            if (!Environment.GetCommandLineArgs().Contains("--desk-revision-validation")) throw new InvalidOperationException("Requires isolated validation save.");
            Data(); EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");
            SessionState.SetBool(Pending, true); waiting = true; started = EditorApplication.timeSinceStartup;
        }
        static void Tick() { if (waiting && !EditorApplication.isCompiling && !EditorApplication.isUpdating && EditorApplication.timeSinceStartup - started > 2) { waiting = false; EditorApplication.isPlaying = true; } }
        static void Changed(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var p = new GameObject("Desk usability Play evidence").AddComponent<DeskUsabilityProbe>();
            p.output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs/desk-usability/play");
            p.completed = pass => { if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1); else EditorApplication.isPlaying = false; };
        }
    }
}
