using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string method) { Type = type; Method = method; }
        public Type? Type { get; }
        public string? Method { get; }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyTargetMethods : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyTargetMethod : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrepare : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method){methodInfo=method;}public MethodInfo methodInfo; }
    public sealed class Patch { public MethodInfo PatchMethod {get;init;}=null!; }
    public sealed class Patches { public List<Patch> Prefixes {get;}=new List<Patch>(); }
    public sealed class Harmony
    {
        private static readonly Dictionary<MethodBase,Patches> Registry=new Dictionary<MethodBase,Patches>();
        public Harmony(string id){}
        public static Patches? GetPatchInfo(MethodBase target)=>Registry.TryGetValue(target,out var value)?value:null;
        public void Patch(MethodBase target,HarmonyMethod? prefix=null)
        {
            if(!Registry.TryGetValue(target,out var patches))Registry[target]=patches=new Patches();
            if(prefix is not null)patches.Prefixes.Add(new Patch{PatchMethod=prefix.methodInfo});
        }
        public static void Clear()=>Registry.Clear();
        public static void Remove(MethodBase target)=>Registry.Remove(target);
    }
    public static class AccessTools
    {
        public static MethodInfo? Method(Type type, string name) => type.GetMethod(name);
        public static MethodInfo? Method(Type type,string name,Type[] args)=>type.GetMethod(name,args);
    }
}

namespace SuikodenRandomizer.Suikoden2Plugin
{
    internal sealed class Gsd2Runtime
    {
        internal static Gsd2Runtime? Current {get;set;}
        internal bool Owns {get;set;}=true;
        internal bool Active {get;set;}=true;
        internal bool OwnsExecution=>Owns;
        internal long ExecutionEpoch {get;set;}=1;
        internal bool MatchesExecution(long epoch)=>Owns&&epoch==ExecutionEpoch;
        internal bool IsActive=>OwnsExecution&&Active&&FaultReason is null;
        internal string? FaultReason {get;private set;}
        internal Dictionary<int,int> Mapping {get;}=new Dictionary<int,int>();
        internal List<string> Logs {get;}=new List<string>();
        internal int MapDisplayId(int canonical)=>Mapping.TryGetValue(canonical,out int donor)?donor:canonical;
        internal void Trace(string message)=>Logs.Add(message);
        internal void Fail(string reason){FaultReason=reason;Logs.Add(reason);}
    }
}

namespace SuikodenRandomizer.Suikoden2Plugin
{
    internal static class Gsd2Graphics {internal static Func<bool> PreserveRawResourceIds {get;set;}=()=>false;}
    internal static class Gsd2Presentation {internal static Func<bool> PreserveRawResourceIds {get;set;}=()=>false;}
}
