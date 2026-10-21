using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil;

namespace TailsideWorkshopModLoader
{
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new string[0];
        public static void Patch(AssemblyDefinition assembly) { }
        private static readonly ManualLogSource Log = Logger.CreateLogSource("TailsideWorkshopModLoader");
        private static bool initialized;
        private static bool ran;
        private static readonly List<string> resolverDirectories = new List<string>();

        public static void Initialize()
        {
            if (!string.Equals(Paths.ProcessName, "Tailside", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                initialized = true;
                Log.LogInfo("TailsideWorkshopModLoader " + Catalog.LoaderVersion + " configured; runtime hook will be installed after preloader patching.");
            }
            catch (Exception e) { Log.LogError("Loader initialization failed; normal plugins remain available: " + e); }
        }

        public static void Finish()
        {
            if (!initialized) return;
            try
            {
                // Touching closed PluginInfo generics before preloader patching can resolve UnityEngine
                // too early. Install the discovery hook only after BepInEx has patched Unity assemblies.
                var method = typeof(TypeLoader).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(m => m.Name == "FindPluginTypes" && m.IsGenericMethodDefinition)
                    .MakeGenericMethod(typeof(PluginInfo));
                new Harmony("tailside.workshopmodloader.discovery").Patch(method,
                    postfix:new HarmonyMethod(typeof(Patcher), nameof(AfterDiscovery)));
                Log.LogInfo("TailsideWorkshopModLoader " + Catalog.LoaderVersion + " ready; discovery is startup-only. No polling or file copying.");
            }
            catch (Exception e) { Log.LogError("Loader runtime hook failed; normal plugins remain available: " + e); }
        }

        private static void Note(string message) { Log.LogInfo(message); }

        private static void AfterDiscovery(string __0, ref Dictionary<string, List<PluginInfo>> __result)
        {
            if (ran || !initialized || !string.Equals(Path.GetFullPath(__0), Path.GetFullPath(Paths.PluginPath), StringComparison.OrdinalIgnoreCase)) return;
            ran = true;
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Chainloader has created its manager, but has not attached plugins yet.
                // Apply the same flags as HideManagerGameObject for this process only.
                // Do not touch ConfigEntry.Value: that can automatically save BepInEx.cfg.
                var manager = Chainloader.ManagerObject;
                if (manager == null) throw new InvalidOperationException("BepInEx manager is unavailable; session protection was not applied.");
                manager.hideFlags |= UnityEngine.HideFlags.HideAndDontSave;
                Note("Applied session-only BepInEx manager protection before plugin creation. No configuration settings changed.");
                var packages = new List<Package>();
                var metadata = new Dictionary<string, PluginInfo>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var folder in SteamSubscriptions.Read(Paths.GameRootPath, Note))
                        Discover(folder.Path, "workshop", folder.Id, packages, metadata);
                }
                catch (Exception e) { Note("Workshop discovery unavailable: " + e.Message); }
                var merged = MergePackages(__result, packages, metadata);
                var accepted = packages.Where(x => merged.ContainsKey(x.Entry) && merged[x.Entry].Contains(metadata[x.Entry])).ToList();
                resolverDirectories.Clear();
                resolverDirectories.AddRange(accepted.Select(x => Path.GetDirectoryName(x.Entry)));
                if (accepted.Count > 0) AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;
                __result = merged;
                Note("Discovery complete: " + accepted.Count + " package(s); " + started.ElapsedMilliseconds + " ms. BepInEx will resolve dependencies and instantiate plugins.");
            }
            catch (Exception e) { Note("Loader discovery failed; normal plugins preserved: " + e); }
        }

        internal static Dictionary<string, List<PluginInfo>> MergePackages(Dictionary<string, List<PluginInfo>> original,
            List<Package> packages, Dictionary<string, PluginInfo> metadata)
        {
            var installed = new HashSet<string>(original.Values.SelectMany(x => x).Select(x => x.Metadata.GUID), StringComparer.OrdinalIgnoreCase);
            var selected = Catalog.Select(packages, installed, Note);
            // Work on a copy. If discovery fails, the original BepInEx result is preserved.
            var merged = original.ToDictionary(x => x.Key, x => new List<PluginInfo>(x.Value));
            foreach (var package in selected)
            {
                var info = metadata[package.Entry];
                merged[package.Entry] = new List<PluginInfo> { info };
                Note("Candidate " + info.Metadata.GUID + " " + info.Metadata.Version + " source=" + package.Source +
                     " workshop=" + package.WorkshopId + " path=" + package.Entry);
            }
            // Mono can unify assemblies with equal simple names even when their file paths differ.
            // Reject external collisions instead of executing a different DLL than the selected one.
            var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in merged.Where(x => x.Value.Count > 0))
            {
                var name = AssemblyName.GetAssemblyName(entry.Key).Name;
                if (!names.TryGetValue(name, out var paths)) names[name] = paths = new List<string>();
                paths.Add(entry.Key);
            }
            foreach (var conflict in names.Where(x => x.Value.Count > 1))
                foreach (var path in conflict.Value.Where(x => metadata.ContainsKey(x)))
                {
                    merged.Remove(path);
                    Note("Skipped external assembly-name collision: " + conflict.Key + " path=" + path);
                }
            return merged;
        }

        private static void Discover(string root, string source, ulong id, List<Package> packages, Dictionary<string, PluginInfo> metadata)
        {
            try
            {
                var package = Catalog.ReadPackage(root, source, id);
                if (package == null) return;
                metadata[package.Entry] = PackageInspector.Read(package);
                packages.Add(package);
            }
            catch (Exception e) { Note("Skipped package " + root + ": " + e.Message); }
        }

        private static Assembly ResolveDependency(object sender, ResolveEventArgs args)
        {
            // Scope support DLL lookup to the requesting package; never resolve arbitrary global names.
            if (args.RequestingAssembly == null) return null;
            var directory = Path.GetDirectoryName(args.RequestingAssembly.Location);
            if (!resolverDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase)) return null;
            var requested = new AssemblyName(args.Name);
            var path = Catalog.ContainedPath(directory, requested.Name + ".dll");
            if (!File.Exists(path)) return null;
            var actual = AssemblyName.GetAssemblyName(path);
            if (actual.FullName != requested.FullName) return null;
            return Assembly.LoadFrom(path);
        }
    }
}


