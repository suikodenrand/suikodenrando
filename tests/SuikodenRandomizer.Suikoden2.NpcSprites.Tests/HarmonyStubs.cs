using System;
namespace HarmonyLib;
[AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch : Attribute
{
    internal HarmonyPatch(Type type, string name, Type[] parameters) { }
}
[AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
