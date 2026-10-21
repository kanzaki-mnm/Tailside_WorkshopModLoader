using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ModEnvironmentSetup.Core;
using ModManager.Core;
using UnifiedTools.Core;
internal static class Program {
 [STAThread] static int Main(string[] args){
  var at=Array.IndexOf(args,"--game-dir");if(at<0)throw new ArgumentException("--game-dir required");var root=Path.GetFullPath(args[at+1]);var scenario=args[0];
  var existing=scenario=="actual-manual";
  if(!existing && (root.Contains("steamapps",StringComparison.OrdinalIgnoreCase)||Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()))throw new IOException("New empty synthetic fixture only");
  if(existing){if(root.Contains("steamapps",StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(root,".modsetup-sandbox"))||File.Exists(SetupEngine.SafePath(root,SetupEngine.ReceiptPath)))throw new IOException("Marked manual copy without receipt required");}else{
  Directory.CreateDirectory(Path.Combine(root,"Tailside_Data/Managed"));File.WriteAllText(Path.Combine(root,".tool-ui-fixture"),"synthetic");
  var pe=new byte[256];BitConverter.GetBytes((ushort)0x5a4d).CopyTo(pe,0);BitConverter.GetBytes(128).CopyTo(pe,60);BitConverter.GetBytes(0x4550u).CopyTo(pe,128);BitConverter.GetBytes((ushort)0x8664).CopyTo(pe,132);File.WriteAllBytes(Path.Combine(root,"Tailside.exe"),pe);
  }
  using var resource=typeof(UnifiedTools.App).Assembly.GetManifestResourceStream("Setup.Payload.zip")!;var payload=SetupEngine.LoadPayload(resource);var controller=new EnvironmentController(payload,()=>false);
  if(scenario=="fresh-localized")foreach(var file in payload.Where(x=>x.Path.Contains("/languages/",StringComparison.Ordinal))){var path=SetupEngine.SafePath(root,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,file.Bytes);}
  if(scenario=="invalid-folder")File.Delete(Path.Combine(root,"Tailside.exe"));
  if(scenario.StartsWith("manual"))foreach(var file in payload){if(scenario=="manual-partial"&&file.Path!="winhttp.dll")continue;var path=SetupEngine.SafePath(root,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,file.Bytes);}
  else if(scenario is "update" or "tool-update"){
   var oldPayload=scenario=="update"?payload.Select(x=>x.Path==SetupEngine.BridgePath?new PayloadFile(x.Path,SetupEngine.Hash(System.Text.Encoding.UTF8.GetBytes("old loader")),System.Text.Encoding.UTF8.GetBytes("old loader")):x).ToArray():payload;
   var previous=new EnvironmentController(oldPayload,()=>false);previous.Setup.Install(root);
   var source=Path.Combine(root,"previous-download.exe");File.WriteAllText(source,"previous tool");ToolDeployment.KeepCopy(root,source,previous.Setup,"0.9.0");File.Delete(source);
  }
  else if(scenario!="fresh"&&scenario!="fresh-localized"&&scenario!="invalid-folder"&&!existing)controller.Setup.Install(root);
  if(scenario=="paused"||scenario=="runtime-paused")controller.Manager.PauseEnvironment(root);
  if(scenario=="broken")File.Delete(SetupEngine.SafePath(root,"BepInEx/core/BepInEx.dll"));
  if(scenario=="changed"||scenario=="partial-remove")File.WriteAllText(SetupEngine.SafePath(root,SetupEngine.BridgePath),"changed owned loader");
  if(scenario=="receipt")File.WriteAllText(SetupEngine.SafePath(root,SetupEngine.ReceiptPath),"broken");
  if(scenario=="runtime"||scenario=="runtime-paused"||scenario=="manual-no-mono")Directory.Delete(Path.Combine(root,"Tailside_Data/Managed"));
  var manual=existing||scenario.StartsWith("manual")||scenario=="receipt";string Snapshot(){var paths=existing?Directory.EnumerateFileSystemEntries(Path.Combine(root,"BepInEx"),"*",SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(root)):Directory.EnumerateFileSystemEntries(root,"*",SearchOption.AllDirectories);return string.Join("\n",paths.Order().Select(p=>Path.GetRelativePath(root,p)+(File.Exists(p)?":"+SetupEngine.HashFile(p):"/")));}
  var before=Snapshot();int checks=0;
  void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS UI "+scenario+" "+label);}
  void Capture(Window window,string suffix=""){Directory.CreateDirectory("UnifiedTools/validation");window.UpdateLayout();var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create("UnifiedTools/validation/recovery-"+scenario+suffix+".png");encoder.Save(output);}
  var app=new UnifiedTools.App();app.InitializeComponent();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};bool started=false;
  timer.Tick+=async(_,_)=>{
   if(started||app.MainWindow is not UnifiedTools.RecoveryWindow window)return;started=true;timer.Stop();
   try{
    Check(window.FindName("Mods")==null&&window.FindName("PauseAll")==null,"old mod management controls absent");
    Check(window.FindName("Primary") is Button,"unified primary action exists");
    var symbol=(UnifiedTools.StatusSymbol)window.FindName("StatusIcon");
    var expectedSymbol=scenario switch{
     "fresh" or "fresh-localized" or "invalid-folder"=>UnifiedTools.StatusSymbolKind.Package,
     "manual" or "manual-partial" or "manual-no-mono" or "actual-manual"=>UnifiedTools.StatusSymbolKind.Information,
     "installed" or "safety" or "permission-messages"=>UnifiedTools.StatusSymbolKind.Check,
     "update" or "tool-update"=>UnifiedTools.StatusSymbolKind.Update,
     "paused" or "runtime-paused"=>UnifiedTools.StatusSymbolKind.Paused,
     _=>UnifiedTools.StatusSymbolKind.Warning
    };
    Check(symbol.Kind==expectedSymbol,"state uses the corresponding vector symbol");
    foreach(var name in new[]{((FrameworkElement)window.FindName("Primary")).IsVisible?"Primary":((FrameworkElement)window.FindName("Remove")).IsVisible?"Remove":"Guide","Guide"}){var button=(FrameworkElement)window.FindName(name);Check(button.IsVisible&&button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize)).Bottom<=window.ActualHeight,name+" visible without scrolling");}
    Check(before==Snapshot(),"opening window does not change files or create tool/settings");
    var normalHeight=window.Height;window.Height=400;window.UpdateLayout();foreach(var name in new[]{((FrameworkElement)window.FindName("Primary")).IsVisible?"Primary":((FrameworkElement)window.FindName("Remove")).IsVisible?"Remove":"Guide","Guide"}){var b=(FrameworkElement)window.FindName(name);Check(b.TransformToAncestor(window).TransformBounds(new Rect(b.RenderSize)).Bottom<=window.ActualHeight,"small window keeps "+name+" visible");}window.Height=normalHeight;window.UpdateLayout();
    Check(!((Expander)window.FindName("DetailsPanel")).IsExpanded,"recovery starts collapsed in every state");
    Check(window.FindName("Details")==null,"raw diagnostic states are not UI controls");
    Capture(window);
    foreach(var language in new[]{"en","ja"}.Where(code=>ModManager.UiLanguage.Options.Any(x=>x.Code==code))){
     ModManager.UiLanguage.Select(language,persist:false);window.RefreshView();
     var japanese=language=="ja";
     Check(((TextBlock)window.FindName("PageTitle")).Text==(japanese?"Mod環境の導入・復旧":"Mod Environment Setup & Recovery"),language+" title identifies shared mod environment");
     Check(((Expander)window.FindName("DetailsPanel")).Header?.ToString()==(japanese?"ゲームが起動しないとき・修復":"Game won't start? Repair & recovery"),language+" recovery wording");
     var explanation=scenario switch{
      "fresh" or "fresh-localized"=>japanese?"Modを使うための準備を行います":"Set up the environment needed to use mods.",
      "installed" or "safety" or "permission-messages"=>japanese?"追加のセットアップは不要です":"No additional setup is needed.",
      "update" or "tool-update"=>japanese?"現在のModと設定を残して更新できます":"Update while keeping your mods and settings.",
      _=>null
     };
     if(explanation!=null)Check(((TextBlock)window.FindName("StatusText")).Text==explanation,language+" state explanation");
     if(scenario=="paused")Check(((TextBlock)window.FindName("StatusHeading")).Text==(japanese?"Mod環境は一時無効化中です":"Mod environment temporarily disabled"),language+" pause is explicitly temporary");
     Capture(window,"-"+language);
    }
    ModManager.UiLanguage.Select("auto",persist:false);window.RefreshView();
    if(scenario is "update" or "tool-update"){
     Check(((Button)window.FindName("Primary")).IsVisible&&!((Button)window.FindName("Pause")).IsVisible,"update is main action; pause is not duplicated");
     Check(!((Button)window.FindName("Repair")).IsVisible,"repair remains folded on normal update screen");
     await window.ExecuteAsync("update");Check(!controller.HasUpdate(root),"runtime/tool update completed");
     Check(SetupEngine.HashFile(SetupEngine.SafePath(root,SetupEngine.UnifiedToolPath))==SetupEngine.HashFile(Environment.ProcessPath!),"update copies running downloaded tool");
     Check(!((Button)window.FindName("Primary")).IsVisible&&((Button)window.FindName("Pause")).IsVisible,"updated screen becomes compact installed state");
    }
    if(scenario=="installed")Check(!((Button)window.FindName("Primary")).IsVisible&&!((Button)window.FindName("Repair")).IsVisible,"healthy screen has no redundant check/repair buttons");
    if(scenario=="permission-messages"){
     var show=typeof(UnifiedTools.RecoveryWindow).GetMethod("ShowError",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
     const string sensitive="technical-only-private-path";
     Exception denied;try{throw new UnauthorizedAccessException(sensitive);}catch(Exception error){denied=error;}
     var cancelled=new System.ComponentModel.Win32Exception(1223,"technical-only-cancel-detail");
     foreach(var language in new[]{"en","ja"}){
      ModManager.UiLanguage.Select(language,persist:false);window.RefreshView();
      show.Invoke(window,new object[]{denied,"install"});window.RefreshView();
      var expected=language=="ja"?"このフォルダを変更する権限がありません。続けるには「管理者として再起動」を選んでください。":UnifiedTools.RecoveryWindow.PermissionMessage;
      Check(((TextBlock)window.FindName("LastResult")).Text==expected,language+" permission guidance is friendly and hides technical path");
      Check(((Button)window.FindName("Admin")).IsVisible,language+" permission error offers elevation");
      Capture(window,"-"+language+"-permission");
      show.Invoke(window,new object[]{cancelled,"administrator restart"});window.RefreshView();
      expected=language=="ja"?"管理者としての再起動をキャンセルしました。この画面からいつでもやり直せます。":UnifiedTools.RecoveryWindow.ElevationCancelledMessage;
      Check(((TextBlock)window.FindName("LastResult")).Text==expected,language+" UAC cancellation gives retry guidance");
      Check(window.IsVisible&&((Button)window.FindName("Admin")).IsEnabled,language+" cancellation keeps window and retry available");
      Capture(window,"-"+language+"-cancel");
     }
     var diagnostic=window.DiagnosticReport();
     Check(diagnostic.Contains("UnauthorizedAccessException")&&diagnostic.Contains(sensitive)&&diagnostic.Contains("Program.cs"),"report retains original permission type, message and stack trace");
     Check(diagnostic.Contains("Win32Exception")&&diagnostic.Contains("NativeErrorCode: 1223")&&diagnostic.Contains("technical-only-cancel-detail"),"report retains UAC native code and original detail");
     Check(diagnostic.Contains("Operation: install")&&diagnostic.Contains("Operation: administrator restart"),"report retains both operations rather than replacing prior error");
     Check(before==Snapshot(),"friendly messages and in-memory technical history do not write files");
     ModManager.UiLanguage.Select("en",persist:false);window.RefreshView();
     Check(((TextBlock)window.FindName("LastResult")).Text==UnifiedTools.RecoveryWindow.ElevationCancelledMessage,"existing guidance follows language switching");
    }
    if(scenario=="safety"){
     var sheet=new StackPanel{Background=System.Windows.Media.Brushes.White,Margin=new Thickness(12)};
     foreach(var kind in Enum.GetValues<UnifiedTools.StatusSymbolKind>()){
      var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,5)};
      row.Children.Add(new TextBlock{Text=kind.ToString(),Width=100,VerticalAlignment=VerticalAlignment.Center});
      foreach(var size in new[]{16d,24d,32d,48d})row.Children.Add(new UnifiedTools.StatusSymbol{Kind=kind,Width=size,Height=size,Margin=new Thickness(10,0,10,0)});
      sheet.Children.Add(row);
     }
     sheet.Measure(new Size(400,450));sheet.Arrange(new Rect(0,0,400,450));sheet.UpdateLayout();
     var raster=new System.Windows.Media.Imaging.RenderTargetBitmap(400,450,96,96,System.Windows.Media.PixelFormats.Pbgra32);raster.Render(sheet);
     var iconSheet=new System.Windows.Media.Imaging.PngBitmapEncoder();iconSheet.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(raster));
     using(var output=File.Create("UnifiedTools/validation/status-symbols.png"))iconSheet.Save(output);
     Check(SetupEngine.Version==UnifiedTools.App.ToolVersion,"setup and UI versions agree");
     Check(((TextBlock)window.FindName("VersionText")).Text.StartsWith(UnifiedTools.App.ToolVersion)&&typeof(UnifiedTools.App).Assembly.GetName().Version!.ToString(3)==UnifiedTools.App.ToolVersion,"visible footer and exe assembly use release version");
     Check(SetupEngine.BridgePath=="BepInEx/patchers/TailsideWorkshopModLoader.dll","loader is directly under patchers");
     Check(SetupEngine.UnifiedToolPath.EndsWith("/TailsideModSetup.exe"),"new executable name");
     var url=SupportInfo.GuideUrl;try{
      SupportInfo.GuideUrl="";Exception? helpError=null;
      ModTools.Desktop.HelpSupport.OpenGuide(e=>helpError=e);
      Check(helpError!=null&&helpError.Message.Contains("not been configured"),"unset help URL gives local guidance without browser launch");
      SupportInfo.GuideUrl="https://example.com/releases";
      Check(SupportInfo.HasGuideUrl,"later HTTPS release URL is recognized");
     }finally{SupportInfo.GuideUrl=url;}
     var tools=ToolStorage.DirectoryPath;var logs=Path.Combine(tools,"logs");
     File.WriteAllText(logs,"blocking file");bool unavailable=false;
     Check(ModTools.Desktop.HelpSupport.ReportDirectory(true,_=>unavailable=true)==Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)&&unavailable,"unwritable report directory falls back without closing app");
     File.Delete(logs);
     void Junction(string link,string target){
      Directory.CreateDirectory(target);var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
      foreach(var arg in new[]{"/c","mklink","/J",link,target})info.ArgumentList.Add(arg);
      using var process=System.Diagnostics.Process.Start(info)!;process.WaitForExit();if(process.ExitCode!=0)throw new IOException("Cannot create test junction: "+process.StandardError.ReadToEnd());
     }
     var outside=Path.Combine(root,"outside-tools");
     var data=Path.Combine(tools,"data");var saved=data+"-saved";Directory.Move(data,saved);
     try{
      Junction(data,outside);bool rejected=false;try{ModManager.UiLanguage.Select("en",persist:true);}catch(InvalidDataException){rejected=true;}
      Check(rejected&&!File.Exists(Path.Combine(outside,"settings.json")),"linked settings directory cannot redirect a write outside tool folder");
     }finally{if(Directory.Exists(data))Directory.Delete(data);Directory.Move(saved,data);}
     try{
      Junction(logs,outside);unavailable=false;
      Check(ModTools.Desktop.HelpSupport.ReportDirectory(true,_=>unavailable=true)==Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)&&unavailable,"linked report directory is rejected with fallback");
      var save=typeof(ModTools.Desktop.HelpSupport).GetMethod("SaveEmergencyReport",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
      Check(save.Invoke(null,new object[]{"test","report"})==null&&!Directory.EnumerateFileSystemEntries(outside).Any(),"emergency report does not follow linked logs directory");
     }finally{if(Directory.Exists(logs))Directory.Delete(logs);}
     Check(!ModTools.Desktop.HelpSupport.FatalErrorActive&&window.IsVisible,"save failures leave recovery UI usable");
    }
    if(manual){
     if(window.CurrentView?.Ownership==Ownership.Manual)Check(!((Button)window.FindName("Primary")).IsVisible&&!((Button)window.FindName("Primary")).IsEnabled,"manual install has no redundant Check again action");
     var recovery=(Expander)window.FindName("DetailsPanel");
     Check(!recovery.IsExpanded,"manual guidance starts collapsed");
     recovery.IsExpanded=false;window.RefreshView();
     Check(!recovery.IsExpanded,"manual guidance keeps user collapse on explicit refresh");
     await Task.Delay(3400);
     Check(!recovery.IsExpanded,"three-second automatic refresh keeps user collapse");
     recovery.IsExpanded=true;window.RefreshView();window.UpdateLayout();
     Check(recovery.IsExpanded,"user can reopen manual guidance");
     var body=(ScrollViewer)window.FindName("BodyScroll");
     Check(body.ScrollableHeight>0,"expanded recovery instructions have a scrollable viewport");
     body.ScrollToEnd();window.UpdateLayout();
     Check(body.VerticalOffset>0,"recovery instructions can scroll to their end");
     var help=(FrameworkElement)window.FindName("Guide");
     Check(help.TransformToAncestor(window).TransformBounds(new Rect(help.RenderSize)).Bottom<=window.ActualHeight,"help stays visible while recovery content scrolls");
     Check(window.CurrentView?.Screen==StartScreen.Manual,"manual guidance selected");Check(((FrameworkElement)window.FindName("ManualPanel")).IsVisible,"manual instructions visible");Check(!((FrameworkElement)window.FindName("OwnedActions")).IsVisible,"mutation controls hidden");
     ModManager.UiLanguage.Select("auto",persist:false);window.RefreshView();
     foreach(var action in new[]{"install","update","repair","pause","resume","remove","tool"})await window.ExecuteAsync(action);
     Check(before==Snapshot(),"all bypassed mutation requests and session language change leave files unchanged");
    }else if(scenario=="invalid-folder"){
     Check(!((Button)window.FindName("Primary")).IsEnabled,"unrelated folder disables Install button");
     await window.ExecuteAsync("install");
     Check(before==Snapshot(),"bypassed Install still creates no files in unrelated folder");
    }else if(scenario is "fresh" or "fresh-localized"){
     Check(((Button)window.FindName("Primary")).IsEnabled,"fresh installation enabled");await window.ExecuteAsync("install");
     Check(window.CurrentView?.Ownership==Ownership.Owned,"install records ownership");Check(File.Exists(SetupEngine.SafePath(root,SetupEngine.UnifiedToolPath)),"explicit install keeps tool copy");Capture(window,"-after-install");
    }else{
     Check(window.CurrentView?.Ownership==Ownership.Owned,"owned recovery actions selected");
     Check(((Button)window.FindName("Remove")).IsEnabled,"owned removal available");
     if(scenario.StartsWith("runtime")){Check(!((Button)window.FindName("Repair")).IsEnabled,"unsupported runtime repair disabled");if(scenario=="runtime-paused")Check(!((Button)window.FindName("Pause")).IsEnabled,"unsupported runtime re-enable disabled");}
     if(scenario=="installed"){
      await window.ExecuteAsync("pause");Check(window.CurrentView?.Runtime=="Paused","disable works");await window.ExecuteAsync("resume");Check(window.CurrentView?.Runtime=="Enabled","re-enable works");
      var custom=SetupEngine.SafePath(root,"BepInEx/plugins/Keep.dll");Directory.CreateDirectory(Path.GetDirectoryName(custom)!);File.WriteAllText(custom,"keep");
      Check(window.RemovalSummary().Contains(SetupEngine.BridgePath)&&!window.RemovalSummary().Contains("Keep.dll"),"removal preview lists owned files only");
      await window.ExecuteAsync("remove");Check(!File.Exists(Path.Combine(root,"winhttp.dll"))&&File.ReadAllText(custom)=="keep","removal keeps user mod and removes owned injector");
      Check(((TextBlock)window.FindName("LastResult")).Text.Contains("TailsideModSetup"),"removal explains manual tool-folder cleanup");
     }
     if(scenario=="partial-remove"){
      await window.ExecuteAsync("remove");
      Check(File.Exists(SetupEngine.SafePath(root,SetupEngine.BridgePath)),"changed loader preserved on removal");
      Check(!((TextBlock)window.FindName("LastResult")).Text.Contains(UnifiedTools.RecoveryWindow.CleanupMessage),"partial removal does not suggest deleting tool folder");
     }
     if(scenario=="changed"||scenario=="broken"){await window.ExecuteAsync("repair");Check(controller.Setup.Inspect(root).State==SetupState.Installed,"repair restores owned runtime");}
     if(ModManager.UiLanguage.Options.Any(x=>x.Code=="ja")){ModManager.UiLanguage.Select("ja",persist:window.CanSaveAutomatically);window.RefreshView();Check(((TextBlock)window.FindName("PageTitle")).Text=="Mod環境の導入・復旧","Japanese labels available");}
    }
    Console.WriteLine($"UI SCENARIO PASSED {scenario} ({checks} checks)");app.Shutdown(0);
   }catch(Exception error){Console.Error.WriteLine(error);app.Shutdown(1);}
  };
  timer.Start();return app.Run();
 }
}










