using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace WorkshopBridge
{
    [DataContract]
    public sealed class Manifest
    {
        [DataMember(Name="schemaVersion", IsRequired=true)] public int Schema;
        // JSON may explicitly contain null even for an IsRequired member.
        // Keep that wire state nullable and reject it when accessing a required value.
        [DataMember(Name="id", IsRequired=true)] private string? id;
        [DataMember(Name="name", IsRequired=true)] private string? name;
        [DataMember(Name="version", IsRequired=true)] private string? version;
        [DataMember(Name="gameAppId", IsRequired=true)] public uint AppId;
        [DataMember(Name="entryAssembly", IsRequired=true)] private string? entry;
        [DataMember(Name="minimumBridgeVersion", IsRequired=true)] private string? minimumBridge;

        public string Id { get => Required(id, "id"); set => id = value; }
        public string Name { get => Required(name, "name"); set => name = value; }
        public string Version { get => Required(version, "version"); set => version = value; }
        public string Entry { get => Required(entry, "entryAssembly"); set => entry = value; }
        public string MinimumBridge { get => Required(minimumBridge, "minimumBridgeVersion"); set => minimumBridge = value; }

        private static string Required(string? value, string member) =>
            value ?? throw new InvalidDataException("Manifest member cannot be null: " + member);
    }

    public sealed class Package
    {
        public Manifest Manifest { get; }
        public string Root { get; }
        public string Entry { get; }
        public string Source { get; }
        public ulong WorkshopId { get; }

        public Package(Manifest manifest, string root, string entry, string source, ulong workshopId)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            WorkshopId = workshopId;
        }
    }

    public static class Catalog
    {
        public const uint AppId = 2922570;
        public static readonly Version BridgeVersion = new Version(0,2,0);

        public static T ReadJson<T>(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                if (new DataContractJsonSerializer(typeof(T)).ReadObject(stream) is not T value)
                    throw new InvalidDataException("Expected a non-null JSON value for " + typeof(T).Name + ".");
                return value;
            }
        }

        public static void WriteJson<T>(string file, T value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            var destination = Path.GetFullPath(file);
            var parent = Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("Expected a file path with a parent directory.");
            Directory.CreateDirectory(parent);
            using (var stream = File.Create(destination))
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }

        // Reject links as well as lexical escapes. Only ordinary files inside a package are read.
        public static string ContainedPath(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("Expected a relative package path.");
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Path escapes package root.");
            for (var current = full; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked package paths are unsupported.");
            }
            return full;
        }

        public static Package? ReadPackage(string root, string source, ulong workshopId = 0)
        {
            var path = ContainedPath(root, "mod.json");
            if (!File.Exists(path)) return null; // ordinary player skins are not code packages
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Manifest exceeds 64 KiB.");
            var manifest = ReadJson<Manifest>(path);
            if (manifest.Schema != 1 || manifest.AppId != AppId)
                throw new InvalidDataException("Unsupported schema or gameAppId.");
            if (!Regex.IsMatch(manifest.Id, @"^[A-Za-z0-9._-]{1,160}$") ||
                string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("Invalid mod identity.");
            if (!Version.TryParse(manifest.Version, out _) ||
                !Version.TryParse(manifest.MinimumBridge, out var minimum) || minimum > BridgeVersion)
                throw new InvalidDataException("Invalid version or a newer bridge is required.");
            var entry = ContainedPath(root, manifest.Entry);
            var plugins = Path.GetFullPath(Path.Combine(root, "plugins")) + Path.DirectorySeparatorChar;
            if (!entry.StartsWith(plugins, StringComparison.OrdinalIgnoreCase) ||
                !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
                throw new InvalidDataException("entryAssembly must be an existing DLL under plugins/.");
            return new Package(manifest, Path.GetFullPath(root), entry, source, workshopId);
        }

        // Resolve identities before executing any assembly. Ambiguous sources fail closed.
        public static List<Package> Select(IEnumerable<Package> packages, HashSet<string> installed,
            HashSet<string> localOverrides, Action<string> log)
        {
            var result = new List<Package>();
            foreach (var group in packages.GroupBy(p => p.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var all = group.ToList();
                var locals = all.Where(p => p.Source == "local").ToList();
                if (localOverrides.Contains(group.Key) && locals.Count == 1)
                {
                    result.Add(locals[0]);
                    log("Explicit local override: " + group.Key);
                }
                else if (installed.Contains(group.Key)) log("Skipped duplicate of manual plugin: " + group.Key);
                else if (all.Count != 1) log("Skipped ambiguous duplicate packages: " + group.Key);
                else result.Add(all[0]);
            }
            return result;
        }
    }
}
