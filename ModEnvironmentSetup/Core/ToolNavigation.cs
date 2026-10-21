namespace ModEnvironmentSetup.Core;
public static class ToolNavigation
{
    public static Action<string>? OpenSetup {get;set;}
    public static Action<string>? OpenManager {get;set;}
    public static Action<string>? EnvironmentInstalled {get;set;}
    public static Func<string,IReadOnlyList<string>>? RemoveEnvironment {get;set;}
    public static Func<string,Inspection>? Inspect {get;set;}
    public static Func<string,string>? Describe {get;set;}
    public static Func<string,bool>? NeedsToolCopy {get;set;}
    public static string Version {get;set;}="0.2.3";
}
