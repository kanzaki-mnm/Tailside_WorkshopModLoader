using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("local.workshopbridge.testdriver", "Bridge Test Driver", "1.0.2")]
public sealed class BridgeTestDriver : BaseUnityPlugin
{
    public static bool Started;
    private string diagnostic;
    private bool testing;
    private void Awake()
    {
        testing=Environment.GetCommandLineArgs().Contains("--bridge-smoke-test") && File.Exists(Path.Combine(Paths.GameRootPath,".modsetup-sandbox"));
        if(!testing) return;
        diagnostic=Path.Combine(Paths.ConfigPath,"DriverDiagnostic.log");
        File.AppendAllText(diagnostic,"Awake args="+string.Join(" | ",Environment.GetCommandLineArgs())+"\nassembly="+GetType().Assembly.Location+" enabled="+enabled+" active="+gameObject.activeInHierarchy+"\n");
        Application.runInBackground=true;
        // Independent observer survives even when the actual BepInEx manager is destroyed.
        // It never changes flags or settings on that manager, and exists only in this test DLL.
        var observer=new GameObject("LoaderLifecycleTestObserver");
        observer.hideFlags=HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(observer);
        observer.AddComponent<LoaderLifecycleTestObserver>();
    }
    private void Start(){if(testing){Started=true;File.AppendAllText(diagnostic,"Start\n");}}
    private void OnDisable(){if(testing)File.AppendAllText(diagnostic,"OnDisable\n");}
    private void OnDestroy(){if(testing)File.AppendAllText(diagnostic,"OnDestroy\n");}
}
public sealed class LoaderLifecycleTestObserver : MonoBehaviour
{
    private float started;
    private bool reported;
    private bool startupOnly;
    private void Awake(){started=Time.realtimeSinceStartup;startupOnly=Environment.GetCommandLineArgs().Contains("--loader-startup-test");}
    private void Update()
    {
        if(reported) return;
        var scene=SceneManager.GetActiveScene().name;
        var steam=Steamworks.SteamClient.IsValid;
        if(Time.realtimeSinceStartup-started<2 || (!startupOnly && (scene!="MainMenu" || !steam))) return;
        reported=true;
        var manager=Chainloader.ManagerObject;
        var report=new List<string>{"BRIDGE_TEST_SCENE="+scene+" STEAM_VALID="+steam,"MANAGER_ALIVE="+(manager!=null)+" FLAGS="+(manager!=null?(int)manager.hideFlags:0),"DRIVER_STARTED="+BridgeTestDriver.Started};
        foreach(var p in Chainloader.PluginInfos.Values.OrderBy(p=>p.Metadata.GUID))
            report.Add("BRIDGE_TEST_PLUGIN "+p.Metadata.GUID+" version="+p.Metadata.Version+" alive="+(p.Instance!=null)+" path="+p.Location);
        report.Add(startupOnly?"LOADER_STARTUP_ONLY (no gameplay assertion)":"BRIDGE_TEST_QUIT (isolated copy; no save opened)");
        File.WriteAllLines(Path.Combine(Paths.ConfigPath,"BridgeTestDriver.report.log"),report);
        File.AppendAllText(Path.Combine(Paths.ConfigPath,"DriverDiagnostic.log"),"Observer report; requesting Application.Quit\n");
        Application.Quit(0);
    }
}
