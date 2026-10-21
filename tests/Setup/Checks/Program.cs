using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Serialization;
using ModEnvironmentSetup.Core;
using ModManager.Core;
using WorkshopBridge;
using UnifiedTools.Core;
using var zip=File.OpenRead("TailsideModSetup/Assets/runtime.zip");var payload=SetupEngine.LoadPayload(zip);
var temp=Path.Combine(Path.GetTempPath(),"UnifiedTools-check-"+Guid.NewGuid());Directory.CreateDirectory(temp);int count=0;
void Check(bool value,string label){if(!value)throw new Exception("FAIL "+label);Console.WriteLine("PASS "+label);count++;}
void Reject(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception("FAIL accepted "+label);}
string Game(string name){var r=Path.Combine(temp,name);Directory.CreateDirectory(Path.Combine(r,"Tailside_Data/Managed"));var b=new byte[256];BitConverter.GetBytes((ushort)0x5a4d).CopyTo(b,0);BitConverter.GetBytes(128).CopyTo(b,60);BitConverter.GetBytes(0x4550u).CopyTo(b,128);BitConverter.GetBytes((ushort)0x8664).CopyTo(b,132);File.WriteAllBytes(Path.Combine(r,"Tailside.exe"),b);return r;}
var c=new EnvironmentController(payload,()=>false);
if(args.Length>0&&args[0]=="prepare") {foreach(var name in new[]{"fresh","installed","paused","broken","receipt","runtime","runtime-paused"}){var r=Game(name);if(name!="fresh")c.Setup.Install(r);if(name is "paused" or "runtime-paused")c.Manager.PauseEnvironment(r);if(name.StartsWith("runtime"))Directory.Delete(Path.Combine(r,"Tailside_Data/Managed"));if(name=="broken")File.Delete(SetupEngine.SafePath(r,"BepInEx/core/BepInEx.dll"));if(name=="receipt")File.WriteAllText(SetupEngine.SafePath(r,SetupEngine.ReceiptPath),"broken");}Console.WriteLine(temp);return;}
var root=Game("main");
Check(!payload.Any(x=>x.Path.EndsWith(".exe")),"runtime package contains no executable or recursive tool");
using(var pe=new PEReader(new MemoryStream(payload.Single(x=>x.Path==SetupEngine.BridgePath).Bytes)))
 Check(pe.GetMetadataReader().GetAssemblyDefinition().Version.ToString(3)==SetupEngine.Version,"embedded loader release matches setup release");
Check(c.Inspect(root).Screen==StartScreen.Setup,"fresh routes to setup");c.Setup.Install(root);
Check(c.Inspect(root).Screen==StartScreen.Manager,"installed routes to manager");
Check(payload.Any(x=>x.Path==SetupEngine.BridgePath)&&!payload.Any(x=>x.Path==SetupEngine.LegacyBridgePath),"payload includes current loader only");
Reject(()=>c.Manager.SaveControl(root,new ModControl()),"current loader rejects obsolete individual control writes");
Check(c.Manager.Inventory(root,new List<string>()).Count==0,"current loader does not use legacy package inventory");
var receipt=SetupEngine.SafePath(root,SetupEngine.ReceiptPath);var before=File.ReadAllBytes(receipt);c.Inspect(root);Check(before.SequenceEqual(File.ReadAllBytes(receipt)),"startup does not reinstall or rewrite receipt");
Directory.CreateDirectory(SetupEngine.SafePath(root,"BepInEx/config"));File.WriteAllText(SetupEngine.SafePath(root,ManagerEngine.ControlPath),"{\"schemaVersion\":1,\"pauseAll\":true,\"disabledIds\":[\"example.mod\"]}");var control=File.ReadAllBytes(SetupEngine.SafePath(root,ManagerEngine.ControlPath));
c.Manager.PauseEnvironment(root);Check(c.Inspect(root).Screen==StartScreen.Recovery&&c.Inspect(root).Runtime=="Paused","paused routes to recovery");
Check(new EnvironmentController(payload,()=>false).Inspect(root).Runtime=="Paused","pause survives tool restart");c.Manager.ResumeEnvironment(root);Check(c.Inspect(root).Screen==StartScreen.Manager,"restore returns manager");
var bridge=SetupEngine.SafePath(root,SetupEngine.BridgePath);File.Delete(bridge);Check(c.Inspect(root).Screen==StartScreen.Recovery,"missing bridge routes to recovery");c.Setup.Install(root);Check(c.Inspect(root).Screen==StartScreen.Manager,"missing bridge repaired");
File.WriteAllText(bridge,"changed owned file");Check(c.Inspect(root).Screen==StartScreen.Recovery,"changed file routes to recovery");Reject(()=>c.Setup.Install(root),"ordinary setup refuses modified file");c.Setup.RepairOwned(root);Check(c.Inspect(root).Screen==StartScreen.Manager,"explicit owned repair restores runtime");Check(Directory.EnumerateFiles(SetupEngine.SafePath(root,"BepInEx/TailsideModSetup/data/repair-backups"),Path.GetFileName(SetupEngine.BridgePath),SearchOption.AllDirectories).Any(x=>File.ReadAllText(x)=="changed owned file"),"changed owned bytes backed up");
Check(control.SequenceEqual(File.ReadAllBytes(SetupEngine.SafePath(root,ManagerEngine.ControlPath))),"legacy settings left untouched across repair");
var custom=SetupEngine.SafePath(root,"BepInEx/plugins/Custom.dll");Directory.CreateDirectory(Path.GetDirectoryName(custom)!);File.WriteAllText(custom,"user mod");var tool=SetupEngine.SafePath(root,SetupEngine.UnifiedToolPath);File.WriteAllText(tool,"test tool");
c.Manager.PauseEnvironment(root);Check(c.Remove(root).Count==0,"paused environment uninstalled");Check(c.Inspect(root).Screen==StartScreen.Manual,"remaining unowned mods route to manual guidance");Check(File.Exists(tool)&&File.ReadAllText(custom)=="user mod"&&control.SequenceEqual(File.ReadAllBytes(SetupEngine.SafePath(root,ManagerEngine.ControlPath))),"tool mods and settings survive removal");
File.Move(custom,Path.Combine(temp,"kept-user-mod.dll"));c.Setup.Install(root);File.WriteAllText(receipt,"broken json");Check(c.Inspect(root).Screen==StartScreen.Manual,"unreadable receipt routes to non-mutating guidance");Reject(()=>c.Setup.RepairOwned(root),"unreadable receipt blocks blind repair");Reject(()=>c.Remove(root),"unreadable receipt blocks blind deletion");Reject(()=>c.Manager.PauseEnvironment(root),"unreadable receipt blocks automatic disable");
var legacy=Game("legacy");var legacyBridge=SetupEngine.SafePath(legacy,SetupEngine.LegacyBridgePath);Directory.CreateDirectory(Path.GetDirectoryName(legacyBridge)!);File.WriteAllText(legacyBridge,"keep old loader");Check(c.Inspect(legacy).Installation.State==SetupState.Blocked,"legacy loader blocks parallel installation");Reject(()=>c.Setup.Install(legacy),"legacy loader not automatically replaced");Check(File.ReadAllText(legacyBridge)=="keep old loader","legacy loader bytes preserved");
var conflict=Game("conflict");File.WriteAllText(Path.Combine(conflict,"winhttp.dll"),"unknown");Reject(()=>c.Setup.Install(conflict),"unknown loader not overwritten");Check(File.ReadAllText(Path.Combine(conflict,"winhttp.dll"))=="unknown","unknown loader bytes preserved");
var broken=Game("broken-core");c.Setup.Install(broken);File.Delete(SetupEngine.SafePath(broken,"BepInEx/core/BepInEx.dll"));Check(c.Inspect(broken).Screen==StartScreen.Recovery,"missing BepInEx core routes recovery");c.Manager.PauseEnvironment(broken);Check(c.Manager.EnvironmentState(broken)=="Paused","disable works without BepInEx core");c.Manager.ResumeEnvironment(broken);c.Setup.Install(broken);Check(c.Inspect(broken).Screen==StartScreen.Manager,"core repaired");
Directory.Delete(Path.Combine(broken,"Tailside_Data/Managed"));Check(c.Inspect(broken).Screen==StartScreen.Recovery,"future runtime change routes recovery");c.Manager.PauseEnvironment(broken);Check(c.Manager.EnvironmentState(broken)=="Paused","known injector disable independent of Mono runtime");
Reject(()=>c.Manager.ResumeEnvironment(broken),"unsupported runtime cannot reactivate old Mono loader");
Check(c.Setup.CanRemoveForRecovery(broken),"receipt allows removal after Mono disappears");Reject(()=>c.Setup.Install(broken),"unsupported runtime still blocks installation");Reject(()=>c.Setup.RepairOwned(broken),"unsupported runtime still blocks repair");
var runningRemoval=new EnvironmentController(payload,()=>true);Reject(()=>runningRemoval.Remove(broken),"running game blocks runtime-independent removal");
Check(c.Remove(broken).Count==0&&!File.Exists(Path.Combine(broken,ManagerEngine.ParkedPath)),"paused Mono environment removed after runtime changes");
var noExe=Game("game-removed");c.Setup.Install(noExe);File.Delete(Path.Combine(noExe,"Tailside.exe"));Check(c.Remove(noExe).Count==0,"owned environment removable even after game exe disappears");
var foreign=Game("foreign-receipt");c.Setup.Install(foreign);var foreignReceipt=SetupEngine.SafePath(foreign,SetupEngine.ReceiptPath);var bad=JsonSerializer.Deserialize<Receipt>(File.ReadAllText(foreignReceipt))!;bad.GameRoot=temp;File.WriteAllText(foreignReceipt,JsonSerializer.Serialize(bad));Check(!c.Setup.CanRemoveForRecovery(foreign),"foreign ownership record cannot enable recovery removal");Reject(()=>c.Remove(foreign),"foreign ownership removal refused");
var running=new EnvironmentController(payload,()=>true);Reject(()=>running.Setup.Install(Game("running")),"running game blocks setup");Reject(()=>running.Manager.ResumeEnvironment(broken),"running game blocks resume");
var modified=Game("modified-remove");c.Setup.Install(modified);File.WriteAllText(SetupEngine.SafePath(modified,SetupEngine.BridgePath),"keep");var kept=c.Remove(modified);Check(kept.Contains(SetupEngine.BridgePath)&&File.ReadAllText(SetupEngine.SafePath(modified,SetupEngine.BridgePath))=="keep","recovery removal preserves changed files and reports them");Check(!File.Exists(Path.Combine(modified,"winhttp.dll")),"recovery removal removes verified injector");
var deployment=Game("tool-copy");c.Setup.Install(deployment);var source=Path.Combine(temp,"tool-source.exe");File.WriteAllText(source,"tool v1");c.KeepTool(deployment,source);var installedTool=SetupEngine.SafePath(deployment,SetupEngine.UnifiedToolPath);Check(File.ReadAllText(installedTool)=="tool v1","single exe copied into game-local tools");c.KeepTool(deployment,source);Check(File.ReadAllText(installedTool)=="tool v1","same version tool copy is idempotent");File.WriteAllText(source,"tool v2");c.KeepTool(deployment,source);Check(File.ReadAllText(installedTool)=="tool v2","recorded tool update succeeds");File.WriteAllText(installedTool,"user changed tool");Reject(()=>c.KeepTool(deployment,source),"unknown changed tool not overwritten");Check(File.ReadAllText(installedTool)=="user changed tool","changed tool bytes retained");
string Snapshot(string game)=>string.Join("\n",Directory.EnumerateFileSystemEntries(game,"*",SearchOption.AllDirectories).Order().Select(path=>Path.GetRelativePath(game,path)+(File.Exists(path)?":"+SetupEngine.HashFile(path):"/")));
foreach(var kind in new[]{"complete","partial","corrupt","foreign","no-mono","legacy-only"}){
 var manual=Game("manual-"+kind);
 if(kind is "complete" or "corrupt" or "foreign" or "no-mono"){
  foreach(var file in payload){var path=SetupEngine.SafePath(manual,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,file.Bytes);}
 }
 if(kind=="partial")File.WriteAllText(Path.Combine(manual,"winhttp.dll"),"unknown partial installation");
 if(kind=="legacy-only"){var path=SetupEngine.SafePath(manual,SetupEngine.LegacyBridgePath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"legacy");}
 if(kind is "corrupt" or "foreign"){Directory.CreateDirectory(SetupEngine.SafePath(manual,SetupEngine.DataPath));File.WriteAllText(SetupEngine.SafePath(manual,SetupEngine.ReceiptPath),kind=="corrupt"?"broken":JsonSerializer.Serialize(new Receipt{GameRoot=temp,Files=[]}));}
 if(kind=="no-mono")Directory.Delete(Path.Combine(manual,"Tailside_Data/Managed"));
 var original=Snapshot(manual);
 Check(c.Inspect(manual).Screen==StartScreen.Manual,kind+" unowned environment routes to manual guidance");
 Reject(()=>c.Setup.Install(manual),kind+" rejects automatic install");
 Reject(()=>c.Setup.RepairOwned(manual),kind+" rejects automatic repair");
 Reject(()=>c.Remove(manual),kind+" rejects automatic removal");
 Reject(()=>c.Manager.PauseEnvironment(manual),kind+" rejects automatic disable");
 Reject(()=>c.Manager.ResumeEnvironment(manual),kind+" rejects automatic re-enable");
 Reject(()=>c.KeepTool(manual,source),kind+" rejects tool copy");
 Check(original==Snapshot(manual),kind+" inspection and rejected operations leave all files and directories unchanged");
}
var coexist=Game("owned-legacy-conflict");c.Setup.Install(coexist);var legacyPath=SetupEngine.SafePath(coexist,SetupEngine.LegacyBridgePath);Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);File.WriteAllText(legacyPath,"keep legacy");var coexistBefore=Snapshot(coexist);
Check(!c.Setup.CanRepairOwned(coexist),"owned legacy conflict disables repair");Reject(()=>c.Setup.RepairOwned(coexist),"repair cannot introduce current loader alongside legacy");Check(coexistBefore==Snapshot(coexist),"rejected legacy repair leaves all files unchanged");
// Exercise the legacy JSON boundary with actual deserialization, not nullable suppression.
void RejectAs<TException>(Action action,string label) where TException:Exception
{
 try{action();}catch(TException){Check(true,label);return;}
 throw new Exception("FAIL accepted "+label);
}
var jsonRoot=Path.Combine(temp,"nullable-json");Directory.CreateDirectory(Path.Combine(jsonRoot,"plugins"));
var manifestFile=Path.Combine(jsonRoot,"mod.json");
const string validManifest="""{"schemaVersion":1,"id":"example.probe","name":"Probe","version":"1.0.0","gameAppId":2922570,"entryAssembly":"plugins/probe.dll","minimumBridgeVersion":"0.2.0"}""";
File.WriteAllText(Path.Combine(jsonRoot,"plugins/probe.dll"),"not executed; metadata fixture only");
File.WriteAllText(manifestFile,validManifest);
var parsed=Catalog.ReadPackage(jsonRoot,"test",42);
Check(parsed is not null&&parsed.Manifest.Id=="example.probe"&&parsed.Manifest.Name=="Probe"&&parsed.Manifest.Version=="1.0.0"&&parsed.WorkshopId==42,"valid legacy manifest still creates complete package");
Check(Catalog.ReadPackage(Path.Combine(temp,"no-manifest"),"test") is null,"missing manifest is explicitly no package");
File.WriteAllText(manifestFile,"null");
RejectAs<InvalidDataException>(()=>Catalog.ReadPackage(jsonRoot,"test"),"JSON null manifest rejected as invalid data");
foreach(var member in new[]{"id","name","version","entryAssembly","minimumBridgeVersion"})
{
 var document=(JsonNode.Parse(validManifest)??throw new Exception("Invalid test fixture")).AsObject();document[member]=null;
 var text=document.ToJsonString();File.WriteAllText(manifestFile,text);
 RejectAs<InvalidDataException>(()=>Catalog.ReadPackage(jsonRoot,"test"),"null manifest "+member+" rejected");
 Check(File.ReadAllText(manifestFile)==text,"invalid "+member+" manifest left unchanged");
 document.Remove(member);File.WriteAllText(manifestFile,document.ToJsonString());
 RejectAs<SerializationException>(()=>Catalog.ReadPackage(jsonRoot,"test"),"missing required "+member+" rejected");
}
File.WriteAllText(manifestFile,"{");
RejectAs<SerializationException>(()=>Catalog.ReadPackage(jsonRoot,"test"),"malformed manifest rejected without null dereference");
File.WriteAllText(manifestFile,validManifest);
var roundtrip=Path.Combine(jsonRoot,"roundtrip.json");Catalog.WriteJson(roundtrip,Catalog.ReadJson<Manifest>(manifestFile));
using(var json=JsonDocument.Parse(File.ReadAllText(roundtrip)))
using(var original=JsonDocument.Parse(validManifest))
 Check(json.RootElement.EnumerateObject().Select(x=>x.Name).Order().SequenceEqual(original.RootElement.EnumerateObject().Select(x=>x.Name).Order()),"manifest roundtrip preserves required JSON member names");
Check(Catalog.ReadJson<Manifest>(roundtrip).Entry=="plugins/probe.dll","manifest roundtrip preserves required values");
var controlFile=Path.Combine(jsonRoot,"control.json");
foreach(var invalid in new[]{"null","{\"schemaVersion\":1,\"pauseAll\":false,\"disabledIds\":null}","{\"schemaVersion\":1,\"pauseAll\":false,\"disabledIds\":[null]}"})
{
 File.WriteAllText(controlFile,invalid);
 RejectAs<InvalidDataException>(()=>ModControl.Read(controlFile),"null control root/collection/entry rejected");
 Check(File.ReadAllText(controlFile)==invalid,"invalid control settings left unchanged");
}
var previousDirectory=Directory.GetCurrentDirectory();
try
{
 Directory.SetCurrentDirectory(jsonRoot);
 Catalog.WriteJson("relative-settings.json",new ModControl{PauseAll=true,DisabledIds=["example.probe"]});
 var saved=ModControl.Read("relative-settings.json");
 Check(saved.PauseAll&&saved.DisabledIds.SequenceEqual(new[]{"example.probe"}),"relative output filename resolves parent and preserves settings");
 RejectAs<ArgumentNullException>(()=>Catalog.WriteJson<ModControl?>("must-not-exist.json",null),"null JSON write rejected before creating file");
 Check(!File.Exists("must-not-exist.json"),"rejected null write creates no file");
 RejectAs<InvalidDataException>(()=>Catalog.WriteJson(Path.GetPathRoot(jsonRoot)??throw new Exception("Missing test root"),new ModControl()),"filesystem root rejected as JSON output file");
}finally{Directory.SetCurrentDirectory(previousDirectory);}
var lifecycle=Game("redownload-lifecycle");c.Setup.Install(lifecycle);
var firstDownload=Path.Combine(temp,"first-download.exe");File.WriteAllText(firstDownload,"simulated tool bytes");c.KeepTool(lifecycle,firstDownload);File.Delete(firstDownload);
Check(!Directory.Exists(SetupEngine.SafePath(lifecycle,"BepInEx/ModEnvironmentSetup")),"setup creates no retired tool directory");
Check(File.Exists(SetupEngine.SafePath(lifecycle,SetupEngine.ReceiptPath))&&File.Exists(SetupEngine.SafePath(lifecycle,SetupEngine.ToolReceiptPath)),"installation and tool records are under TailsideModSetup/data");
var licenses=Directory.GetFiles(SetupEngine.SafePath(lifecycle,SetupEngine.ToolsPath+"/licenses"));var licenseHashes=licenses.ToDictionary(x=>x,SetupEngine.HashFile);
var redownloaded=new EnvironmentController(payload,()=>false);
Check(redownloaded.OwnershipOf(lifecycle)==Ownership.Owned,"new controller recognizes ownership after original download is deleted");
Check(redownloaded.Remove(lifecycle).Count==0,"fresh tool instance removes owned environment");
Check(File.Exists(SetupEngine.SafePath(lifecycle,SetupEngine.UnifiedToolPath)),"removal leaves game-local exe for manual folder cleanup");
Check(licenses.Length>0&&licenseHashes.All(x=>File.Exists(x.Key)&&SetupEngine.HashFile(x.Key)==x.Value),"third-party notices remain alongside retained executable");
Check(!File.Exists(SetupEngine.SafePath(lifecycle,SetupEngine.ReceiptPath)),"successful removal clears active ownership record");
var updateRoot=Game("update-flow");
var previousPayload=payload.Select(x=>x.Path==SetupEngine.BridgePath?new PayloadFile(x.Path,SetupEngine.Hash(System.Text.Encoding.UTF8.GetBytes("previous loader")),System.Text.Encoding.UTF8.GetBytes("previous loader")):x).ToArray();
var previousController=new EnvironmentController(previousPayload,()=>false);previousController.Setup.Install(updateRoot);
var oldExe=Path.Combine(temp,"old-tool.exe");File.WriteAllText(oldExe,"previous tool");
ToolDeployment.KeepCopy(updateRoot,oldExe,previousController.Setup,"0.9.0");
var userMod=SetupEngine.SafePath(updateRoot,"BepInEx/plugins/Keep.dll");Directory.CreateDirectory(Path.GetDirectoryName(userMod)!);File.WriteAllText(userMod,"user mod");
var userConfig=SetupEngine.SafePath(updateRoot,"BepInEx/config/Keep.cfg");Directory.CreateDirectory(Path.GetDirectoryName(userConfig)!);File.WriteAllText(userConfig,"user settings");
var download=Path.Combine(temp,"new-tool.exe");File.WriteAllText(download,"new tool");
Check(c.HasUpdate(updateRoot),"older runtime and tool offer update");
var targetExe=SetupEngine.SafePath(updateRoot,SetupEngine.UnifiedToolPath);File.WriteAllText(targetExe,"changed tool");
Reject(()=>c.Update(updateRoot,download),"update preflight protects changed tool before runtime writes");
Check(File.ReadAllText(SetupEngine.SafePath(updateRoot,SetupEngine.BridgePath))=="previous loader","failed tool preflight leaves runtime unchanged");
File.WriteAllText(targetExe,"previous tool");c.Update(updateRoot,download);
Check(SetupEngine.HashFile(SetupEngine.SafePath(updateRoot,SetupEngine.BridgePath))==payload.Single(x=>x.Path==SetupEngine.BridgePath).Hash,"update replaces owned loader");
Check(File.ReadAllText(targetExe)=="new tool"&&!c.HasUpdate(updateRoot),"update also replaces game-local exe and records current version");
Check(File.ReadAllText(userMod)=="user mod"&&File.ReadAllText(userConfig)=="user settings","update preserves mods and configuration");
ToolDeployment.KeepCopy(updateRoot,oldExe,c.Setup,"0.9.0");
Check(c.HasUpdate(updateRoot),"tool-only release offers update even with identical runtime payload");c.Update(updateRoot,download);
Check(!c.HasUpdate(updateRoot),"tool-only update completes without runtime reinstall");
c.Manager.PauseEnvironment(updateRoot);Reject(()=>c.Update(updateRoot,download),"paused environment requires explicit re-enable before update");c.Manager.ResumeEnvironment(updateRoot);
Reject(()=>new EnvironmentController(payload,()=>true).Update(updateRoot,download),"game-running guard also covers tool-only updates");
foreach(var kind in new[]{"Downloads","Unrelated","FakeGame"}){
 var unrelated=Path.Combine(temp,"invalid-targets",kind);Directory.CreateDirectory(unrelated);
 File.WriteAllText(Path.Combine(unrelated,"keep.txt"),"existing user data");
 if(kind=="FakeGame")File.WriteAllText(Path.Combine(unrelated,"Tailside.exe"),"not a game executable");
 string TargetSnapshot()=>string.Join("\n",Directory.EnumerateFileSystemEntries(unrelated,"*",SearchOption.AllDirectories).Order().Select(p=>Path.GetRelativePath(unrelated,p)+(File.Exists(p)?":"+SetupEngine.HashFile(p):"/")));
 var original=TargetSnapshot();
 Check(!c.Inspect(unrelated).Installation.CanInstall,kind+" is not an installation target");
 Reject(()=>c.Setup.Install(unrelated),kind+" direct install request rejected");
 Check(TargetSnapshot()==original,kind+" rejection leaves all files and folders unchanged");
}
Console.WriteLine($"{count} unified checks passed. Fixtures: {temp}");


