using System;
using System.Runtime.CompilerServices;
using BepInEx;
#if A
#if REVERSE
[BepInPlugin("collision.z", "Collision A", "1.0.0")]
#else
[BepInPlugin("collision.a", "Collision A", "1.0.0")]
#endif
#else
[BepInPlugin("collision.b", "Collision B", "1.0.0")]
#endif
public sealed class CollisionMod : BaseUnityPlugin {
 private void Awake() {
  try { Logger.LogInfo("COLLISION_OK " + Call()); }
  catch(Exception e) { Logger.LogError("COLLISION_FAIL " + e.GetType().Name + " " + e.Message); }
  foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
   if(a.GetName().Name=="CollisionHelper") Logger.LogInfo("COLLISION_LOADED " + a.FullName + " path=" + a.Location);
 }
 [MethodImpl(MethodImplOptions.NoInlining)] private static string Call() {
#if A
 return CollisionHelper.AOnly();
#else
 return CollisionHelper.BOnly();
#endif
 }
}
