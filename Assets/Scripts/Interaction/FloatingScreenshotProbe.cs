using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Opt-in standalone GPU evidence. Uses an isolated save and runtime APIs, not OS mouse events.</summary>
    public sealed class FloatingScreenshotProbe : MonoBehaviour
    {
        string folder;
        readonly List<string> errors = new List<string>();
        [Serializable] sealed class Result
        {
            public bool passed;
            public string scope, graphicsDevice;
            public int frames;
            public string[] errors;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--dream-screens");
            if(index<0||index+1>=args.Length||!args.Contains("--commission-validation"))return;
            var probe=new GameObject("Floating standalone evidence").AddComponent<FloatingScreenshotProbe>();
            probe.folder=args[index+1];DontDestroyOnLoad(probe.gameObject);
            Application.runInBackground=true;
        }
        void OnEnable()=>Application.logMessageReceived+=Log;
        void OnDisable()=>Application.logMessageReceived-=Log;
        void Log(string message,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));
            yield return new WaitForSecondsRealtime(.5f);
        }
        IEnumerator JournalSettled(JournalController journal)
        {
            float until=Time.realtimeSinceStartup+8;
            while((journal.IsBusy||journal.cameraRig.IsTransitioning)&&Time.realtimeSinceStartup<until)yield return null;
            if(journal.IsBusy||journal.cameraRig.IsTransitioning)errors.Add("Journal transition exceeded eight seconds");
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            float until=Time.realtimeSinceStartup+30;WorkspaceGameplayController workspace=null;
            while(Time.realtimeSinceStartup<until)
            {
                workspace=FindFirstObjectByType<WorkspaceGameplayController>();
                if(workspace&&workspace.Ready)break;
                yield return null;
            }
            if(!workspace||!workspace.Ready){Finish(false,"Workspace initialization timeout");yield break;}
            foreach(var router in FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None))router.enabled=false;
            foreach(var input in FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None))input.enabled=false;
            foreach(var input in FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None))input.enabled=false;
            var cycle=FindFirstObjectByType<DreamTimeController>();
            if(!cycle){Finish(false,"Missing day/night controller");yield break;}
            cycle.SetNight(false,false,true);yield return Shot("start-day");
            workspace.desk.Begin();yield return Shot("desk-day");
            workspace.desk.OpenSettings();cycle.SetNight(true,false);
            yield return new WaitForSecondsRealtime(cycle.NightTransitionDuration+.2f);yield return Shot("settings-night");
            workspace.desk.HandleBack();yield return Shot("desk-night");
            foreach(var passer in FindObjectsByType<DreamSkyPasser>(FindObjectsSortMode.None))
            {passer.enabled=false;passer.Sample(passer.duration*.5f-passer.phaseSeconds);}
            yield return Shot("night-passing-objects");
            bool night=cycle.IsNight&&!cycle.IsTransitioning&&cycle.workLamp.intensity>.08f;
            cycle.SetNight(false,false);yield return new WaitForSecondsRealtime(cycle.NightTransitionDuration+.2f);
            bool day=!cycle.IsNight&&!cycle.IsTransitioning;
            var journal=workspace.journal;bool entered=journal.EnterEditing();
            yield return JournalSettled(journal);
            journal.BeginPaperSelection(JournalSide.Left);journal.SelectPaper("page_02");journal.ConfirmPaper();
            journal.BeginPaperSelection(JournalSide.Right);journal.SelectPaper("page_03");journal.ConfirmPaper();
            journal.ZoomBy(.52f);yield return new WaitForSecondsRealtime(.8f);yield return Shot("new-paper-detail");
            bool paper=journal.Document.GetPage(JournalSide.Left).paperId=="page_02"&&journal.Document.GetPage(JournalSide.Right).paperId=="page_03";
            journal.ExitEditing();yield return JournalSettled(journal);
            bool saved=workspace.Service.Flush();
            Debug.Log("[FloatingProbe] day="+day+" night="+night+" entered="+entered+" paper="+paper+" saved="+saved);
            Finish(day&&night&&entered&&paper&&saved,"Rendered standalone, runtime API actions and real-frame screenshots including UI. Native mouse input not tested.");
        }
        void Finish(bool passed,string scope)
        {
            var result=new Result{passed=passed&&errors.Count==0,scope=scope,graphicsDevice=SystemInfo.graphicsDeviceName,frames=Time.frameCount,errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(result,true));
            Application.Quit(result.passed?0:1);
        }
    }
}
