using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ModEnvironmentSetup.Core;
using UnifiedTools.Core;
namespace UnifiedTools;
public partial class App:Application
{
    public const string ToolVersion=SetupEngine.Version;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args=Environment.GetCommandLineArgs();var at=Array.IndexOf(args,"--game-dir");string? root=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):null;
        using var resource=typeof(App).Assembly.GetManifestResourceStream("Setup.Payload.zip")!;
        var controller=new EnvironmentController(SetupEngine.LoadPayload(resource),Running);
        if(root==null)for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)if(controller.Setup.CanRemoveForRecovery(d.FullName)){root=d.FullName;break;}
        if(root==null){var roots=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam")};
            foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})try{using var k=hive.OpenSubKey(@"Software\Valve\Steam");if(k?.GetValue("SteamPath") is string p)roots.Add(p);}catch{}
            var games=SteamDiscovery.Find(roots);if(games.Count==1)root=games[0];}
        var window=new RecoveryWindow(controller,root);MainWindow=window;
        SupportInfo.GuideUrl=SupportInfo.ReleasePageUrl;
        ModTools.Desktop.HelpSupport.Attach(this,"Tailside Mod Environment Setup & Recovery",ToolVersion,ModManager.UiLanguage.Get,()=>window.CanSaveAutomatically);
        window.Show();
    }
    public static bool Running(){var processes=Process.GetProcessesByName("Tailside");try{return processes.Length>0;}finally{foreach(var p in processes)p.Dispose();}}
}




