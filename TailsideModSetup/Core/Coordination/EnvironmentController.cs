using ModEnvironmentSetup.Core;
using ModManager.Core;
namespace UnifiedTools.Core;
public enum StartScreen {Setup,Manager,Recovery,Manual}
public enum Ownership {None,Owned,Manual,Unverified}
public sealed record EnvironmentView(StartScreen Screen,Inspection Installation,string Runtime,string Detail,Ownership Ownership);
public sealed class EnvironmentController
{
    public SetupEngine Setup {get;}
    public ManagerEngine Manager {get;}
    private readonly Func<bool> gameRunning;
    public EnvironmentController(IReadOnlyList<PayloadFile> payload,Func<bool> running)
    {
        gameRunning=running;
        Setup=new(payload,running,manualReadOnly:true);
        Manager=new(running,root=>Setup.CanRemoveForRecovery(root));
    }
    public Ownership OwnershipOf(string root)
    {
        if(Setup.CanRemoveForRecovery(root))return Ownership.Owned;
        if(File.Exists(SetupEngine.SafePath(root,SetupEngine.ReceiptPath)))return Ownership.Unverified;
        return SetupEngine.HasRuntimeFiles(root)?Ownership.Manual:Ownership.None;
    }
    public void KeepTool(string root,string source)=>ToolDeployment.KeepCopy(root,source,Setup);
    public bool HasUpdate(string root)=>OwnershipOf(root)==Ownership.Owned&&
        (Setup.Inspect(root).State==SetupState.NeedsUpdate||ToolDeployment.IsOlderCopy(root,SetupEngine.Version));
    public void Update(string root,string source)
    {
        if(gameRunning())throw new IOException("Close Tailside normally before changing the Mod environment.");
        if(OwnershipOf(root)!=Ownership.Owned)throw new IOException("No verified installation record.");
        var status=Setup.Inspect(root);
        if(status.State is not (SetupState.Installed or SetupState.NeedsUpdate))throw new IOException(status.Message);
        ToolDeployment.ValidateTarget(root,source,Setup);
        if(status.CanInstall)Setup.Install(root);
        KeepTool(root,source);
    }
    public IReadOnlyList<OwnedFile> RemovalFiles(string root)
    {
        if(OwnershipOf(root)!=Ownership.Owned)throw new IOException("No verified installation record.");
        return Setup.PreviewRemoval(root);
    }
    public IReadOnlyList<string> Remove(string root)
    {
        var kept=Setup.RemoveForRecovery(root).ToList();
        foreach(var path in new[]{"winhttp.dll",ManagerEngine.ParkedPath})
            if(File.Exists(SetupEngine.SafePath(root,path))&&!kept.Contains(path))kept.Add(path);
        var journal=SetupEngine.SafePath(root,ManagerEngine.RecoveryPath);
        if(kept.Count==0&&File.Exists(journal))
        {
            var archive=SetupEngine.SafePath(root,SetupEngine.DataPath+"/removal-history/recovery-"+Guid.NewGuid().ToString("N")+".json");
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);File.Move(journal,archive);
        }
        return kept;
    }
    public EnvironmentView Inspect(string root)
    {
        var installation=Setup.Inspect(root);string runtime;
        try{runtime=Manager.EnvironmentState(root);}catch(Exception e){runtime=e.Message;}
        var ownership=OwnershipOf(root);
        if(ownership is Ownership.Manual or Ownership.Unverified)return new(StartScreen.Manual,installation,runtime,installation.Message,ownership);
        if(installation.State==SetupState.NotInstalled&&runtime=="Not installed")return new(StartScreen.Setup,installation,runtime,installation.Message,ownership);
        if(installation.State==SetupState.Installed&&runtime=="Enabled")return new(StartScreen.Manager,installation,runtime,"",ownership);
        return new(StartScreen.Recovery,installation,runtime,installation.Message+"\n"+runtime,ownership);
    }
}

