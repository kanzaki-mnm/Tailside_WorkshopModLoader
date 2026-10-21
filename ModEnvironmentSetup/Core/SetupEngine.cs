using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModEnvironmentSetup.Core;

public enum SetupState { NotInstalled, Installed, NeedsRepair, NeedsUpdate, Existing, Blocked, Paused }
public sealed record Inspection(SetupState State, string Message, bool CanInstall, bool CanRemove);
public sealed record PayloadFile(string Path, string Hash, byte[] Bytes);
public sealed record OwnedFile(string Path, string Hash);
public sealed class Receipt
{
    public int Schema { get; set; } = 1;
    public string GameRoot { get; set; } = "";
    public string SetupVersion { get; set; } = SetupEngine.Version;
    public List<OwnedFile> Files { get; set; } = [];
}

public sealed class SetupEngine
{
    public const string Version = "1.0.0";
    public const string ToolsPath = "BepInEx/TailsideModSetup";
    public const string DataPath = ToolsPath + "/data";
    public const string ReceiptPath = DataPath + "/install.json";
    public const string ToolReceiptPath = DataPath + "/tool-install.json";
    public const string BridgePath = "BepInEx/patchers/TailsideWorkshopModLoader.dll";
    public const string LegacyBridgePath = "BepInEx/patchers/WorkshopBridge/WorkshopBridge.dll";
    public const string ManagerPath = "BepInEx/TailsideModSetup/ModManager.exe";
    public const string UnifiedToolPath = "BepInEx/TailsideModSetup/TailsideModSetup.exe";
    private readonly IReadOnlyList<PayloadFile> payload;
    private readonly Func<bool> gameRunning;
    private readonly Action<int>? afterWrite;
    private readonly bool manualReadOnly;
    public SetupEngine(IReadOnlyList<PayloadFile> payload, Func<bool> gameRunning, Action<int>? afterWrite = null, bool manualReadOnly = false)
    { this.payload = payload; this.gameRunning = gameRunning; this.afterWrite = afterWrite; this.manualReadOnly = manualReadOnly; }

    public static bool HasRuntimeFiles(string root)
    {
        if(new[]{"winhttp.dll","doorstop_config.ini","winhttp.dll.modmanager-disabled"}.Any(p=>File.Exists(SafePath(root,p))))return true;
        var pending=new Stack<string>(new[]{"BepInEx/core","BepInEx/plugins","BepInEx/patchers"});
        while(pending.Count>0){
            var relative=pending.Pop();var folder=SafePath(root,relative);if(!Directory.Exists(folder))continue;
            foreach(var path in Directory.EnumerateFileSystemEntries(folder)){
                var attributes=File.GetAttributes(path);
                if((attributes&FileAttributes.ReparsePoint)!=0)return true;
                if((attributes&FileAttributes.Directory)==0)return true;
                pending.Push(Path.GetRelativePath(root,path));
            }
        }
        return false;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string HashFile(string file) => Hash(File.ReadAllBytes(file));

    public static IReadOnlyList<PayloadFile> LoadPayload(Stream zipStream)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(zip.GetEntry("hashes.json")!.Open())!;
        var result = new List<PayloadFile>();
        foreach (var item in hashes)
        {
            // A generated, embedded allowlist. No arbitrary files from an external archive are extracted.
            if (!(item.Key is "winhttp.dll" or "doorstop_config.ini" or ".doorstop_version" ||
                item.Key.StartsWith("BepInEx/core/", StringComparison.Ordinal) ||
                item.Key == BridgePath || item.Key == LegacyBridgePath || item.Key == ManagerPath || IsLanguageDefault(item.Key) || item.Key.StartsWith("BepInEx/TailsideModSetup/licenses/", StringComparison.Ordinal)))
                throw new InvalidDataException("Unsupported payload path.");
            using var input = zip.GetEntry(item.Key)?.Open() ?? throw new InvalidDataException("Missing embedded file.");
            using var output = new MemoryStream(); input.CopyTo(output);
            var bytes = output.ToArray();
            if (Hash(bytes) != item.Value) throw new InvalidDataException("Embedded payload checksum mismatch.");
            result.Add(new PayloadFile(item.Key, item.Value, bytes));
        }
        return result;
    }

    public static string SafePath(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Unsafe setup path.");
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Setup path escapes game folder.");
        for (var part = full; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked game folders are not supported by this preview.");
        return full;
    }

    private Receipt? ReadReceipt(string root)
    {
        var path = SafePath(root, ReceiptPath);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)) ?? throw new InvalidDataException("Unreadable install record.");
        if (receipt.Schema != 1 || !Path.GetFullPath(receipt.GameRoot).TrimEnd('\\').Equals(Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Install record belongs to a different game folder.");
        var allowed = payload.Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allowed.Add(ManagerPath); // Previous two-exe installs may still own the retired Manager.
        allowed.Add(LegacyBridgePath); // Historical receipts remain readable; never infer ownership.
        allowed.UnionWith(new[]{"0Harmony.xml","BepInEx.Harmony.xml","BepInEx.Preloader.xml","BepInEx.xml","MonoMod.RuntimeDetour.xml","MonoMod.Utils.xml"}.Select(n=>"BepInEx/core/"+n));
        if (receipt.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Files.Count ||
            receipt.Files.Any(f => !allowed.Contains(f.Path) || !Regex.IsMatch(f.Hash, "^[A-F0-9]{64}$")))
            throw new InvalidDataException("Invalid install record. Files were left untouched.");
        return receipt;
    }

    private bool CompatibleExisting(string root, PayloadFile file)
    {
        var path = SafePath(root, file.Path);
        if (!File.Exists(path)) return false;
        if(IsLanguageDefault(file.Path)||IsToolLicense(file.Path))return true;
        if (HashFile(path) == file.Hash) return true;
        if (file.Path == "doorstop_config.ini")
        {
            var text = File.ReadAllText(path);
            // Respect user preferences only when the target remains the tested BepInEx preloader.
            return Regex.IsMatch(text, @"(?im)^\s*enabled\s*=\s*true\s*$") &&
                Regex.IsMatch(text, @"(?im)^\s*target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Preloader\.dll\s*$");
        }
        return false;
    }

    public Inspection Inspect(string root)
    {
        try
        {
            if (!GameFolder.IsValid(root)) return new(SetupState.Blocked, "Choose the folder containing Tailside.exe (Windows 64-bit).", false, false);
            if (payload.Any(p=>p.Path==BridgePath) && File.Exists(SafePath(root,LegacyBridgePath)))
                return new(SetupState.Blocked,"The legacy WorkshopBridge is installed. Remove it before installing Tailside Workshop Mod Loader. No files were changed.",false,false);
            if (File.Exists(SafePath(root,"winhttp.dll.modmanager-disabled")))
                return new(SetupState.Paused,"The Mod environment is paused. Open Mod Manager to restore it. Setup will not reactivate it automatically.",false,false);
            var receipt = ReadReceipt(root);
            if(manualReadOnly && receipt==null && HasRuntimeFiles(root))
                return new(SetupState.Existing,"This environment was not installed by this tool. Follow the manual instructions; no files will be changed.",false,false);
            foreach (var item in receipt?.Files ?? [])
            {
                var path = SafePath(root, item.Path);
                if (File.Exists(path) && HashFile(path) != item.Hash)
                    return new(SetupState.Blocked, "A file managed by Setup has been changed. Restore it before repair or removal. Your files were left untouched.", false, false);
            }
            var owned = (receipt?.Files ?? []).Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var framework = payload.Where(p => p.Path.StartsWith("BepInEx/core/") || p.Path is "winhttp.dll" or "doorstop_config.ini").ToArray();
            bool hasFramework = framework.Any(p => File.Exists(SafePath(root,p.Path)));
            if (hasFramework)
                foreach (var file in framework.Where(f => !owned.Contains(f.Path)))
                    if (!CompatibleExisting(root,file)) return new(SetupState.Blocked, "An incomplete or different BepInEx installation was found. Setup will not overwrite it. See Details for help.",false,false);
            foreach (var file in payload.Where(f => !owned.Contains(f.Path)))
                if (File.Exists(SafePath(root,file.Path)) && !CompatibleExisting(root,file))
                    return new(SetupState.Blocked,"A different version or conflicting file was found. Setup will not overwrite it.",false,false);
            bool complete = payload.Where(f => !IsLanguageDefault(f.Path) && !f.Path.Contains("/licenses/",StringComparison.Ordinal) && f.Path!=".doorstop_version").All(f => File.Exists(SafePath(root,f.Path)));
            if (receipt != null)
            {
                if (!complete) return new(SetupState.NeedsRepair,"Some setup files are missing. Repair will restore them.",true,receipt.Files.Count>0);
                bool update = payload.Any(p => owned.Contains(p.Path) && HashFile(SafePath(root,p.Path)) != p.Hash);
                return update ? new(SetupState.NeedsUpdate,"An update for the Mod environment is ready.",true,true) :
                    new(SetupState.Installed,"Your Mod environment is ready. Launch Tailside from Steam as usual.",false,receipt.Files.Count>0);
            }
            if (complete) return new(SetupState.Existing,"A compatible Mod environment is already installed. No changes are needed.",false,false);
            return new(SetupState.NotInstalled, hasFramework ? "Compatible BepInEx found. Setup will add the missing Workshop support." :
                "One-time setup. Then subscribe to compatible Workshop mods and launch the game from Steam.",true,false);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(SetupState.Blocked, "The installation could not be checked safely: " + e.Message,false,false); }
    }

    public void Install(string root)
    {
        Guard(root);
        var status = Inspect(root);
        if (!status.CanInstall) throw new InvalidOperationException(status.Message);
        var previous = ReadReceipt(root);
        var receipt = new Receipt { GameRoot=Path.GetFullPath(root), Files=previous?.Files.ToList() ?? [] };
        var writes = new List<(string Relative, byte[]? Bytes)>();
        foreach (var file in payload)
        {
            var path = SafePath(root,file.Path);
            if(IsLanguageDefault(file.Path)||IsToolLicense(file.Path))
            {
                // Seed editable language files on setup only; never overwrite or own user translations.
                if(!File.Exists(path))writes.Add((file.Path,file.Bytes));
                continue;
            }
            var owned = receipt.Files.FindIndex(f => f.Path.Equals(file.Path,StringComparison.OrdinalIgnoreCase));
            if (!File.Exists(path) || owned >= 0 && HashFile(path) != file.Hash)
            {
                writes.Add((file.Path,file.Bytes));
                if (owned >= 0) receipt.Files.RemoveAt(owned);
                receipt.Files.Add(new(file.Path,file.Hash));
            }
        }
        writes.Add((ReceiptPath,JsonSerializer.SerializeToUtf8Bytes(receipt,new JsonSerializerOptions{WriteIndented=true})));
        Apply(root,writes);
    }

    private static bool IsToolLicense(string path)=>path.StartsWith(ToolsPath+"/licenses/",StringComparison.Ordinal);

    private static bool IsLanguageDefault(string path)=>path is
        "BepInEx/TailsideModSetup/languages/en.json" or
        "BepInEx/TailsideModSetup/languages/ja.json" or
        "BepInEx/TailsideModSetup/languages/README.md";

    public void Remove(string root)
    {
        Guard(root);
        var status=Inspect(root);
        if (!status.CanRemove) throw new InvalidOperationException(status.Message);
        var receipt=ReadReceipt(root)!;
        // Only the exact files recorded as created by this installer are eligible for removal.
        Apply(root,receipt.Files.Select(f => (f.Path,(byte[]?)null)).Append((ReceiptPath,null)).ToList());
    }

    public void RetireLegacyManager(string root)
    {
        Guard(root);var receipt=ReadReceipt(root);if(receipt==null)return;
        var old=receipt.Files.SingleOrDefault(x=>x.Path==ManagerPath);if(old==null)return;
        var path=SafePath(root,ManagerPath);
        if(File.Exists(path)&&HashFile(path)!=old.Hash)throw new IOException("The old Manager has changed; it was kept.");
        receipt.Files.Remove(old);
        Apply(root,[(ManagerPath,null),(ReceiptPath,JsonSerializer.SerializeToUtf8Bytes(receipt))]);
    }
    public bool CanRepairOwned(string root)
    {try{return GameFolder.IsValid(root)&&ReadReceipt(root)!=null&&!File.Exists(SafePath(root,"winhttp.dll.modmanager-disabled"))&&!(payload.Any(p=>p.Path==BridgePath)&&File.Exists(SafePath(root,LegacyBridgePath)));}catch{return false;}}
    public bool CanRemoveForRecovery(string root)
    {try{return ReadReceipt(root)!=null;}catch{return false;}}
    public IReadOnlyList<OwnedFile> PreviewRemoval(string root) =>
        (ReadReceipt(root)??throw new IOException("No verified installation record.")).Files.ToArray();

    // Explicit recovery command. Modified owned files are backed up before replacement;
    // an unknown/unowned conflicting file is never overwritten.
    public void RepairOwned(string root)
    {
        Guard(root);
        if(payload.Any(p=>p.Path==BridgePath)&&File.Exists(SafePath(root,LegacyBridgePath)))throw new IOException("Remove the legacy WorkshopBridge before repairing to the current loader.");
        if(File.Exists(SafePath(root,"winhttp.dll.modmanager-disabled")))throw new IOException("Restore the paused environment before repair.");
        var receipt=ReadReceipt(root)??throw new IOException("No valid installation record; unknown files cannot be repaired automatically.");
        var owned=receipt.Files.Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var file in payload.Where(x=>!IsLanguageDefault(x.Path)&&!owned.Contains(x.Path)))
            if(File.Exists(SafePath(root,file.Path))&&!CompatibleExisting(root,file))throw new IOException("Unowned conflicting file: "+file.Path);
        var writes=new List<(string Relative,byte[]? Bytes)>();
        var backup=DataPath+"/repair-backups/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"/";
        foreach(var file in payload)
        {
            var path=SafePath(root,file.Path);
            if(IsLanguageDefault(file.Path)||IsToolLicense(file.Path)){if(!File.Exists(path))writes.Add((file.Path,file.Bytes));continue;}
            if(File.Exists(path)&&HashFile(path)==file.Hash)continue;
            if(File.Exists(path))writes.Add((backup+file.Path,File.ReadAllBytes(path)));
            writes.Add((file.Path,file.Bytes));receipt.Files.RemoveAll(x=>x.Path==file.Path);receipt.Files.Add(new(file.Path,file.Hash));
        }
        writes.Add((backup+"install.json",File.ReadAllBytes(SafePath(root,ReceiptPath))));
        writes.Add((ReceiptPath,JsonSerializer.SerializeToUtf8Bytes(receipt)));Apply(root,writes);
    }

    // Recovery removal preserves modified/unowned files. The report identifies leftovers.
    public IReadOnlyList<string> RemoveForRecovery(string root)
    {
        // Recovery is anchored to a validated ownership record, not the current game runtime.
        if(gameRunning())throw new InvalidOperationException("Close Tailside normally before changing the Mod environment.");
        var receipt=ReadReceipt(root)??throw new IOException("No valid installation record; automatic removal is unavailable.");
        var writes=new List<(string Relative,byte[]? Bytes)>();var kept=new List<OwnedFile>();
        foreach(var file in receipt.Files)
        {
            var relative=file.Path;
            if(relative=="winhttp.dll"&&!File.Exists(SafePath(root,relative))&&File.Exists(SafePath(root,"winhttp.dll.modmanager-disabled")))relative="winhttp.dll.modmanager-disabled";
            var path=SafePath(root,relative);
            if(!File.Exists(path))continue;
            if(HashFile(path)==file.Hash)writes.Add((relative,null));else kept.Add(file);
        }
        if(kept.Count==0)writes.Add((ReceiptPath,null));
        else{receipt.Files=kept;writes.Add((ReceiptPath,JsonSerializer.SerializeToUtf8Bytes(receipt)));}
        Apply(root,writes);return kept.Select(x=>x.Path).ToArray();
    }

    private void Guard(string root)
    {
        if (!GameFolder.IsValid(root)) throw new InvalidOperationException("Tailside was not found in this folder.");
        if (gameRunning()) throw new InvalidOperationException("Close Tailside normally before changing the Mod environment.");
    }

    private void Apply(string root,List<(string Relative,byte[]? Bytes)> changes)
    {
        var snapshots=new List<(string Path,byte[]? Before)>();
        var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var change in changes)
            {
                if (gameRunning()) throw new InvalidOperationException("Tailside started during setup. No changes will be kept.");
                var path=SafePath(root,change.Relative);
                var before=File.Exists(path)?File.ReadAllBytes(path):null;
                snapshots.Add((path,before));
                if (change.Bytes == null) { if (File.Exists(path)) File.Delete(path); }
                else
                {
                    for(var dir=Path.GetDirectoryName(path);dir!=null && !Directory.Exists(dir);dir=Path.GetDirectoryName(dir)) directories.Add(dir);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    AtomicWrite(path,change.Bytes);
                }
                afterWrite?.Invoke(snapshots.Count);
            }
        }
        catch
        {
            foreach (var snapshot in snapshots.AsEnumerable().Reverse())
                if (snapshot.Before == null) { if(File.Exists(snapshot.Path)) File.Delete(snapshot.Path); }
                else if(!File.Exists(snapshot.Path) || HashFile(snapshot.Path)!=Hash(snapshot.Before)) AtomicWrite(snapshot.Path,snapshot.Before);
            foreach(var dir in directories.OrderByDescending(x=>x.Length))
                if(Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            throw;
        }
    }

    private static void AtomicWrite(string path,byte[] bytes)
    {
        var temporary=path+".setup-"+Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temporary,bytes); File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}


