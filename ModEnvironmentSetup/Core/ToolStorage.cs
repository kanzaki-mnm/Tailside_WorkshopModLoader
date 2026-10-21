using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text.Json;
namespace ModEnvironmentSetup.Core;

public static class ToolStorage
{
    private static string? selectedRoot;
    // Validate the complete destination, including child directories and the file itself.
    public static string FilePath(string relative)=>SetupEngine.SafePath(DirectoryPath,relative);
    public static void SetGameRoot(string root)=>selectedRoot=Path.GetFullPath(root);
    public static bool Isolated=>Array.IndexOf(Environment.GetCommandLineArgs(),"--ui-settings-dir")>=0;
    public static string DirectoryPath
    {
        get
        {
            var args=Environment.GetCommandLineArgs();var at=Array.IndexOf(args,"--ui-settings-dir");
            if(at>=0&&at+1<args.Length)return Path.GetFullPath(args[at+1]);
            var root=selectedRoot;
            at=Array.IndexOf(args,"--game-dir");
            if(root==null&&at>=0&&at+1<args.Length&&File.Exists(Path.Combine(args[at+1],"Tailside.exe")))root=Path.GetFullPath(args[at+1]);
            if(root==null)
            {
                for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
                    if(File.Exists(Path.Combine(dir.FullName,"Tailside.exe"))){root=dir.FullName;break;}
            }
            if(root==null)
            {
                var steamRoots=new List<string>();
                if(OperatingSystem.IsWindows())foreach(var spec in new[]{(Registry.CurrentUser,@"Software\Valve\Steam","SteamPath"),(Registry.LocalMachine,@"SOFTWARE\WOW6432Node\Valve\Steam","InstallPath")})
                    try{using var key=spec.Item1.OpenSubKey(spec.Item2);if(key?.GetValue(spec.Item3) is string path)steamRoots.Add(path);}catch{}
                steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
                var games=SteamDiscovery.Find(steamRoots);if(games.Count==1)root=games[0];
            }
            if(root==null)throw new IOException("Choose the Tailside game folder before saving tool settings or reports.");
            return SetupEngine.SafePath(root,SetupEngine.ToolsPath);
        }
    }
    // Only the old tools' known files are imported. Existing destinations are preserved.
    public static int ImportLegacy(string source,string destination)
    {
        source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
        if(source.Equals(destination,StringComparison.OrdinalIgnoreCase))throw new IOException("Migration paths must differ.");
        if(!Directory.Exists(source))return 0;
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked migration source is unsupported.");
        var files=new List<string>();
        if(File.Exists(Path.Combine(source,"manager-language.json")))files.Add(Path.Combine(source,"manager-language.json"));
        foreach(var sub in new[]{"languages","reports"})
        {
            var folder=Path.Combine(source,sub);if(Directory.Exists(folder))Collect(folder);
        }
        var records=new List<object>();
        foreach(var file in files)
        {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked migration files are unsupported.");
            var relative=Path.GetRelativePath(source,file);var target=SetupEngine.SafePath(destination,relative);
            var hash=SetupEngine.HashFile(file);
            if(File.Exists(target)&&SetupEngine.HashFile(target)!=hash)
                target=SetupEngine.SafePath(destination,Path.Combine("legacy-import",hash,relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if(!File.Exists(target))
            {
                var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.Copy(file,temp);if(SetupEngine.HashFile(temp)!=hash)throw new IOException("Migration verification failed.");File.Move(temp,target,false);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            if(SetupEngine.HashFile(target)!=hash||SetupEngine.HashFile(file)!=hash)throw new IOException("Migration source or destination changed.");
            File.Delete(file);records.Add(new{source=file,destination=target,sha256=hash});
        }
        Directory.CreateDirectory(destination);
        if(records.Count>0)File.WriteAllText(Path.Combine(destination,"legacy-import-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
        return records.Count;
        void Collect(string folder)
        {
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked migration directories are unsupported.");
            foreach(var file in Directory.GetFiles(folder))
            {if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked migration files are unsupported.");files.Add(file);}
            foreach(var child in Directory.GetDirectories(folder))Collect(child);
        }
    }
    public static void ImportLegacyForCurrentUser()
    {
        if(Isolated)return;
        ImportLegacy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TailsideWorkshopModLoader"),DirectoryPath);
    }
}

