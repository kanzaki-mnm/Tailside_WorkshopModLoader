using System.Text.Json;
using ModEnvironmentSetup.Core;

var temp=Path.Combine(Path.GetTempPath(),"ModSetup-check-"+Guid.NewGuid());
Directory.CreateDirectory(temp);
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);passed++;}
void Reject(Action action,string name){try{action();}catch{Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
string Game(string name)
{
 var root=Path.Combine(temp,name);Directory.CreateDirectory(Path.Combine(root,"Tailside_Data","Managed"));
 var bytes=new byte[256];BitConverter.GetBytes((ushort)0x5a4d).CopyTo(bytes,0);BitConverter.GetBytes(128).CopyTo(bytes,60);
 BitConverter.GetBytes(0x4550u).CopyTo(bytes,128);BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes,132);File.WriteAllBytes(Path.Combine(root,"Tailside.exe"),bytes);return root;
}
try
{
 using var zip=File.OpenRead(Path.GetFullPath(args.Length>0?args[0]:"TailsideModSetup/Assets/payload.zip"));
 var payload=SetupEngine.LoadPayload(zip);var engine=new SetupEngine(payload,()=>false);
 Check(payload.Count>10,"embedded payload integrity");
 Check(!payload.Any(p=>p.Path.StartsWith("BepInEx/core/")&&p.Path.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)),"user payload excludes API XML documentation");
 var fresh=Game("fresh");var vanilla=File.ReadAllBytes(Path.Combine(fresh,"Tailside.exe"));
 var save=Path.Combine(fresh,"save.dat");File.WriteAllText(save,"keep");
 Check(engine.Inspect(fresh).State==SetupState.NotInstalled,"fresh installation detected");
 engine.Install(fresh);Check(engine.Inspect(fresh).State==SetupState.Installed,"install verified");
 Check(SetupEngine.HashFile(Path.Combine(fresh,"Tailside.exe"))==SetupEngine.Hash(vanilla),"game executable untouched");
 Check(File.ReadAllText(save)=="keep","save untouched");
 Check(payload.All(p=>SetupEngine.HashFile(SetupEngine.SafePath(fresh,p.Path))==p.Hash),"installed hashes");
 var japanesePath=SetupEngine.SafePath(fresh,"BepInEx/TailsideModSetup/languages/ja.json");
 var language=JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(File.ReadAllText(japanesePath))!;
 var translations=(System.Text.Json.Nodes.JsonObject)language["translations"]!;
 translations.Remove("Cancel");translations["Install"]="custom install";translations["CustomKey"]="keep custom";
 File.WriteAllText(japanesePath,language.ToJsonString());
 Check(engine.Inspect(fresh).State==SetupState.NeedsUpdate,"missing language key offers update at same version");
 engine.Install(fresh);
 language=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(japanesePath))!.AsObject();translations=language["translations"]!.AsObject();
 Check(translations["Cancel"]!.GetValue<string>()=="キャンセル","update adds new Japanese key");
 Check(translations["Install"]!.GetValue<string>()=="custom install"&&translations["CustomKey"]!.GetValue<string>()=="keep custom","update preserves edited and custom translations");
 Check(engine.Inspect(fresh).State==SetupState.Installed,"language update settles without repeated update");
 File.WriteAllText(japanesePath,"user-edited translation");
 Check(engine.Inspect(fresh).State==SetupState.Installed,"edited translation does not block setup");
 File.Delete(japanesePath);
 Check(engine.Inspect(fresh).State==SetupState.Installed,"removed translation stays optional");
 var bridge=SetupEngine.SafePath(fresh,SetupEngine.BridgePath);File.Delete(bridge);
 Check(engine.Inspect(fresh).State==SetupState.NeedsRepair,"missing file detected");engine.Install(fresh);
 Check(engine.Inspect(fresh).State==SetupState.Installed,"repair successful");
 File.WriteAllText(japanesePath,"keep this translation");File.Delete(bridge);engine.Install(fresh);
 Check(File.ReadAllText(japanesePath)=="keep this translation","repair preserves edited translation");
 var receiptPath=SetupEngine.SafePath(fresh,SetupEngine.ReceiptPath);
 var receipt=JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receiptPath))!;
 var oldBytes="previous bridge build"u8.ToArray();File.WriteAllBytes(bridge,oldBytes);
 receipt.Files=receipt.Files.Select(f=>f.Path==SetupEngine.BridgePath?new OwnedFile(f.Path,SetupEngine.Hash(oldBytes)):f).ToList();
 File.WriteAllText(receiptPath,JsonSerializer.Serialize(receipt));
 Check(engine.Inspect(fresh).State==SetupState.NeedsUpdate,"owned older version detected");engine.Install(fresh);
 Check(engine.Inspect(fresh).State==SetupState.Installed&&SetupEngine.HashFile(bridge)==payload.Single(p=>p.Path==SetupEngine.BridgePath).Hash,"owned version update successful");
 File.AppendAllText(bridge,"user change");Check(engine.Inspect(fresh).State==SetupState.Blocked,"modified managed file preserved");
 Reject(()=>engine.Remove(fresh),"modified file removal refused");
 File.WriteAllBytes(bridge,payload.Single(p=>p.Path==SetupEngine.BridgePath).Bytes);
 var failRemove=new SetupEngine(payload,()=>false,i=>{if(i==3)throw new IOException("simulated remove failure");});
 Reject(()=>failRemove.Remove(fresh),"failed removal reported");Check(engine.Inspect(fresh).State==SetupState.Installed,"failed removal rolled back");
 var other=Path.Combine(fresh,"BepInEx","plugins","OtherMod.dll");Directory.CreateDirectory(Path.GetDirectoryName(other)!);File.WriteAllText(other,"keep other mod");
 engine.Remove(fresh);Check(!File.Exists(bridge)&&!File.Exists(Path.Combine(fresh,SetupEngine.ReceiptPath)),"owned files removed");
 Check(File.ReadAllText(other)=="keep other mod"&&File.Exists(save),"other mods and saves preserved on remove");
 var existing=Game("existing");
 foreach(var p in payload.Where(p=>p.Path!=SetupEngine.BridgePath&&!p.Path.Contains("/licenses/"))) {var dest=SetupEngine.SafePath(existing,p.Path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.WriteAllBytes(dest,p.Bytes);}
 engine.Install(existing);engine.Remove(existing);
 Check(File.Exists(Path.Combine(existing,"winhttp.dll"))&&File.Exists(Path.Combine(existing,"BepInEx/core/BepInEx.dll")),"pre-existing BepInEx retained");
 var externalBridge=SetupEngine.SafePath(existing,SetupEngine.BridgePath);Directory.CreateDirectory(Path.GetDirectoryName(externalBridge)!);File.WriteAllBytes(externalBridge,payload.Single(p=>p.Path==SetupEngine.BridgePath).Bytes);
 Check(engine.Inspect(existing).State==SetupState.Existing&&!engine.Inspect(existing).CanRemove,"externally installed environment recognized without ownership claim");
 File.Delete(Path.Combine(existing,".doorstop_version"));Check(engine.Inspect(existing).State==SetupState.Existing,"optional Doorstop marker not required for existing installation");
 var conflict=Game("conflict");File.WriteAllText(Path.Combine(conflict,"winhttp.dll"),"unknown loader");
 Check(engine.Inspect(conflict).State==SetupState.Blocked,"unknown injector preserved");Reject(()=>engine.Install(conflict),"conflicting install refused");
 var fail=Game("rollback");var failing=new SetupEngine(payload,()=>false,i=>{if(i==4)throw new IOException("simulated failure");});
 Reject(()=>failing.Install(fail),"interrupted operation reported");Check(!File.Exists(Path.Combine(fail,"winhttp.dll"))&&!File.Exists(Path.Combine(fail,SetupEngine.ReceiptPath)),"failed install rolled back");
 var running=Game("running");Reject(()=>new SetupEngine(payload,()=>true).Install(running),"running game blocks installation");
 Reject(()=>SetupEngine.SafePath(fresh,"../outside.dll"),"path traversal refused");
 var library=Path.Combine(temp,"SteamLibrary");var steam=Path.Combine(temp,"Steam");Directory.CreateDirectory(Path.Combine(steam,"steamapps"));Directory.CreateDirectory(Path.Combine(library,"steamapps","common"));
 var discoverGame=Path.Combine(library,"steamapps","common","Tailside");Directory.Move(Game("discover-source"),discoverGame);
 string Escape(string s)=>s.Replace("\\","\\\\");
 File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+Escape(library)+"\" } }");
 File.WriteAllText(Path.Combine(library,"steamapps","appmanifest_2922570.acf"),"\"AppState\" { \"appid\" \"2922570\" \"installdir\" \"Tailside\" }");
 Check(SteamDiscovery.Find([steam]).Single()==discoverGame,"secondary Steam library autodetected");
 File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"LibraryFolders\" { \"1\" \""+Escape(library)+"\" }");
 Check(SteamDiscovery.Find([steam]).Count==1,"legacy Steam library format");
 Check(!GameFolder.IsValid(temp),"wrong folder rejected");
 var receiptFile=Path.Combine(existing,SetupEngine.ReceiptPath);Directory.CreateDirectory(Path.GetDirectoryName(receiptFile)!);
 File.WriteAllText(receiptFile,JsonSerializer.Serialize(new Receipt{GameRoot=existing,Files=[new OwnedFile("save.dat",SetupEngine.Hash("keep"u8.ToArray()))]}));
 Check(engine.Inspect(existing).State==SetupState.Blocked,"tampered ownership record rejected");
 Console.WriteLine("Passed "+passed+" checks.");
}
finally{Directory.Delete(temp,true);}
