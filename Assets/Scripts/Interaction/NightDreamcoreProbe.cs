using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>
    /// Evidence probe, active only with "--night-dreamcore-check &lt;dir&gt;" (editor Play or Windows player; pair with
    /// --commission-validation for an isolated save). Captures the seated camera (no IMGUI) for day, the night
    /// transition at 25/50/75 % and full night, and measures the spec's grayscale rule.
    /// </summary>
    public sealed class NightDreamcoreProbe : MonoBehaviour
    {
        [Serializable] class Stats { public string shot; public float rawNight; public float minLuma, p001Luma, p01Luma, darkestBlock, fractionBelow15, meanLuma; public string darkestBlockAt; }
        [Serializable] class Report { public string timestampUtc; public string mode; public bool dreamcoreBound; public bool passed; public string verdict; public List<Stats> shots = new List<Stats>(); public List<string> notes = new List<string>(); }
        string folder;
        readonly List<(string name, float raw, Texture2D tex)> pending = new List<(string, float, Texture2D)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "--night-dreamcore-check");
            if (i < 0 || i + 1 >= args.Length) return;
            var probe = new GameObject("Night dreamcore probe").AddComponent<NightDreamcoreProbe>(); probe.folder = args[i + 1]; DontDestroyOnLoad(probe.gameObject);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            var report = new Report { timestampUtc = DateTime.UtcNow.ToString("o"), mode = Application.isEditor ? "Unity Play Mode" : "Windows player" };
            WorkspaceGameplayController workspace = null; DreamTimeController time = null; float until = Time.realtimeSinceStartup + 40;
            while ((workspace == null || !workspace.Ready || time == null) && Time.realtimeSinceStartup < until)
            { workspace = FindFirstObjectByType<WorkspaceGameplayController>(); time = FindFirstObjectByType<DreamTimeController>(); yield return null; }
            if (!workspace || !workspace.Ready || !time) { report.verdict = "workspace never became ready"; Write(report); Quit(2); yield break; }
            report.dreamcoreBound = time.dreamcore;
            workspace.desk.Begin(); yield return new WaitForSecondsRealtime(1.5f);
            var lens = workspace.lens;

            time.SetNight(false, false, true); yield return new WaitForSecondsRealtime(1.2f);
            Capture(lens, "day.png", time);
            // Real-time transition (6–10 s) sampled at three points.
            time.SetNight(true, false, false);
            foreach (var target in new[] { .25f, .5f, .75f })
            {
                float wait = Time.realtimeSinceStartup + 15;
                while (time.NightAmount < target && Time.realtimeSinceStartup < wait) yield return null;
                Capture(lens, "transition-" + Mathf.RoundToInt(target * 100) + ".png", time);
            }
            float settle = Time.realtimeSinceStartup + 15; while (time.IsTransitioning && Time.realtimeSinceStartup < settle) yield return null;
            report.notes.Add("transition seconds (measured, 0->1): " + (time.dreamcore ? time.nightTransitionSeconds : time.transitionSeconds));
            yield return new WaitForSecondsRealtime(2.5f);
            Capture(lens, "night.png", time);
            yield return new WaitForSecondsRealtime(3.1f);
            Capture(lens, "night-later.png", time);
            // Statistics only after every frame is taken, so the slow analysis never stalls the timed transition.
            foreach (var p in pending) { report.shots.Add(Analyse(p.name, p.raw, p.tex)); Destroy(p.tex); }
            var night = report.shots.First(x => x.shot == "night.png");
            report.passed = night.darkestBlock >= .15f && night.p001Luma >= .12f;
            report.verdict = $"night darkest 1/576 screen block = {night.darkestBlock:F3} (spec >= .15), 0.1th percentile pixel = {night.p001Luma:F3}, pixels below 15 % = {night.fractionBelow15:P2}";
            Write(report);
            time.SetNight(false, false, true);
            Quit(report.passed ? 0 : 1);
        }

        void Capture(Camera lens, string name, DreamTimeController time)
        {
            const int w = 1920, h = 1080; var rt = new RenderTexture(w, h, 24); var tex = new Texture2D(w, h, TextureFormat.RGB24, false); var prev = RenderTexture.active;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(lens, request)) RenderPipeline.SubmitRenderRequest(lens, request);
                else { lens.targetTexture = rt; lens.Render(); lens.targetTexture = null; }
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                pending.Add((name, time.NightAmount, tex)); tex = null;
            }
            finally { RenderTexture.active = prev; if (tex) Destroy(tex); rt.Release(); Destroy(rt); }
        }

        Stats Analyse(string name, float raw, Texture2D tex)
        {
            const int w = 1920, h = 1080;
            {
                File.WriteAllBytes(Path.Combine(folder, name), tex.EncodeToPNG());
                var px = tex.GetPixels32(); var luma = new float[px.Length];
                for (int i = 0; i < px.Length; i++) luma[i] = (.2126f * px[i].r + .7152f * px[i].g + .0722f * px[i].b) / 255f;
                var sorted = (float[])luma.Clone(); Array.Sort(sorted);
                // Region rule: 32 x 18 blocks (60 px squares at 1080p).
                float darkest = 1; string at = "";
                for (int by = 0; by < 18; by++) for (int bx = 0; bx < 32; bx++)
                {
                    double sum = 0; for (int y = by * 60; y < by * 60 + 60; y++) for (int x = bx * 60; x < bx * 60 + 60; x++) sum += luma[y * w + x];
                    float m = (float)(sum / 3600); if (m < darkest) { darkest = m; at = $"block ({bx},{17 - by}) of 32x18 from top-left"; }
                }
                var s = new Stats { shot = name, rawNight = raw, minLuma = sorted[0], p001Luma = sorted[(int)(sorted.Length * .001f)], p01Luma = sorted[(int)(sorted.Length * .01f)],
                    darkestBlock = darkest, darkestBlockAt = at, fractionBelow15 = sorted.Count(v => v < .15f) / (float)sorted.Length, meanLuma = luma.Average() };
                Debug.Log($"[NightDreamcoreProbe] {name} night={s.rawNight:F2} min={s.minLuma:F3} p0.1={s.p001Luma:F3} darkestBlock={s.darkestBlock:F3} below15={s.fractionBelow15:P2} mean={s.meanLuma:F3}");
                return s;
            }
        }

        void Write(Report r) => File.WriteAllText(Path.Combine(folder, "night-check.json"), JsonUtility.ToJson(r, true));
        static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
