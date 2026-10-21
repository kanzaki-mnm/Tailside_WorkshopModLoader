using System.Text.RegularExpressions;

namespace ModEnvironmentSetup.Core;

public static class SteamDiscovery
{
    // Steam VDF is a token-based nested key/value format, not JSON. Preserve escaped paths.
    public static Dictionary<string, object> ParseVdf(string text)
    {
        var tokens = Regex.Matches(text, "\"(?:\\\\.|[^\"\\\\])*\"|[{}]|//[^\\r\\n]*")
            .Select(m => m.Value).Where(s => !s.StartsWith("//")).ToArray();
        int index = 0;
        string Unquote(string s) => s[1..^1].Replace("\\\\", "\\").Replace("\\\"", "\"");
        Dictionary<string, object> Read(bool nested)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (index < tokens.Length)
            {
                if (tokens[index] == "}") { index++; if (!nested) throw new InvalidDataException("Unexpected VDF brace."); return result; }
                var key = tokens[index++];
                if (!key.StartsWith('"') || index == tokens.Length) throw new InvalidDataException("Invalid VDF key.");
                var value = tokens[index++];
                result[Unquote(key)] = value == "{" ? Read(true) : value.StartsWith('"') ? Unquote(value) : throw new InvalidDataException("Invalid VDF value.");
            }
            if (nested) throw new InvalidDataException("Unclosed VDF object.");
            return result;
        }
        return Read(false);
    }

    public static IReadOnlyList<string> Find(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(Path.GetFullPath(root));
            try
            {
                var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(file)) file = Path.Combine(root, "config", "libraryfolders.vdf");
                var data = ParseVdf(File.ReadAllText(file));
                if (data.GetValueOrDefault("libraryfolders") is Dictionary<string, object> entries)
                    foreach (var entry in entries)
                        if (uint.TryParse(entry.Key, out _))
                        {
                            var path = entry.Value is string oldPath ? oldPath :
                                (entry.Value as Dictionary<string, object>)?.GetValueOrDefault("path") as string;
                            if (!string.IsNullOrWhiteSpace(path)) libraries.Add(Path.GetFullPath(path));
                        }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        }
        foreach (var library in libraries)
        {
            try
            {
                var manifest = Path.Combine(library, "steamapps", "appmanifest_2922570.acf");
                if (!File.Exists(manifest)) continue;
                var data = ParseVdf(File.ReadAllText(manifest));
                if (data.GetValueOrDefault("AppState") is not Dictionary<string, object> app ||
                    app.GetValueOrDefault("appid") as string != "2922570" ||
                    app.GetValueOrDefault("installdir") is not string name || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..") continue;
                var game = Path.GetFullPath(Path.Combine(library, "steamapps", "common", name));
                if (GameFolder.IsValid(game)) games.Add(game);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        }
        return games.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public static class GameFolder
{
    public static bool IsValid(string root)
    {
        try
        {
            var exe = Path.Combine(root, "Tailside.exe");
            if (!File.Exists(exe) || !Directory.Exists(Path.Combine(root, "Tailside_Data", "Managed"))) return false;
            using var reader = new BinaryReader(File.OpenRead(exe));
            if (reader.ReadUInt16() != 0x5a4d) return false;
            reader.BaseStream.Position = 0x3c;
            int offset = reader.ReadInt32();
            if (offset < 64 || offset > reader.BaseStream.Length - 6) return false;
            reader.BaseStream.Position = offset;
            return reader.ReadUInt32() == 0x4550 && reader.ReadUInt16() == 0x8664;
        }
        catch { return false; }
    }
}
