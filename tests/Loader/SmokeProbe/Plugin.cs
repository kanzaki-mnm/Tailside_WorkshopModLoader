using BepInEx;
[BepInPlugin("local.workshopbridge.smoke", "Bridge Smoke Probe", SmokeBuild.Version)]
public sealed class BridgeSmokeProbe : BaseUnityPlugin
{
    private void Awake() => Logger.LogInfo("BRIDGE_SMOKE_OK version=" + SmokeBuild.Version + " location=" + GetType().Assembly.Location);
}
internal static class SmokeBuild
{
#if PROBE_V2
    public const string Version = "1.0.1";
#else
    public const string Version = "1.0.0";
#endif
}
