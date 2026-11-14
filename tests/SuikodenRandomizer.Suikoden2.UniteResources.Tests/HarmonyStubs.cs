using System;
namespace HarmonyLib;
[AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch : Attribute
{ public HarmonyPatch(Type type,string method) { } public HarmonyPatch(Type type,string method,Type[] arguments) { } }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
