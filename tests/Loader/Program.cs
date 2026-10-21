using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using TailsideWorkshopModLoader;

if (args.Length == 2 && args[0] == "steam")
{
    foreach (var item in SteamSubscriptions.Read(args[1], Console.WriteLine))
        Console.WriteLine(item.Id + " " + item.Path);
    return;
}
if (args.Length == 2 && args[0] == "reparse")
{
    try { Catalog.ContainedPath(args[1], "linked/anything.dll"); }
    catch (InvalidDataException) { Console.WriteLine("PASS reparse point escape rejected"); return; }
    throw new Exception("Reparse point accepted.");
}
var root = Path.Combine(Path.GetTempPath(), "TailsideWorkshopModLoader-check-" + Guid.NewGuid());
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch { Check(true,name); return; } throw new Exception("Accepted: " + name); }
HashSet<string> Ids(params string[] ids) => new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
try
{
    byte[] Buffer(string text) => System.Text.Encoding.UTF8.GetBytes(text+"\0");
    Check(SteamSubscriptions.DecodeInstallFolder(Buffer(@"C:\SteamLibrary\workshop\content\2922570\123")) == @"C:\SteamLibrary\workshop\content\2922570\123", "Windows Steam folder preserved");
    Check(SteamSubscriptions.DecodeInstallFolder(Buffer(@"Z:\run\media\deck\SD Card\steamapps\workshop\content\2922570\123")) == @"Z:\run\media\deck\SD Card\steamapps\workshop\content\2922570\123", "already mapped removable-library path preserved (synthetic)");
    Check(SteamSubscriptions.DecodeInstallFolder(Buffer(@"D:\Steam ライブラリ\☕\123")) == @"D:\Steam ライブラリ\☕\123", "UTF-8 non-ASCII install folder preserved");
    foreach(var invalid in new[]{"/home/deck/.steam/steam/steamapps/workshop/content/2922570/123",@"\Steam\123",@"C:Steam\123","relative/folder",@"\\?\C:\Steam",@"\\.\device"})
        Reject(()=>SteamSubscriptions.DecodeInstallFolder(Buffer(invalid)),"unmapped or ambiguous path rejected: "+invalid);
    Reject(()=>SteamSubscriptions.DecodeInstallFolder(new byte[]{0x43,0x3a,0x5c,0xc3,0x28,0}),"invalid UTF-8 rejected");
    Reject(()=>SteamSubscriptions.DecodeInstallFolder(System.Text.Encoding.UTF8.GetBytes(@"C:\Steam")),"unterminated install folder rejected");
    Reject(()=>SteamSubscriptions.DecodeInstallFolder(new byte[]{0}),"empty install folder rejected");
    Check(SteamSubscriptions.IsReady(5),"subscribed and installed ready");
    foreach (var state in new uint[]{0,1,4,5|8,5|16,5|32})
        Check(!SteamSubscriptions.IsReady(state),"ineligible Steam state " + state);
    var folder = Path.Combine(root, "Example");
    Directory.CreateDirectory(Path.Combine(folder, "plugins"));
    File.WriteAllBytes(Path.Combine(folder, "plugins", "Example.dll"), new byte[] {0});
    var m = new Manifest { Schema=1, AppId=Catalog.AppId, Id="example.mod", Name="Example", Version="1.0.0", Entry="plugins/Example.dll", MinimumLoader="0.1.0" };
    void Save() => Catalog.WriteJson(Path.Combine(folder,"mod.json"), m);
    Save();
    Check(Catalog.LoaderVersion.ToString()==typeof(Catalog).Assembly.GetName().Version.ToString(3),"compatibility version follows assembly release version");
    m.MinimumLoader=Catalog.LoaderVersion.ToString();Save();
    Check(Catalog.ReadPackage(folder,"workshop",123)!=null,"current release minimum is accepted");
    m.MinimumLoader=new Version(Catalog.LoaderVersion.Major+1,0,0).ToString();Save();
    Reject(()=>Catalog.ReadPackage(folder,"workshop",123),"newer major loader required is rejected");
    m.MinimumLoader="0.1.0";Save();
    var local = Catalog.ReadPackage(folder,"local");
    var workshop = Catalog.ReadPackage(folder,"workshop",123);
    Check(local.Manifest.Id == "example.mod", "valid package");
    Check(Catalog.ReadPackage(root,"local") == null,"skin / no manifest ignored");
    Reject(()=>Catalog.ContainedPath(folder,"../example/plugins/Example.dll"),"case-only sibling traversal rejected");
    m.Entry="../escape.dll"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"path traversal");
    m.Entry="C:/escape.dll"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"absolute entry");
    m.Entry="Example.dll"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"entry outside plugins");
    m.Entry="plugins/Example.dll"; m.AppId=1; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"wrong game");
    m.AppId=Catalog.AppId; m.Schema=2; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"future schema");
    m.Schema=1; m.MinimumLoader="9.0.0"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"future bridge");
    m.MinimumLoader="0.1.0"; m.Version="bad"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"invalid version");
    m.Version="1.0.0"; Save();
    Check(Catalog.Select(new[]{local},Ids(),_=>{}).Count==0,"local packages no longer eligible");
    Check(Catalog.Select(new[]{workshop},Ids(),_=>{}).Count==1,"workshop package eligible");
    var otherWorkshop = Catalog.ReadPackage(folder,"workshop",456);
    Check(Catalog.Select(new[]{workshop,otherWorkshop},Ids(),_=>{}).Count==0,"ambiguous workshop IDs rejected");
    Check(Catalog.Select(new[]{workshop},Ids("EXAMPLE.MOD"),_=>{}).Count==0,"manual plugins always take precedence");
    Check(Catalog.Select(Array.Empty<Package>(),Ids(),_=>{}).Count==0,"removed subscription has no fallback");
    Check(Catalog.Select(new[]{Catalog.ReadPackage(folder,"workshop",0)},Ids(),_=>{}).Count==0,"workshop item ID required");
    Check(Catalog.Select(new[]{local,workshop},Ids(),_=>{}).Single().Source=="workshop","no local override of workshop");
    m.Version="2.0.0"; Save();
    Check(Catalog.ReadPackage(folder,"local").Manifest.Version=="2.0.0","updated manifest is read without stale cache");
    m.Entry="plugins/missing.dll"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"missing entry DLL");
    m.Entry="plugins/Example.dll"; m.Id="bad/id"; Save(); Reject(()=>Catalog.ReadPackage(folder,"local"),"invalid mod ID");
    m.Id="example.mod"; Save();
    File.WriteAllText(Path.Combine(folder,"mod.json"),"{broken"); Reject(()=>Catalog.ReadPackage(folder,"local"),"broken JSON");
    Console.WriteLine("Passed " + passed + " checks.");
}
finally { Directory.Delete(root,true); }
