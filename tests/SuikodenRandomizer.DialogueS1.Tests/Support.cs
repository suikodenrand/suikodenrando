extern alias GSD1;
using SuikodenRandomizer.Shared;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        internal Type DeclaringType { get; }
        internal string MethodName { get; }
        internal Type[] Arguments { get; }
        public HarmonyPatch(Type type, string method, Type[] arguments) { DeclaringType = type; MethodName = method; Arguments = arguments; }
    }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPriority : Attribute
    { internal int Value { get; } public HarmonyPriority(int value) { Value = value; } }
}
namespace SuikodenRandomizer.Unified
{
    // Only a bridge contract double. Root tests the actual shared bridge separately.
    internal sealed class NativeDialogueContext
    {
        internal string Game { get; }
        internal Func<bool> IsCurrent { get; }
        internal Func<int, string?> ResolveName { get; }
        internal Func<int?> MaxUtf16Length { get; }
        internal Func<int, bool>? ShouldReplaceRole { get; }
        internal NativeDialogueContext(string game, Func<bool> isCurrent, Func<int, string?> resolveName,
            Func<int?> maxUtf16Length, Func<int, bool>? shouldReplaceRole = null)
        { Game = game; IsCurrent = isCurrent; ResolveName = resolveName; MaxUtf16Length = maxUtf16Length; ShouldReplaceRole = shouldReplaceRole; }
    }
    internal static class DialogueNative
    {
        internal static int NameDepth, Enters, Disposes;
        internal static bool ThrowEnter, ThrowSuppress;
        internal static ActiveGame LastGame;
        internal static NativeDialogueContext? Current;
        private sealed class Scope(Action dispose) : IDisposable
        { private Action? _dispose = dispose; public void Dispose() { var call = _dispose; _dispose = null; call?.Invoke(); } }
        internal static IDisposable Enter(ActiveGame game, Func<NativeDialogueContext?> capture)
        {
            if (ThrowEnter) throw new InvalidOperationException("Bridge unavailable");
            var before = Current; LastGame = game; Enters++; Current = capture();
            return new Scope(() => { Current = before; Disposes++; });
        }
        internal static IDisposable SuppressNames()
        {
            if (ThrowSuppress) throw new InvalidOperationException("Suppression unavailable");
            NameDepth++;
            return new Scope(() => NameDepth--);
        }
    }
}
namespace SuikodenRandomizer.AvatarPlugin
{
    // Admission/state values are test doubles. The publication snapshot itself
    // and all adapter patch methods are the actual linked production sources.
    internal sealed partial class AvatarRuntime
    {
        internal static AvatarRuntime? Current { get; set; }
        internal object State { get; private set; } = new();
        private object? _fixedProfile = new(), _fixedAssignments = new();
        private Dictionary<int, GSD1.PLAYER_DATA> _originals = new(), _proxies = new();
        private GSD1.PlayerManager? _manager;
        private IntPtr _game;
        private bool _preparing, _fixedLoading;
        internal bool Active = true, Owns = true;
        internal long Epoch = 1, AdmittedEpoch = 1;
        internal bool IsActive => Active && Owns && Epoch == AdmittedEpoch;
        internal long ExecutionEpoch => Epoch;
        internal bool MatchesExecution(long epoch) => Owns && Epoch == epoch && AdmittedEpoch == epoch;
        internal readonly Dictionary<int, int> Assignments = new() { [0] = 28, [28] = 42, [22] = 22 };
        internal bool IsRole(int id) => IsActive && Assignments.ContainsKey(id);
        internal int MapDisplayId(int id) => IsActive && Assignments.TryGetValue(id, out int value) ? value : id;
        internal AvatarRuntime()
        {
            Current = this;
            var game = new GSD1.GAME_WORK(); var manager = new GSD1.PlayerManager();
            _manager = manager; _game = game.Pointer;
            GSD1.OldSrcBase.game_work = game; GSD1.GlobalWork.Instance = new GSD1.GlobalWork { player_manager = manager };
            GSD1.OldSrcBase.ThrowOnRead = false; GSD1.OldSrcBase.Reads = GSD1.GlobalWork.Reads = 0;
            AvatarSaveCall.Current = null;
        }
        internal void Change(string kind)
        {
            switch (kind)
            {
                case "state": State = new(); break;
                case "profile": _fixedProfile = new(); break;
                case "assignments": _fixedAssignments = new(); break;
                case "originals": _originals = new(); break;
                case "proxies": _proxies = new(); break;
                case "manager": _manager = new(); break;
                case "game": _game = new IntPtr(999999); break;
                case "preparing": _preparing = true; break;
                case "loading": _fixedLoading = true; break;
                default: throw new ArgumentException(kind);
            }
        }
    }
}
