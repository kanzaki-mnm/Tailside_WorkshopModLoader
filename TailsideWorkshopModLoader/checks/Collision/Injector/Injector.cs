using System;
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Mono.Cecil;
using TailsideWorkshopModLoader;
public static class CollisionTestInjector {
 public static IEnumerable<string> TargetDLLs => new string[0];
 public static void Patch(AssemblyDefinition a) {}
 public static void Finish() {
  if(!File.Exists(Path.Combine(Paths.GameRootPath,".modsetup-sandbox")) || Environment.GetEnvironmentVariable("COLLISION_TEST")!="1") return;
  var h = new Harmony("collision.test.only");
  if(Environment.GetEnvironmentVariable("COLLISION_TEST_LOCATION_FALLBACK")=="1") h.Patch(typeof(Patcher).Assembly.GetType("TailsideWorkshopModLoader.PackageInspector").GetMethod("Read", BindingFlags.NonPublic|BindingFlags.Static),transpiler:new HarmonyMethod(typeof(CollisionTestInjector),nameof(LocationFallback)));
  h.Patch(typeof(SteamSubscriptions).GetMethod("Read"),prefix:new HarmonyMethod(typeof(CollisionTestInjector),nameof(Items)));
  var inspector=typeof(Patcher).Assembly.GetType("TailsideWorkshopModLoader.PackageInspector").GetMethod("Read", BindingFlags.NonPublic|BindingFlags.Static);
  var patches=Harmony.GetPatchInfo(inspector);
  var unity=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name=="UnityEngine.CoreModule");
  File.WriteAllLines(Path.Combine(Paths.ConfigPath,"CollisionTest.diagnostics.log"),new[]{
   "LocationFallback="+(Environment.GetEnvironmentVariable("COLLISION_TEST_LOCATION_FALLBACK")=="1"),
   "InspectorPatched="+(patches!=null && (patches.Prefixes.Count+patches.Postfixes.Count+patches.Transpilers.Count+patches.Finalizers.Count)>0),
   "UnityLocationEmpty="+(unity!=null && string.IsNullOrEmpty(unity.Location)),
   "ManagedPath="+Paths.ManagedPath});
 }
 private static bool Items(ref List<WorkshopFolder> __result) {
  __result=new List<WorkshopFolder>(); ulong id=100;
  foreach(var p in File.ReadAllLines(Path.Combine(Paths.GameRootPath,".collision-items"))) __result.Add(new WorkshopFolder {Id=id++,Path=p});
  return false;
 }
 private static IEnumerable<CodeInstruction> LocationFallback(IEnumerable<CodeInstruction> instructions) {
  foreach(var i in instructions) {
   if(i.operand is MethodInfo m && m == typeof(Assembly).GetProperty("Location").GetGetMethod()) {
    i.opcode=OpCodes.Call; i.operand=typeof(CollisionTestInjector).GetMethod(nameof(DiskLocation),BindingFlags.Static|BindingFlags.NonPublic);
   }
   yield return i;
  }
 }
 private static string DiskLocation(Assembly a) {
  return string.IsNullOrEmpty(a.Location) ? Path.Combine(Paths.ManagedPath,a.GetName().Name+".dll") : a.Location;
 }}
