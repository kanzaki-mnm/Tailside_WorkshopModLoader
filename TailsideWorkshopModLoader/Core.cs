using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace TailsideWorkshopModLoader
{
    [DataContract]
    public sealed class Manifest
    {
        [DataMember(Name="schemaVersion", IsRequired=true)] public int Schema;
        [DataMember(Name="id", IsRequired=true)] public string Id;
        [DataMember(Name="name", IsRequired=true)] public string Name;
        [DataMember(Name="version", IsRequired=true)] public string Version;
        [DataMember(Name="gameAppId", IsRequired=true)] public uint AppId;
        [DataMember(Name="entryAssembly", IsRequired=true)] public string Entry;
        [DataMember(Name="minimumLoaderVersion", IsRequired=true)] public string MinimumLoader;
    }

    public sealed class Package
    {
        public Manifest Manifest;
        public string Root;
        public string Entry;
        public string Source;
        public ulong WorkshopId;
    }

    public static class Catalog
    {
        public const uint AppId = 2922570;
        public static readonly Version LoaderVersion = Version.Parse(typeof(Catalog).Assembly.GetName().Version.ToString(3));

        public static T ReadJson<T>(string file)
        {
            using (var stream = File.OpenRead(file))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        public static void WriteJson<T>(string file, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using (var stream = File.Create(file))
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }

        // Reject links as well as lexical escapes. Only ordinary files inside a package are read.
        public static string ContainedPath(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("Expected a relative package path.");
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            // A sibling whose case differs can be a distinct directory on a Proton host.
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Path escapes package root.");
            for (var current = full; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked package paths are unsupported.");
            }
            return full;
        }

        public static Package ReadPackage(string root, string source, ulong workshopId = 0)
        {
            var path = ContainedPath(root, "mod.json");
            if (!File.Exists(path)) return null; // ordinary player skins are not code packages
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Manifest exceeds 64 KiB.");
            var manifest = ReadJson<Manifest>(path);
            if (manifest.Schema != 1 || manifest.AppId != AppId)
                throw new InvalidDataException("Unsupported schema or gameAppId.");
            if (manifest.Id == null || !Regex.IsMatch(manifest.Id, @"^[A-Za-z0-9._-]{1,160}$") ||
                string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("Invalid mod identity.");
            if (!Version.TryParse(manifest.Version, out _) ||
                !Version.TryParse(manifest.MinimumLoader, out var minimum) || minimum > LoaderVersion)
                throw new InvalidDataException("Invalid version or a newer loader is required.");
            var entry = ContainedPath(root, manifest.Entry);
            var plugins = Path.GetFullPath(Path.Combine(root, "plugins")) + Path.DirectorySeparatorChar;
            if (!entry.StartsWith(plugins, StringComparison.OrdinalIgnoreCase) ||
                !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
                throw new InvalidDataException("entryAssembly must be an existing DLL under plugins/.");
            return new Package { Manifest=manifest, Root=Path.GetFullPath(root), Entry=entry,
                Source=source, WorkshopId=workshopId };
        }

        // Resolve identities before executing any assembly. Ambiguous sources fail closed.
        public static List<Package> Select(IEnumerable<Package> packages, HashSet<string> installed,
            Action<string> log)
        {
            var result = new List<Package>();
            foreach (var group in packages.Where(p => p.Source == "workshop" && p.WorkshopId != 0).GroupBy(p => p.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var all = group.ToList();
                if (installed.Contains(group.Key)) log("Skipped duplicate of manual plugin: " + group.Key);
                else if (all.Count != 1) log("Skipped ambiguous duplicate packages: " + group.Key);
                else result.Add(all[0]);
            }
            return result;
        }
    }
}
