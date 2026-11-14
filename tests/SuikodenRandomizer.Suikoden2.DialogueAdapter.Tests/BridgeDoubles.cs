using System;
using SuikodenRandomizer.Shared;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        internal HarmonyPatch(Type declaringType, string methodName) : this(declaringType, methodName, Type.EmptyTypes) { }
        internal HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes)
        { DeclaringType = declaringType; MethodName = methodName; ArgumentTypes = argumentTypes; }
        internal Type DeclaringType { get; }
        internal string MethodName { get; }
        internal Type[] ArgumentTypes { get; }
    }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
}

namespace SuikodenRandomizer.Unified
{
    internal sealed record NativeDialogueContext(string Game, Func<bool> IsCurrent,
        Func<int, string?> ResolveName, Func<int?> MaxUtf16Length, Func<int, bool>? ShouldReplaceRole = null);

    internal static class DialogueNative
    {
        [ThreadStatic] internal static NativeDialogueContext? Current;
        [ThreadStatic] internal static int NameDepth;
        internal static int EnterCalls;
        internal static ActiveGame LastEnteredGame;
        internal static bool ThrowEnter, ThrowDispose, ThrowSuppress;
        internal static Action? DuringSuppress;

        internal static IDisposable Enter(ActiveGame game, Func<NativeDialogueContext?> capture)
        {
            EnterCalls++; LastEnteredGame = game;
            if (ThrowEnter) throw new InvalidOperationException("injected scope entry failure");
            var previous = Current; Current = capture();
            return new Scope(() => Current = previous);
        }
        internal static IDisposable SuppressNames()
        {
            if (ThrowSuppress) throw new InvalidOperationException("injected suppression failure");
            DuringSuppress?.Invoke(); NameDepth++;
            return new Scope(() => NameDepth--);
        }
        private sealed class Scope : IDisposable
        {
            private Action? _leave;
            internal Scope(Action leave) => _leave = leave;
            public void Dispose()
            {
                var leave = _leave; _leave = null; leave?.Invoke();
                if (ThrowDispose) throw new InvalidOperationException("injected scope dispose failure");
            }
        }
    }
}
