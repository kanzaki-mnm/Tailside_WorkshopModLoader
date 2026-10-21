using System.Text.Json;
using ModEnvironmentSetup.Core;
namespace UnifiedTools.Core;
public static class ToolDeployment
{
    public static bool IsOlderCopy(string root,string version)
    {
        try{
            var path=SetupEngine.SafePath(root,SetupEngine.ToolReceiptPath);
            if(!File.Exists(path))return false;
            using var receipt=JsonDocument.Parse(File.ReadAllText(path));
            return receipt.RootElement.TryGetProperty("version",out var old)&&Version.TryParse(old.GetString(),out var installed)&&Version.Parse(version)>installed;
        }catch{return false;}
    }
    public static void ValidateTarget(string root,string source,SetupEngine owner)
    {
        if(!owner.CanRemoveForRecovery(root))throw new IOException("No verified installation record. The tool was not copied into this manually installed environment.");
        var target=SetupEngine.SafePath(root,SetupEngine.UnifiedToolPath);
        if(Path.GetFullPath(source).Equals(target,StringComparison.OrdinalIgnoreCase)||!File.Exists(target)||SetupEngine.HashFile(target)==SetupEngine.HashFile(source))return;
        var path=SetupEngine.SafePath(root,SetupEngine.ToolReceiptPath);
        if(!File.Exists(path))throw new IOException("An unrecognized tool executable already exists; it was not overwritten.");
        using var receipt=JsonDocument.Parse(File.ReadAllText(path));
        if(receipt.RootElement.GetProperty("hash").GetString()!=SetupEngine.HashFile(target))throw new IOException("An unrecognized tool executable already exists; it was not overwritten.");
    }
    public static void KeepCopy(string root,string source,SetupEngine owner,string? version=null)
    {
        ValidateTarget(root,source,owner);
        version??=SetupEngine.Version;
        if(!Version.TryParse(version,out _))throw new ArgumentException("Invalid tool version.");
        var target=SetupEngine.SafePath(root,SetupEngine.UnifiedToolPath);
        if(Path.GetFullPath(source).Equals(target,StringComparison.OrdinalIgnoreCase))return;
        var hash=SetupEngine.HashFile(source);var receipt=SetupEngine.SafePath(root,SetupEngine.ToolReceiptPath);
        if(!File.Exists(target)||SetupEngine.HashFile(target)!=hash){
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp=target+".new-"+Guid.NewGuid().ToString("N");
            try{File.Copy(source,temp);if(SetupEngine.HashFile(temp)!=hash)throw new IOException("Tool copy verification failed.");File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        ModManager.Core.ManagerEngine.Atomic(receipt,JsonSerializer.SerializeToUtf8Bytes(new{hash,version}));
    }
}
