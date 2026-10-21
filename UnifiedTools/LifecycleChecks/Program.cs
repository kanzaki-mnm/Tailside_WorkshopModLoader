using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Automation;
using ModEnvironmentSetup.Core;

internal static class Program
{
 static int checks;
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
 static T Wait<T>(Func<T?> read,string label) where T:class
 {
  var timer=Stopwatch.StartNew();
  while(timer.Elapsed<TimeSpan.FromSeconds(45)){
   try{var value=read();if(value!=null)return value;}catch(ElementNotAvailableException){}catch(InvalidOperationException){}
   Thread.Sleep(150);
  }
  throw new TimeoutException(label);
 }
 static AutomationElement Window(Process p)=>Wait(()=>{
  p.Refresh();return p.MainWindowHandle==IntPtr.Zero?null:AutomationElement.FromHandle(p.MainWindowHandle);
 },"Window for process "+p.Id);
 static AutomationElement? Find(AutomationElement window,string id)=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
 static void Invoke(AutomationElement element)=>((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
 static void Ready(AutomationElement window,string id)=>Wait(()=>{var e=Find(window,id);return e?.Current.IsEnabled==true?e:null;},"Ready "+id);
 static Process Open(string exe,string root){var info=new ProcessStartInfo(exe){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};info.ArgumentList.Add("--game-dir");info.ArgumentList.Add(root);return Process.Start(info)??throw new IOException("No test process");}
 static void Close(Process p){if(p.HasExited)return;var w=Window(p);((WindowPattern)w.GetCurrentPattern(WindowPattern.Pattern)).Close();if(!p.WaitForExit(10000))throw new IOException("Owned test window did not close");}
 static void ConfirmRemoval(Process p)
 {
  var names=new[]{"Remove verified files","確認済みファイルを撤去"};
  var button=Wait(()=>{
   var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,p.Id));
   foreach(AutomationElement w in windows)foreach(var name in names){var b=w.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,name)));if(b!=null)return b;}
   return null;
  },"Removal confirmation");Invoke(button);
 }
 [STAThread] static int Main(string[] args)
 {
  Process? app=null;
  try{
   if(args.Length!=4||args[0] is not ("install" or "update" or "remove" or "pause"))throw new ArgumentException("install|update|remove|pause published-exe marked-game-copy new-work-dir");
   var root=Path.GetFullPath(args[2]);var work=Path.GetFullPath(args[3]);var published=Path.GetFullPath(args[1]);
   if(root.Contains("steamapps",StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(root,".modsetup-sandbox")))throw new IOException("Marked isolated game copy only");
   var tool=SetupEngine.SafePath(root,SetupEngine.UnifiedToolPath);var receipt=SetupEngine.SafePath(root,SetupEngine.ReceiptPath);
   if(args[0]=="install"){
    if(Directory.Exists(work))throw new IOException("New work directory required");Directory.CreateDirectory(work);
    var download=Path.Combine(work,"First download.exe");File.Copy(published,download);
    app=Open(download,root);var w=Window(app);Ready(w,"Primary");Invoke(Find(w,"Primary")??throw new Exception("Install button missing"));
    Ready(w,"Remove");Wait(()=>File.Exists(tool)&&File.Exists(receipt)?"installed":null,"Copied exe and receipt");
    Check(Hash(tool)==Hash(published),"actual published exe copied into TailsideModSetup");
    Check(!Directory.Exists(SetupEngine.SafePath(root,"BepInEx/ModEnvironmentSetup")),"no retired tools directory created");
    Close(app);app.Dispose();app=null;File.Delete(download);Check(!File.Exists(download),"original downloaded exe removed after install");
    app=Open(tool,root);w=Window(app);Ready(w,"Remove");Check(Find(w,"Remove")?.Current.IsEnabled==true,"game-local exe reopens owned recovery UI");Close(app);app.Dispose();app=null;
   }else if(args[0]=="pause"){
    app=Open(published,root);var w=Window(app);Ready(w,"Pause");Invoke(Find(w,"Pause")!);
    var yes=Wait(()=>{
     var dialogs=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,app.Id));
     foreach(AutomationElement dialog in dialogs){var button=dialog.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.AutomationIdProperty,"6")));if(button!=null)return button;}
     return null;
    },"Pause confirmation Yes button");Invoke(yes);
    Wait(()=>!File.Exists(Path.Combine(root,"winhttp.dll"))&&File.Exists(Path.Combine(root,"winhttp.dll.modmanager-disabled"))?"paused":null,"Whole BepInEx disabled");
    Check(File.Exists(receipt),"pause retains ownership record");
    Check(File.Exists(SetupEngine.SafePath(root,SetupEngine.BridgePath)),"pause retains loader files without loading them");
    Close(app);app.Dispose();app=null;
   }else if(args[0]=="update"){
    if(!Directory.Exists(work)||!File.Exists(receipt))throw new IOException("Install phase required");
    var runtime=System.Text.Json.JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receipt))!.Files.ToDictionary(x=>SetupEngine.SafePath(root,x.Path),x=>Hash(SetupEngine.SafePath(root,x.Path)));
    var settings=SetupEngine.SafePath(root,SetupEngine.DataPath+"/settings.json");File.WriteAllText(settings,"{\"language\":\"en\"}");var settingsHash=Hash(settings);
    // An older same-format installation fixture; not a released-version migration claim.
    File.WriteAllText(tool,"older tool fixture");
    File.WriteAllText(SetupEngine.SafePath(root,SetupEngine.ToolReceiptPath),System.Text.Json.JsonSerializer.Serialize(new{hash=Hash(tool),version="0.9.0"}));
    var download=Path.Combine(work,"Update download.exe");File.Copy(published,download);
    app=Open(download,root);var w=Window(app);Ready(w,"Primary");
    Check(Find(w,"Primary")?.Current.Name=="Update","real exe offers tool-only update");Invoke(Find(w,"Primary")!);
    Wait(()=>Hash(tool)==Hash(published)?"updated":null,"Updated game-local exe");Ready(w,"Remove");
    Check(runtime.All(x=>File.Exists(x.Key)&&Hash(x.Key)==x.Value),"tool-only update leaves runtime files unchanged");
    Check(Hash(settings)==settingsHash,"update preserves language setting");
    Check(File.ReadAllText(SetupEngine.SafePath(root,SetupEngine.ToolReceiptPath)).Contains("1.0.0"),"update records current tool version");
    Close(app);app.Dispose();app=null;app=Open(tool,root);w=Window(app);Ready(w,"Remove");
    Check(Find(w,"Pause")?.Current.IsEnabled==true,"updated retained exe opens installed screen");Close(app);app.Dispose();app=null;
   }else{
    if(!Directory.Exists(work)||!File.Exists(receipt))throw new IOException("Install phase required");
    var plugins=Path.Combine(root,"BepInEx/plugins");var kept=Directory.GetFiles(plugins,"*",SearchOption.AllDirectories).ToDictionary(x=>x,Hash);
    var config=Path.Combine(root,"BepInEx/config/BepInEx.cfg");var configHash=Hash(config);
    var toolHash=Hash(tool);var licenses=Directory.GetFiles(SetupEngine.SafePath(root,SetupEngine.ToolsPath+"/licenses")).ToDictionary(x=>x,Hash);
    var download=Path.Combine(work,"Second download.exe");File.Copy(published,download);
    app=Open(download,root);var w=Window(app);Ready(w,"Remove");Check(!File.Exists(Path.Combine(work,"First download.exe")),"redownload does not depend on first exe");Invoke(Find(w,"Remove")??throw new Exception("Remove button missing"));ConfirmRemoval(app);
    Wait(()=>!File.Exists(receipt)&&Find(w,"Primary")?.Current.IsEnabled==true?"removed":null,"Removal finished");
    var result=Find(w,"LastResult")?.Current.Name??"";
    Check(result.Contains("BepInEx/TailsideModSetup")&&(result.Contains("may delete")||result.Contains("削除できます")),"actual UI explains close-then-delete tool folder");
    Check(!File.Exists(Path.Combine(root,"winhttp.dll"))&&!File.Exists(SetupEngine.SafePath(root,SetupEngine.BridgePath)),"redownloaded exe removes owned runtime");
    Check(kept.Count>0&&kept.All(x=>File.Exists(x.Key)&&Hash(x.Key)==x.Value)&&Hash(config)==configHash,"manual plugins and BepInEx config preserved");
    Check(File.Exists(tool)&&Hash(tool)==toolHash,"tool exe is retained and never self-deletes");
    Check(licenses.Count>0&&licenses.All(x=>File.Exists(x.Key)&&Hash(x.Key)==x.Value),"third-party notices remain with retained tool");Close(app);app.Dispose();app=null;
   }
   Console.WriteLine($"PASS lifecycle {args[0]}: {checks} checks");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{if(app!=null){try{if(!app.HasExited){app.Kill();app.WaitForExit(10000);}}finally{app.Dispose();}}}
 }
}

