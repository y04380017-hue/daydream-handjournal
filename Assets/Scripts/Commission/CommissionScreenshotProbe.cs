using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Player-only evidence probe, active solely with "--commission-screens &lt;dir&gt;" (always paired with --commission-validation,
    /// which isolates the save). Drives the same runtime actions as the Play harness and captures real frames including IMGUI.
    /// It does not send native OS mouse events.
    /// </summary>
    public sealed class CommissionScreenshotProbe : MonoBehaviour
    {
        string folder;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "--commission-screens");
            if (i < 0 || i + 1 >= args.Length || !args.Contains("--commission-validation")) return;
            var probe = new GameObject("Commission screenshot probe").AddComponent<CommissionScreenshotProbe>();
            probe.folder = args[i + 1]; DontDestroyOnLoad(probe.gameObject);
        }
        IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(.6f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name)); Debug.Log("[CommissionProbe] Shot:" + name);
            yield return new WaitForSecondsRealtime(.6f);
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            WorkspaceGameplayController workspace = null; CommissionDeskController desk = null; float until = Time.realtimeSinceStartup + 30;
            while ((workspace == null || !workspace.Ready || desk == null || !desk.Ready) && Time.realtimeSinceStartup < until)
            { workspace = FindFirstObjectByType<WorkspaceGameplayController>(); desk = FindFirstObjectByType<CommissionDeskController>(); yield return null; }
            if (desk == null || !desk.Ready) { Debug.LogError("[CommissionProbe] Not ready"); Application.Quit(2); yield break; }
            yield return Shot("player-start.png");
            workspace.desk.Begin();
            until = Time.realtimeSinceStartup + 10;
            while (!desk.Service.OpenLetters.Any() && Time.realtimeSinceStartup < until) yield return null;
            yield return Shot("player-desk-unread.png");
            desk.OpenPanel(); yield return Shot("player-letter-panel.png");
            var rose = FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).FirstOrDefault(p => p.assetId == "sticker_02" && p.gameObject.activeInHierarchy);
            string page = workspace.journal.Document.GetPage(JournalSide.Left).pageId;
            if (rose && workspace.GrabPhysical(rose))
                workspace.Attach(new CraftPlacement { owner = CraftOwner.JournalPage, containerId = "journal", pageId = page, position = new Vector2(.45f, .55f), scale = 1.4f });
            desk.SelectTarget(CraftOwner.JournalPage, "journal", page);
            yield return Shot("player-checklist.png");
            desk.SetCompact(true); yield return Shot("player-panel-compact.png"); desk.SetCompact(false);
            bool sent = desk.SendActive(); Debug.Log("[CommissionProbe] Sent:" + sent + " " + desk.Status);
            yield return Shot("player-sent.png");
            desk.Service.FastForwardReplies(); desk.ClosePanel(); yield return new WaitForSecondsRealtime(.4f);
            desk.OpenPanel(); yield return Shot("player-reply.png");
            desk.ClosePanel(); yield return Shot("player-desk-after.png");
            // Settings panel with the day / dusk / night buttons, then dusk itself.
            workspace.desk.OpenSettings(); yield return Shot("player-settings.png");
            var time = FindFirstObjectByType<DreamTimeController>();
            if (time) { time.SetDusk(true, false); yield return new WaitForSecondsRealtime(3f); yield return Shot("player-settings-dusk.png"); time.SetNight(false, false, true); }
            workspace.desk.HandleBack(); yield return new WaitForSecondsRealtime(.4f);
            workspace.Service.Flush(); Debug.Log("[CommissionProbe] Done"); Application.Quit(0);
        }
    }
}
