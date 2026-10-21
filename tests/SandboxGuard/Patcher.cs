using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Test fixture only. Never included in the setup payload or installed into the live game.
namespace SetupSandboxGuard
{
    public static class Patcher
    {
        private static string Sandbox
        {
            get
            {
                var root=Environment.GetEnvironmentVariable("MODSETUP_SANDBOX_ROOT");
                if(string.IsNullOrEmpty(root) || !Environment.GetCommandLineArgs().Contains("--bridge-smoke-test")) return null;
                return Path.GetFullPath(root).TrimEnd('\\').Equals(Path.GetFullPath(Paths.GameRootPath).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)?root:null;
            }
        }
        public static IEnumerable<string> TargetDLLs => Sandbox==null?new string[0]:new[]{"Assembly-CSharp.dll"};
        public static void Patch(AssemblyDefinition assembly)
        {
            var root=Sandbox;if(root==null)throw new InvalidOperationException("Sandbox guard is test-only.");
            var data=Path.Combine(root,"sandbox-user-data");Directory.CreateDirectory(data);
            int paths=0,prefs=0;
            foreach(var type in assembly.MainModule.GetTypes())
                foreach(var method in type.Methods.Where(m=>m.HasBody))
                    foreach(var instruction in method.Body.Instructions)
                    {
                        if(instruction.Operand is MethodReference call)
                        {
                            if(call.DeclaringType.FullName=="UnityEngine.Application" && call.Name=="get_persistentDataPath")
                            {instruction.OpCode=OpCodes.Ldstr;instruction.Operand=data;paths++;}
                            else if(call.DeclaringType.FullName=="UnityEngine.PlayerPrefs")
                            {
                                var signature=call.Parameters.Select(p=>p.ParameterType.FullName).ToArray();
                                var target=typeof(SandboxPrefs).GetMethods().Single(m=>m.Name==call.Name && m.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(signature));
                                instruction.Operand=assembly.MainModule.ImportReference(target);prefs++;
                            }
                        }
                    }
            if(paths==0 || prefs==0)throw new InvalidOperationException("No sandboxed paths/prefs were found.");
            if(Environment.GetEnvironmentVariable("MODSETUP_TRACE_STARTUP")=="1")
            {
                var trace=Path.Combine(root,"startup-trace.log");
                var append=assembly.MainModule.ImportReference(typeof(File).GetMethod("AppendAllText",new[]{typeof(string),typeof(string)}));
                foreach(var type in assembly.MainModule.GetTypes())
                    foreach(var method in type.Methods.Where(m=>m.HasBody && (m.Name=="Awake" || m.Name=="Start" || m.CustomAttributes.Any(a=>a.AttributeType.Name=="RuntimeInitializeOnLoadMethodAttribute"))))
                    {
                        var il=method.Body.GetILProcessor();
                        var first=method.Body.Instructions[0];
                        il.InsertBefore(first,il.Create(OpCodes.Ldstr,trace));
                        il.InsertBefore(first,il.Create(OpCodes.Ldstr,"ENTER "+method.FullName+"\n"));
                        il.InsertBefore(first,il.Create(OpCodes.Call,append));
                        foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
                        {
                            il.InsertBefore(ret,il.Create(OpCodes.Ldstr,trace));
                            il.InsertBefore(ret,il.Create(OpCodes.Ldstr,"LEAVE "+method.FullName+"\n"));
                            il.InsertBefore(ret,il.Create(OpCodes.Call,append));
                        }
                    }
            }
            Logger.CreateLogSource("SetupSandboxGuard").LogInfo("SANDBOX_ISOLATED paths="+paths+" prefs="+prefs+" data="+data);
        }
    }
    public static class SandboxPrefs
    {
        private static readonly Dictionary<string,object> Values=new Dictionary<string,object>();
        public static string GetString(string key)=>GetString(key,"");
        public static string GetString(string key,string fallback)=>Values.TryGetValue(key,out var value)?(string)value:fallback;
        public static int GetInt(string key)=>GetInt(key,0);
        public static int GetInt(string key,int fallback)=>Values.TryGetValue(key,out var value)?(int)value:fallback;
        public static float GetFloat(string key)=>GetFloat(key,0);
        public static float GetFloat(string key,float fallback)=>Values.TryGetValue(key,out var value)?(float)value:fallback;
        public static void SetString(string key,string value)=>Values[key]=value;
        public static void SetInt(string key,int value)=>Values[key]=value;
        public static void SetFloat(string key,float value)=>Values[key]=value;
        public static bool HasKey(string key)=>Values.ContainsKey(key);
        public static void DeleteKey(string key)=>Values.Remove(key);
        public static void DeleteAll()=>Values.Clear();
        public static void Save(){}
    }
}
