using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace VintageJournal.Editor
{
    /// <summary>Batch entry: opens the desk scene and enters Play Mode; NightDreamcoreProbe captures, measures and exits.</summary>
    public static class NightDreamcoreCheck
    {
        public static void Begin()
        {
            if (!Environment.GetCommandLineArgs().Contains("--night-dreamcore-check")) throw new InvalidOperationException("Pass --night-dreamcore-check <dir>.");
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");
            EditorApplication.update += Enter;
        }
        static void Enter()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Enter; EditorApplication.isPlaying = true;
        }
    }
}
