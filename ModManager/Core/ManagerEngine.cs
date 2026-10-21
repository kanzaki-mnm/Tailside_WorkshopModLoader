using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModEnvironmentSetup.Core;
using WorkshopBridge;

namespace ModManager.Core;

public sealed record ModRow(string Id, string Name, string Version, string Source, string Folder, ulong WorkshopId = 0);
public sealed record RecoveryRecord(int Schema, string GameRoot, string Hash);
public sealed class ManagerEngine(Func<bool> gameRunning, Func<string,bool>? canChange = null)
{
    public const string ControlPath = "BepInEx/config/WorkshopBridge.mods.json";
    public const string RecoveryPath = SetupEngine.DataPath+"/recovery.json";
    public const string ParkedPath = "winhttp.dll.modmanager-disabled";
    // Only this tested Doorstop binary is movable. An unknown proxy is never modified.
    public const string ProxyHash = "8C6CDBC38836DEE87E3368F5DE1994D7C0CCEBF29E4CE7ABA3C0981F9375412C";
    public static string Safe(string root, string relative) => SetupEngine.SafePath(root, relative);

    public static bool IsGame(string root)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(Safe(root,"Tailside.exe")));
            if(reader.ReadUInt16()!=0x5a4d)return false;
            reader.BaseStream.Position=0x3c; var offset=reader.ReadInt32();
            if(offset<64 || offset>reader.BaseStream.Length-6)return false;
            reader.BaseStream.Position=offset;
            return reader.ReadUInt32()==0x4550 && reader.ReadUInt16()==0x8664;
        }catch{return false;}
    }

    public ModControl ReadControl(string root) => ModControl.Read(Safe(root,ControlPath));
    public void SaveControl(string root, ModControl value)
    {
        Guard(root);
        if(File.Exists(Safe(root,SetupEngine.BridgePath)))throw new InvalidOperationException("Individual mod controls are not supported by Tailside Workshop Mod Loader. Use Steam Workshop subscriptions.");
        if(value.Schema!=1 || value.DisabledIds==null || value.DisabledIds.Length>1024 ||
            value.DisabledIds.Any(x=>!Regex.IsMatch(x,@"^[A-Za-z0-9._-]{1,160}$")))throw new InvalidDataException("Invalid Mod selection.");
        var file=Safe(root,ControlPath);
        // Never overwrite unknown/corrupt settings. UI checks for stale edits before calling.
        ModControl.Read(file);
        using var memory=new MemoryStream();
        new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ModControl)).WriteObject(memory,value);
        Atomic(file,memory.ToArray());
    }

    public static Dictionary<string,string> BridgeSettings(string root)
    {
        var file=Safe(root,"BepInEx/config/WorkshopBridge.cfg");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string section="";
        if(!File.Exists(file))return result;
        foreach(var raw in File.ReadLines(file))
        {
            var line=raw.Trim();if(line.StartsWith('#')||line.StartsWith(';'))continue;
            if(line.StartsWith('[')&&line.EndsWith(']')){section=line[1..^1];continue;}
            var split=line.IndexOf('=');if(split>0)result[section+"."+line[..split].Trim()]=line[(split+1)..].Trim();
        }
        return result;
    }

    public IReadOnlyList<ModRow> Inventory(string root,List<string> warnings)
    {
        if(File.Exists(Safe(root,SetupEngine.BridgePath)))return Array.Empty<ModRow>();
        var folders=new Dictionary<string,(string Source, ulong WorkshopId)>(StringComparer.OrdinalIgnoreCase);
        var settings=BridgeSettings(root);
        if(settings.GetValueOrDefault("Development.Enabled")?.Equals("true",StringComparison.OrdinalIgnoreCase)==true)
        {
            var local=settings.GetValueOrDefault("Development.LocalRoot")??"BepInEx/local-mods";
            var path=Path.GetFullPath(Path.IsPathRooted(local)?local:Path.Combine(root,local));
            AddRoot(path,"Local");
        }
        // Inventory only. Actual subscription/ready-state eligibility is decided by the bridge's
        // Steam API at launch; a downloaded folder is not proof of a current subscription.
        var common=Directory.GetParent(root);
        if(common?.Name.Equals("common",StringComparison.OrdinalIgnoreCase)==true)
            AddRoot(Path.Combine(common.Parent!.FullName,"workshop/content/2922570"),"Workshop download");
        var report=Safe(root,"BepInEx/config/WorkshopBridge.last-run.log");
        if(File.Exists(report))foreach(var line in File.ReadLines(report))
        {
            var match=Regex.Match(line,@"^Candidate .* source=workshop workshop=(\d+) path=(.+)$");
            if(match.Success)
            {
                var entry=Path.GetFullPath(match.Groups[2].Value);
                var plugins=Directory.GetParent(entry);
                if(plugins?.Name.Equals("plugins",StringComparison.OrdinalIgnoreCase)==true)
                    folders.TryAdd(plugins.Parent!.FullName,("Workshop (last launch)",ParseWorkshopId(match.Groups[1].Value)));
            }
        }
        var rows=new List<ModRow>();
        foreach(var item in folders)
            try
            {
                var package=Catalog.ReadPackage(item.Key,"inventory",0);
                if(package!=null)rows.Add(new(package.Manifest.Id,package.Manifest.Name,package.Manifest.Version,item.Value.Source,item.Key,item.Value.WorkshopId));
            }catch(Exception e){warnings.Add(Path.GetFileName(item.Key)+": "+e.Message);}
        var manual=Safe(root,"BepInEx/plugins");
        if(Directory.Exists(manual)&&Directory.EnumerateFiles(manual,"*.dll",SearchOption.AllDirectories).Any())
            warnings.Add("Manual BepInEx/plugins mods are outside this list. Use Pause Mod environment to bypass all BepInEx mods.");
        return rows.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();

        void AddRoot(string path,string source)
        {
            try { if(Directory.Exists(path))foreach(var dir in Directory.GetDirectories(path))folders.TryAdd(dir,(source,source=="Workshop download"?ParseWorkshopId(Path.GetFileName(dir)):0)); }
            catch(Exception e){warnings.Add("Unable to list "+source+": "+e.Message);}
        }
    }

    private static ulong ParseWorkshopId(string value) =>
        ulong.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var id)?id:0;
    public static string WorkshopUrl(ulong id) => id>0
        ?"https://steamcommunity.com/sharedfiles/filedetails/?id="+id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        :throw new ArgumentOutOfRangeException(nameof(id));

    private RecoveryRecord? Recovery(string root)
    {
        var file=Safe(root,RecoveryPath);if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>8192)throw new InvalidDataException("Invalid recovery record.");
        var value=JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(file));
        if(value==null||value.Schema!=1||value.Hash!=ProxyHash||
            !Path.GetFullPath(value.GameRoot).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unknown recovery record; no files were changed.");
        return value;
    }

    public string EnvironmentState(string root)
    {
        var proxy=Safe(root,"winhttp.dll");var parked=Safe(root,ParkedPath);var record=Recovery(root);
        if(File.Exists(parked))
        {
            if(record==null||SetupEngine.HashFile(parked)!=ProxyHash||File.Exists(proxy))
                throw new InvalidDataException("Recovery files conflict or have changed. Nothing will be overwritten.");
            return "Paused";
        }
        if(!File.Exists(proxy))return record==null?"Not installed":"Recovery files missing";
        if(SetupEngine.HashFile(proxy)!=ProxyHash)throw new InvalidDataException("Unknown winhttp.dll. This version of Manager cannot safely pause it.");
        var config=Safe(root,"doorstop_config.ini");
        if(!File.Exists(config)||!Regex.IsMatch(File.ReadAllText(config),@"(?im)^\s*target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Preloader\.dll\s*$"))
            throw new InvalidDataException("The proxy is not configured for the supported BepInEx environment.");
        return "Enabled";
    }

    public void PauseEnvironment(string root)
    {
        Guard(root);if(EnvironmentState(root)!="Enabled")throw new InvalidOperationException("The Mod environment is not enabled.");
        // Journal is committed first. An interrupted rename is recognizable on the next run.
        Atomic(Safe(root,RecoveryPath),JsonSerializer.SerializeToUtf8Bytes(new RecoveryRecord(1,Path.GetFullPath(root),ProxyHash)));
        Guard(root);
        File.Move(Safe(root,"winhttp.dll"),Safe(root,ParkedPath));
    }

    public void ResumeEnvironment(string root)
    {
        Guard(root);
        if(!GameFolder.IsValid(root))throw new InvalidOperationException("This game runtime is not supported. Keep the Mod environment disabled or remove it.");
        if(EnvironmentState(root)!="Paused")throw new InvalidOperationException("There is no paused environment to restore.");
        File.Move(Safe(root,ParkedPath),Safe(root,"winhttp.dll"));
        // Keep the journal for recovery; it is harmless when the original binary is back.
    }

    private void Guard(string root)
    {
        if(canChange!=null&&!canChange(root))throw new InvalidOperationException("No verified installation record. Use the manual instructions; no files were changed.");
        if(!IsGame(root))throw new InvalidOperationException("Choose the folder containing the 64-bit Tailside.exe.");
        if(gameRunning())throw new InvalidOperationException("Save if needed, then close Tailside normally before applying changes.");
    }

    public static void Atomic(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+".manager-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

