using System;
using System.Threading;

namespace SuikodenRandomizer.Dialogue;

/// <summary>A snapshot of one admitted native dialogue invocation, never a process-wide campaign.</summary>
public sealed class DialogueRewriteContext
{
    public DialogueRewriteContext(string game, string language, Func<bool> isCurrent,
        Func<int, string?> resolveCanonicalRoleName, Func<int?>? maxUtf16Length = null,
        Func<int, bool>? shouldReplaceRole = null)
    {
        Game = game; Language = language;
        IsCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        ResolveName = resolveCanonicalRoleName ?? throw new ArgumentNullException(nameof(resolveCanonicalRoleName));
        MaxUtf16Length = maxUtf16Length ?? (() => null);
        ShouldReplaceRole = shouldReplaceRole;
    }
    public string Game { get; }
    public string Language { get; }
    internal Func<bool> IsCurrent { get; }
    internal Func<int, string?> ResolveName { get; }
    internal Func<int?> MaxUtf16Length { get; }
    internal Func<int, bool>? ShouldReplaceRole { get; }
}

/// <summary>Thread-local synchronous scopes and name/reentrancy barriers around the pure rewriter.</summary>
public sealed class DialogueRewriteGate
{
    private readonly DialogueRewriteCatalog _catalog;
    private readonly ThreadLocal<ThreadState> _threads = new ThreadLocal<ThreadState>(() => new ThreadState());
    public DialogueRewriteGate(DialogueRewriteCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    // An unadmitted entry still pushes a barrier. It cannot inherit a foreign or
    // otherwise admitted outer window's context while native calls are nested.
    public IDisposable Enter(DialogueRewriteContext? context)
    {
        var state = _threads.Value!;
        var frame = new Frame(context, state.Current);
        state.Current = frame;
        return new ScopeLease(state, frame);
    }

    public IDisposable SuppressNames()
    {
        var state = _threads.Value!;
        state.SuppressionDepth++;
        return new SuppressionLease(state, state.SuppressionDepth);
    }

    public string? Rewrite(string? rawText, string? category, int nativeMessageId)
        => Rewrite(rawText, category, nativeMessageId, out _);

    internal string? Rewrite(string? rawText, string? category, int nativeMessageId, out string? appliedGame)
    {
        appliedGame = null;
        var state = _threads.Value!;
        var frame = state.Current;
        var context = frame?.Context;
        if (rawText is null || category != "message" || state.Poisoned || state.SuppressionDepth != 0
            || context is null || context.Language != "en" || (context.Game != "GSD1" && context.Game != "GSD2")) return rawText;
        try
        {
            // Native SIDs are passed through a signed int even for rows above
            // Int32.MaxValue. Preserve all 32 bits instead of sign extending.
            var key = new DialogueMessageKey(context.Game, context.Language, unchecked((uint)nativeMessageId));
            if (!_catalog.HasEntry(key)) return rawText;
            using (SuppressNames())
            {
                bool Current() => !state.Poisoned && ReferenceEquals(state.Current, frame) && context.IsCurrent();
                if (!Current()) return rawText;
                string? result = _catalog.Rewrite(rawText, key, role => Current() ? context.ResolveName(role) : null,
                    shouldReplaceRole: context.ShouldReplaceRole is null ? null : role => Current() && context.ShouldReplaceRole(role));
                if (!Current()) return rawText;
                if (string.Equals(result, rawText, StringComparison.Ordinal)) return rawText;
                int? maximum = context.MaxUtf16Length();
                if (!Current() || maximum.HasValue && (maximum.Value <= 0 || result!.Length > maximum.Value)) return rawText;
                appliedGame = context.Game;
                return result;
            }
        }
        catch (Exception) { return rawText; }
    }

    private sealed class ThreadState
    {
        internal readonly int ThreadId = Environment.CurrentManagedThreadId;
        internal Frame? Current;
        internal int SuppressionDepth;
        internal volatile bool Poisoned;
    }
    private sealed class Frame
    {
        internal Frame(DialogueRewriteContext? context, Frame? previous) { Context = context; Previous = previous; }
        internal readonly DialogueRewriteContext? Context;
        internal readonly Frame? Previous;
    }
    private sealed class ScopeLease : IDisposable
    {
        private readonly ThreadState _state;
        private readonly Frame _frame;
        private int _disposed;
        internal ScopeLease(ThreadState state, Frame frame) { _state = state; _frame = frame; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_state.ThreadId != Environment.CurrentManagedThreadId || !ReferenceEquals(_state.Current, _frame))
            { _state.Poisoned = true; return; }
            _state.Current = _frame.Previous;
        }
    }
    private sealed class SuppressionLease : IDisposable
    {
        private readonly ThreadState _state;
        private readonly int _depth;
        private int _disposed;
        internal SuppressionLease(ThreadState state, int depth) { _state = state; _depth = depth; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_state.ThreadId != Environment.CurrentManagedThreadId || _state.SuppressionDepth != _depth)
            { _state.Poisoned = true; return; }
            _state.SuppressionDepth--;
        }
    }
}
