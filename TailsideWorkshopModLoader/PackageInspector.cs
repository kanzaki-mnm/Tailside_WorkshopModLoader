using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;

namespace TailsideWorkshopModLoader
{
    internal static class PackageInspector
    {
        internal static PluginInfo Read(Package package)
        {
            // Own both the search paths and Cecil's assembly cache for this inspection.
            // Never lend an untrusted package directory to TypeLoader's shared resolver.
            using (var resolver = new DefaultAssemblyResolver())
            {
                foreach (var path in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(path);
                resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(BaseUnityPlugin).Assembly.Location));
                // BepInEx loads patched Unity assemblies from memory: Assembly.Location can be empty.
                // The configured Managed directory remains the on-disk metadata source.
                resolver.AddSearchDirectory(Paths.ManagedPath);
                resolver.AddSearchDirectory(Path.GetDirectoryName(package.Entry));
                var parameters = new ReaderParameters { AssemblyResolver = resolver };
                using (var assembly = AssemblyDefinition.ReadAssembly(package.Entry, parameters))
                {
                    var plugins = assembly.MainModule.Types.Select(Chainloader.ToPluginInfo).Where(x => x != null).ToList();
                    if (plugins.Count != 1) throw new InvalidDataException("Entry assembly must declare exactly one BepInEx plugin.");
                    var plugin = plugins[0];
                    if (plugin.Metadata.GUID != package.Manifest.Id || plugin.Metadata.Version != Version.Parse(package.Manifest.Version))
                        throw new InvalidDataException("Manifest id/version does not match BepInPlugin metadata.");
                    var targetVersion = assembly.MainModule.AssemblyReferences.First(x => x.Name == "BepInEx").Version;
                    if (targetVersion.Major != 5 || targetVersion > typeof(BaseUnityPlugin).Assembly.GetName().Version)
                        throw new InvalidDataException("Plugin requires an incompatible BepInEx version.");
                    return plugin;
                }
            }
        }
    }
}
