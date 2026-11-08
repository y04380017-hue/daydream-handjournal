using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Opt-in standalone measurement. Does not activate any gameplay or UI methods.</summary>
    public sealed class DeskDiagnostics : MonoBehaviour
    {
        private const int FrameCount = 600;
        private readonly List<float> timings = new List<float>(FrameCount);
        private string outputPath;
        private DeskUI ui;
        private DeskCamera cameraRig;
        private float warmup;
        private double sampleStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor) return;
            string output = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--desk-probe-output=", StringComparison.Ordinal))
                    output = args[i].Substring("--desk-probe-output=".Length);
                else if (args[i] == "--desk-probe-output" && i + 1 < args.Length) output = args[++i];
            }
            if (string.IsNullOrWhiteSpace(output)) return;
            var diagnostics = new GameObject("Opt-in standalone performance diagnostics").AddComponent<DeskDiagnostics>();
            diagnostics.outputPath = output;
            DontDestroyOnLoad(diagnostics.gameObject);
            Debug.Log("[Desk] Performance:Armed; waiting for 600 consecutive default-desk frames after 3s warmup");
        }

        private void Update()
        {
            if (!ui) ui = FindFirstObjectByType<DeskUI>();
            if (!cameraRig) cameraRig = FindFirstObjectByType<DeskCamera>();
            if (!ui || !ui.HasBegun || ui.IsBlocking || !cameraRig || cameraRig.IsInspecting ||
                cameraRig.IsTransitioning || !Application.isFocused)
            {
                warmup = 0;
                timings.Clear();
                return;
            }
            if (warmup < 3)
            {
                warmup += Time.unscaledDeltaTime;
                return;
            }
            if (timings.Count == 0) sampleStarted = Time.realtimeSinceStartupAsDouble;
            timings.Add(Time.unscaledDeltaTime * 1000f);
            if (timings.Count < FrameCount) return;
            Save();
            Destroy(gameObject);
        }

        [Serializable]
        private sealed class PerformanceResult
        {
            public string timestampUtc;
            public string scope;
            public string unityVersion;
            public string operatingSystem;
            public string processor;
            public int processorCount;
            public int systemMemoryMB;
            public string graphicsDevice;
            public string graphicsApi;
            public int graphicsMemoryMB;
            public string renderPipeline;
            public int width;
            public int height;
            public double displayRefreshHz;
            public bool fullScreen;
            public int vSyncCount;
            public int targetFrameRate;
            public int frames;
            public double actualElapsedSeconds;
            public float minFrameMs;
            public float maxFrameMs;
            public float meanFrameMs;
            public float p95FrameMs;
            public float averageFps;
            public float minimumFps;
            public int framesBelow55Fps;
            public float[] frameTimesMs;
        }

        private void Save()
        {
            float[] sorted = timings.ToArray();
            Array.Sort(sorted);
            float sum = 0;
            int below55 = 0;
            foreach (float time in timings) { sum += time; if (time > 1000f / 55f) below55++; }
            float mean = sum / timings.Count;
            var result = new PerformanceResult {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                scope = "Actual standalone player Time.unscaledDeltaTime. Default desk, focused window, 3 second warmup, 600 consecutive frames. Does not measure GPU time separately.",
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemoryMB = SystemInfo.systemMemorySize,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsMemoryMB = SystemInfo.graphicsMemorySize,
                renderPipeline = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : "Built-in",
                width = Screen.width, height = Screen.height,
                displayRefreshHz = Screen.currentResolution.refreshRateRatio.value,
                fullScreen = Screen.fullScreen,
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                frames = timings.Count,
                actualElapsedSeconds = Time.realtimeSinceStartupAsDouble - sampleStarted,
                minFrameMs = sorted[0], maxFrameMs = sorted[sorted.Length - 1],
                meanFrameMs = mean,
                p95FrameMs = sorted[Mathf.CeilToInt(.95f * sorted.Length) - 1],
                averageFps = 1000f / mean,
                minimumFps = 1000f / sorted[sorted.Length - 1],
                framesBelow55Fps = below55,
                frameTimesMs = timings.ToArray()
            };
            try
            {
                string fullPath = Path.GetFullPath(outputPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(fullPath, JsonUtility.ToJson(result, true));
                Debug.Log("[Desk] Performance:Saved:" + fullPath + "; frames=" + timings.Count +
                    "; meanMs=" + mean.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; p95Ms=" + result.p95FrameMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception error) { Debug.LogError("[Desk] Performance:WriteFailed:" + error.Message); }
        }
    }
}
