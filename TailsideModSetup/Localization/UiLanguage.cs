using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace ModManager;
public sealed record LanguageOption(string Code,string Name);
public static class UiLanguage
{
    private static readonly CultureInfo OsLanguage=CultureInfo.CurrentUICulture;
    private static readonly HashSet<string> KnownKeys=ReadCatalog();
    private static readonly Dictionary<string,Dictionary<string,string>> External=new(StringComparer.OrdinalIgnoreCase);
    public static List<string> Warnings {get;}=[];
    public static string Choice {get;private set;}="auto";
    public static List<LanguageOption> Options {get;private set;}=[];
    public static string Effective => Choice=="auto"?Resolve(OsLanguage.Name):Resolve(Choice);
    public static string Resolve(string code)
    {
        code=code.ToLowerInvariant();
        if(External.ContainsKey(code))return code;
        var parent=code.Split('-')[0];
        return External.ContainsKey(parent)?parent:"en";
    }
    public static string CultureName
    {
        get{try{return CultureInfo.GetCultureInfo(Effective).IetfLanguageTag;}catch{return "en-US";}}
    }
    private static HashSet<string> ReadCatalog(){using var stream=typeof(UiLanguage).Assembly.GetManifestResourceStream("ModManager.Strings.catalog.json")!;return JsonSerializer.Deserialize<HashSet<string>>(stream)!;}
    private static Dictionary<string,string> ReadEmbedded(string name)
    {
        using var stream=typeof(UiLanguage).Assembly.GetManifestResourceStream("ModManager.Strings."+name+".json")!;
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    private static string SettingsDirectory => ModEnvironmentSetup.Core.ToolStorage.DirectoryPath;
    private static string SettingsPath=>ModEnvironmentSetup.Core.ToolStorage.FilePath("data/settings.json");
    public static string TranslationDirectory=>ModEnvironmentSetup.Core.ToolStorage.FilePath("languages");
    private static Dictionary<string,string>? Active => External.GetValueOrDefault(Effective);
    public static string Get(string english)=>Active?.TryGetValue(english,out var value)==true?value:english;
    public static string ErrorText(string text)
    {
        var active=Active;if(active==null)return text;
        if(active.TryGetValue(text,out var value))return value;
        foreach(var entry in active.Where(x=>x.Key.EndsWith(": ")).OrderByDescending(x=>x.Key.Length))
            if(text.StartsWith(entry.Key,StringComparison.Ordinal))return entry.Value+ErrorText(text[entry.Key.Length..]);
        return text;
    }
    public static void Load(bool importLegacy = true)
    {
        External.Clear();Warnings.Clear();Choice="auto";
        if(importLegacy)try{ModEnvironmentSetup.Core.ToolStorage.ImportLegacyForCurrentUser();}catch(Exception e){Warnings.Add(e.Message);}
        Options=[new("auto","Auto (OS) / 自動")];
        var duplicates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if(Directory.Exists(TranslationDirectory))foreach(var file in Directory.GetFiles(TranslationDirectory,"*.json").Order(StringComparer.OrdinalIgnoreCase).Take(100))
            {
                try
                {
                    if(new FileInfo(file).Length>262144)throw new InvalidDataException("File exceeds 256 KiB.");
                    using var document=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions {MaxDepth=8});var json=document.RootElement;
                    if(json.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("Unsupported schemaVersion.");
                    var code=json.GetProperty("languageCode").GetString()!.ToLowerInvariant();
                    if(!Regex.IsMatch(code,"^[a-z]{2,3}(-[a-z0-9]{2,8})*$"))throw new InvalidDataException("Invalid languageCode.");
                    var name=json.GetProperty("displayName").GetString();
                    if(string.IsNullOrWhiteSpace(name)||name.Length>60||name.Any(char.IsControl))throw new InvalidDataException("Invalid displayName.");
                    var strings=json.GetProperty("translations");if(strings.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("translations must be an object.");
                    var values=new Dictionary<string,string>(StringComparer.Ordinal);
                    foreach(var entry in strings.EnumerateObject())
                    {
                        if(!KnownKeys.Contains(entry.Name))continue; // Unknown/future keys are ignored.
                        if(entry.Value.ValueKind!=JsonValueKind.String)continue;
                        var text=entry.Value.GetString();if(string.IsNullOrWhiteSpace(text)||text.Length>8192)continue;
                        // Validate formatting and retain exactly the same numbered placeholders.
                        try
                        {
                            var original=CompositeFormatItems(entry.Name);var translated=CompositeFormatItems(text);
                            if(!original.SetEquals(translated))continue;
                            _=string.Format(CultureInfo.InvariantCulture,text,new object[16]);
                        }catch(FormatException){continue;}
                        values[entry.Name]=text;
                    }
                    if(duplicates.Contains(code))continue;
                    if(External.ContainsKey(code)){External.Remove(code);Options.RemoveAll(x=>x.Code==code);duplicates.Add(code);throw new InvalidDataException("Duplicate languageCode; both packs ignored.");}
                    External[code]=values;Options.Add(new(code,name));
                }
                catch(Exception e){Warnings.Add(Path.GetFileName(file)+": "+e.Message);}
            }
        }catch(Exception e){Warnings.Add(e.Message);}
        if(!External.ContainsKey("en"))Options.Add(new("en","English (fallback)"));
        try
        {
            if(new FileInfo(SettingsPath) is {Exists:true,Length:<=4096})
            {
                using var json=JsonDocument.Parse(File.ReadAllText(SettingsPath));var value=json.RootElement.GetProperty("language").GetString();
                if(value=="auto"||Options.Any(x=>x.Code==value))Choice=value!;
            }
        }catch {Choice="auto";}
    }
    private static HashSet<int> CompositeFormatItems(string value)=>Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[,}:])").Select(x=>int.Parse(x.Groups[1].Value,CultureInfo.InvariantCulture)).ToHashSet();
    public static void Select(string value, bool persist = true)
    {
        if(!Options.Any(x=>x.Code==value))throw new ArgumentException("Unsupported UI language.");
        if(!persist){Choice=value;return;}
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);var temp=SettingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new{language=value}));File.Move(temp,SettingsPath,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        Choice=value;
    }
    public static void PrepareTranslationFolder()=>Directory.CreateDirectory(TranslationDirectory);
    public static void Apply(Window window)
    {
        var keys=ReadEmbedded("keys");
        foreach(var item in keys){window.Resources[item.Value]=Get(item.Key);Application.Current.Resources[item.Value]=Get(item.Key);}
    }
}

