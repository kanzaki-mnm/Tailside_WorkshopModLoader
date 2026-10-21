using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TailsideWorkshopModLoader;

var root = Path.Combine(Path.GetTempPath(), "tailside-metadata-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
Check(Catalog.LoaderVersion.ToString()==typeof(Patcher).Assembly.GetName().Version.ToString(3),"production loader compatibility and assembly versions agree");
Check(Catalog.LoaderVersion==new Version(1,0,0),"production first public release is 1.0.0");
var source=(BepInEx.Logging.ManualLogSource)typeof(Patcher).GetField("Log",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
var recorded=new List<string>();
source.LogEvent += (_,e)=>recorded.Add(e.Data.ToString());
var note=typeof(Patcher).GetMethod("Note",BindingFlags.NonPublic|BindingFlags.Static);
note.Invoke(null,new object[]{"standard BepInEx log probe"});
Check(recorded.Contains("standard BepInEx log probe"),"production diagnostics use BepInEx log source");
Check(typeof(Patcher).GetMethod("WriteReport",BindingFlags.NonPublic|BindingFlags.Static)==null,"loader has no independent report file writer");
var inspect = typeof(Patcher).Assembly.GetType("TailsideWorkshopModLoader.PackageInspector").GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static);
PluginInfo Inspect(Package p)
{
    try { return (PluginInfo)inspect.Invoke(null, new object[] { p }); }
    catch (TargetInvocationException e) { throw e.InnerException; }
}
void Reject(Package p, Type expected, string label)
{
    try { Inspect(p); }
    catch (Exception e) when (expected.IsInstanceOfType(e)) { Check(true, label); return; }
    throw new Exception("Accepted: " + label);
}
// Generate real managed DLLs without loading their code into the test process.
void AddConstructor(TypeDefinition type, ModuleDefinition module)
{
    var ctor = new MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName |
        Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
    var parent = new MethodReference(".ctor", module.TypeSystem.Void, type.BaseType) { HasThis = true };
    ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, parent));
    ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    type.Methods.Add(ctor);
}
Package Fixture(string name, string helper = null, bool pluginBase = true, int count = 1, int bepMajor = 5, string assemblyName = null)
{
    var folder = Path.Combine(root, name);
    Directory.CreateDirectory(Path.Combine(folder, "plugins"));
    var entry = Path.Combine(folder, "plugins", name + ".dll");
    using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName ?? name, new Version(1,0,0)), name, ModuleKind.Dll);
    var module = a.MainModule;
    var bep = new AssemblyNameReference("BepInEx", new Version(bepMajor,4,23,5));
    module.AssemblyReferences.Add(bep);
    TypeReference baseType = new TypeReference("BepInEx", "BaseUnityPlugin", module, bep);
    if (helper != null)
    {
        using var h = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(helper, new Version(1,0,0)), helper, ModuleKind.Dll);
        h.MainModule.AssemblyReferences.Add(bep);
        var hb = pluginBase ? new TypeReference("BepInEx", "BaseUnityPlugin", h.MainModule, bep) : h.MainModule.TypeSystem.Object;
        var helperType = new TypeDefinition("Fixture", "SharedBase", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract, hb);
        AddConstructor(helperType, h.MainModule);
        h.MainModule.Types.Add(helperType);
        h.Write(Path.Combine(folder, "plugins", helper + ".dll"));
        var hr = new AssemblyNameReference(helper, new Version(1,0,0));
        module.AssemblyReferences.Add(hr);
        baseType = new TypeReference("Fixture", "SharedBase", module, hr);
    }
    for (int i = 0; i < count; i++)
    {
        var t = new TypeDefinition("Fixture", "Plugin" + i, Mono.Cecil.TypeAttributes.Public, baseType);
        var attr = new CustomAttribute(module.ImportReference(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) })));
        foreach (var value in new[] { "fixture." + name, name, "1.0.0" }) attr.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
        t.CustomAttributes.Add(attr);
        AddConstructor(t, module);
        module.Types.Add(t);
    }
    a.Write(entry);
    var m = new Manifest { Schema = 1, AppId = Catalog.AppId, Id = "fixture." + name, Name = name, Version = "1.0.0", Entry = "plugins/" + name + ".dll", MinimumLoader = "0.1.0" };
    Catalog.WriteJson(Path.Combine(folder, "mod.json"), m);
    return Catalog.ReadPackage(folder, "workshop", 123);
}
try
{
    Directory.CreateDirectory(Path.Combine(root, "BepInEx", "config"));
    typeof(Paths).GetMethod("SetExecutablePath", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,
        new object[] { Path.Combine(root, "Tailside.exe"), null, AppContext.BaseDirectory, null });
    var before = TypeLoader.Resolver.GetSearchDirectories();
    var good = Fixture("Good");
    Check(Inspect(good).Metadata.GUID == good.Manifest.Id, "real plugin metadata accepted");
    good.Manifest.Id = "wrong.id";
    Reject(good, typeof(InvalidDataException), "manifest mismatch rejected");
    Reject(Fixture("Empty", count:0), typeof(InvalidDataException), "no plugin rejected");
    Reject(Fixture("Multiple", count:2), typeof(InvalidDataException), "multiple plugins rejected");
    Reject(Fixture("Future", bepMajor:6), typeof(InvalidDataException), "incompatible BepInEx rejected");
    var broken = Fixture("Broken");
    File.WriteAllBytes(broken.Entry, new byte[] { 0 });
    Reject(broken, typeof(BadImageFormatException), "broken DLL rejected");
    var bad = Fixture("BadHelper", "SameHelper", false);
    var valid = Fixture("ValidHelper", "SameHelper", true);
    Reject(bad, typeof(InvalidDataException), "non-plugin helper rejected");
    Check(Inspect(valid).Metadata.GUID == valid.Manifest.Id, "rejected helper cannot poison following package");
    Reject(bad, typeof(InvalidDataException), "accepted helper cannot make rejected package valid");
    Check(Inspect(valid).Metadata.GUID == valid.Manifest.Id, "same-name helper cache isolated on repeated inspection");
    File.Delete(Path.Combine(valid.Root, "plugins", "SameHelper.dll"));
    Reject(valid, typeof(InvalidDataException), "missing helper cannot reuse previous package cache");
    Check(before.SequenceEqual(TypeLoader.Resolver.GetSearchDirectories()), "shared resolver search directories unchanged");
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SameHelper" || a.GetName().Name == "Good"), "metadata inspection does not execute or load fixture assemblies");
    var manual = Fixture("Manual");
    var manualInfo = Inspect(manual);
    var original = new Dictionary<string,List<PluginInfo>> { [manual.Entry] = new() { manualInfo } };
    var merge = typeof(Patcher).GetMethod("MergePackages", BindingFlags.NonPublic | BindingFlags.Static);
    Dictionary<string,List<PluginInfo>> Merge(params Package[] ps) => (Dictionary<string,List<PluginInfo>>)merge.Invoke(null,
        new object[] { original, ps.ToList(), ps.ToDictionary(p => p.Entry, Inspect) });
    var workshop = Fixture("Workshop");
    Check(Merge(workshop).Count == 2, "real workshop plugin merged alongside manual plugin");
    Check(original.Count == 1 && original[manual.Entry].Single() == manualInfo, "normal plugin discovery result not mutated");
    Check(Merge(manual).Count == 1, "manual GUID takes precedence");
    var duplicate = Fixture("Duplicate");
    // A second Workshop item may contain an identical mod under a different path.
    File.Copy(workshop.Entry, duplicate.Entry, true);
    duplicate.Manifest.Id = workshop.Manifest.Id;
    Check(Merge(workshop, duplicate).Count == 1, "duplicate workshop GUIDs skipped without losing manual plugin");
    var collision = Fixture("Collision", assemblyName:"Manual");
    Check(Merge(collision).Count == 1, "entry assembly collision cannot replace normal plugin");
    var accepted = new List<Package>();
    var metadata = new Dictionary<string,PluginInfo>(StringComparer.OrdinalIgnoreCase);
    var discover = typeof(Patcher).GetMethod("Discover", BindingFlags.NonPublic | BindingFlags.Static);
    foreach (var p in new[] { broken, workshop })
        discover.Invoke(null, new object[] { p.Root, "workshop", (ulong)123, accepted, metadata });
    Check(accepted.Count == 1 && accepted[0].Entry == workshop.Entry, "production discovery skips broken DLL and continues with healthy package");
    Check(before.SequenceEqual(TypeLoader.Resolver.GetSearchDirectories()), "production discovery and merge leave shared search paths unchanged");
    Check(Merge().Count == 1, "removed workshop packages leave normal plugin result intact");
    Console.WriteLine("Passed " + passed + " real-DLL checks.");
}
finally { Directory.Delete(root, true); }
