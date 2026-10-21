using System.Diagnostics;
using System.Reflection;
using ModEnvironmentSetup.Core;

if(args.Length!=3)throw new ArgumentException("install|inspect|repair|remove copy-root app-dll");
var root=Path.GetFullPath(args[1]);
// Do not expose this test runner as a generic installer or allow the real Steam installation.
if(!File.Exists(Path.Combine(root,".modsetup-sandbox")) || root.Contains("steamapps",StringComparison.OrdinalIgnoreCase))
 throw new InvalidOperationException("Only the explicitly marked isolated copy is permitted.");
var app=Assembly.LoadFrom(Path.GetFullPath(args[2]));
using var resource=app.GetManifestResourceStream("Setup.Payload.zip")!;
var engine=new SetupEngine(SetupEngine.LoadPayload(resource),()=>Process.GetProcessesByName("Tailside").Length>0);
Console.WriteLine("Before: "+engine.Inspect(root));
switch(args[0]){case "install":case "repair":engine.Install(root);break;case "remove":engine.Remove(root);break;case "inspect":break;default:throw new ArgumentException("Unknown action.");}
Console.WriteLine("After: "+engine.Inspect(root));
