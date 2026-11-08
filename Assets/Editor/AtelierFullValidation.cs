using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VintageJournal.Editor
{
    [InitializeOnLoad]
    public static class AtelierFullValidation
    {
        const string Pending = "VintageJournal.AllStages.Pending";
        static bool waiting;
        static double started;
        static AtelierFullValidation() { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Changed; }
        [MenuItem("Vintage Journal/Complete gameplay/Run data checks")]
        public static void Data()
        {
            bool pass = AtelierProgressChecks.RunDataChecks() && PersonalStudioChecks.RunDataChecks() && AtelierSocialChecks.RunDataChecks() &&
                NightGameplayDataChecks.RunDataChecks() && NightCommissionChecks.RunDataChecks() && CraftDataTests.RunDataChecks() &&
                JournalDataTests.RunDataChecks() && DecorationDataTests.RunDataChecks() && CommissionDataTests.RunDataChecks();
            Directory.CreateDirectory("docs/all-stages");
            File.WriteAllText("docs/all-stages/data-result.json", JsonUtility.ToJson(new DataResult { timestampUtc = DateTime.UtcNow.ToString("O"), passed = pass, unityVersion = Application.unityVersion }, true));
            if (!pass) throw new InvalidOperationException("Complete gameplay data suites failed; inspect logs.");
        }
        [Serializable] sealed class DataResult { public string timestampUtc, unityVersion; public bool passed; }
        public static void Begin()
        {
            if (!Environment.GetCommandLineArgs().Contains("--atelier-full-validation") || !Environment.GetCommandLineArgs().Contains("--desk-revision-validation"))
                throw new InvalidOperationException("Use isolated full-validation flags.");
            Data();
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");
            SessionState.SetBool(Pending, true); waiting = true; started = EditorApplication.timeSinceStartup;
        }
        static void Tick()
        { if (waiting && !EditorApplication.isCompiling && !EditorApplication.isUpdating && EditorApplication.timeSinceStartup - started > 2) { waiting = false; EditorApplication.isPlaying = true; } }
        static void Changed(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var probe = new GameObject("Full gameplay evidence").AddComponent<AtelierFullProbe>();
            probe.output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs/all-stages/play");
            probe.completed = pass => { if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1); else EditorApplication.isPlaying = false; };
        }
    }
}
