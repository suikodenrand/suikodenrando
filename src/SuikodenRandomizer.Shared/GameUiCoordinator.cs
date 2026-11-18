using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Shared;

public readonly struct EngineStatus
{
    public string Text { get; }
    public bool IsFaulted { get; }
    public EngineStatus(string text, bool isFaulted = false) { Text = text ?? ""; IsFaulted = isFaulted; }
}

/// <summary>Game-specific facades keep their native state, settings and persistence private.</summary>
public interface IGameUiBackend
{
    ActiveGame Game { get; }
    string DisplayName { get; }
    // Status snapshots should be managed-only. The host must call ObserveScene
    // before routing work and repeat ownership checks inside native callbacks.
    EngineStatus GetStatus(GameOwnershipToken ownership);
    void Tick(GameOwnershipToken ownership);
    void HandleF6(GameOwnershipToken ownership);
}

public sealed class BackendFailure
{
    public ActiveGame Game { get; }
    public long Generation { get; }
    public string Operation { get; }
    public string Message { get; }
    internal GameOwnershipToken Ownership { get; }
    internal BackendFailure(GameOwnershipToken ownership, string operation, string message)
    { Ownership = ownership; Game = ownership.Game; Generation = ownership.Generation; Operation = operation; Message = message; }
}

public readonly struct GameUiSnapshot
{
    public ActiveGame Game { get; }
    public long Generation { get; }
    public string DisplayName { get; }
    public string Status { get; }
    public bool Faulted { get; }
    public bool HasBackend { get; }
    internal GameUiSnapshot(GameOwnershipToken token, string name, string status, bool faulted, bool hasBackend)
    { Game = token.Game; Generation = token.Generation; DisplayName = name; Status = status; Faulted = faulted; HasBackend = hasBackend; }
}

/// <summary>One host owns UI/input dispatch; one backend is selected at a time.</summary>
public sealed class GameUiCoordinator
{
    private readonly ActiveGameRouter _router;
    private readonly Dictionary<ActiveGame, IGameUiBackend> _backends = new();
    private readonly Dictionary<ActiveGame, BackendFailure> _failures = new();
    private bool _hasF6Frame;
    private long _lastF6Frame;
    public GameUiCoordinator(ActiveGameRouter router) => _router = router ?? throw new ArgumentNullException(nameof(router));
    public void Register(IGameUiBackend backend)
    {
        if (backend is null) throw new ArgumentNullException(nameof(backend));
        var game = backend.Game;
        if (game != ActiveGame.Gsd1 && game != ActiveGame.Gsd2) throw new ArgumentException("Backend requires a specific game.", nameof(backend));
        if (_backends.ContainsKey(game)) throw new InvalidOperationException("A backend is already registered for " + game + ".");
        _backends.Add(game, backend);
    }
    public BackendFailure? GetFailure(ActiveGame game) => _failures.TryGetValue(game, out var value) ? value : null;
    public void ClearFailure(ActiveGame game) => _failures.Remove(game);

    // Owned lifecycle work must still run while an engine reports a fault:
    // title/load cleanup lives there. Each backend guards its own projection.
    public bool Tick() => Dispatch("tick", static (backend, token) => backend.Tick(token), allowFaultedStatus: true);
    public bool DispatchF6(long frame, bool focused, bool pressed)
    {
        if (!focused || !pressed || (_hasF6Frame && _lastF6Frame == frame)) return false;
        // Consume once even in the launcher/unknown scene, so the same key event
        // cannot acquire a second destination after an ownership change.
        _hasF6Frame = true; _lastF6Frame = frame;
        return Dispatch("F6", static (backend, token) => backend.HandleF6(token));
    }
    public GameUiSnapshot SelectUi()
    {
        var token = _router.Capture();
        if (!Select(token, out var backend)) return Empty(token);
        var failure = CurrentFailure(token);
        if (failure is not null) return Fault(token, failure);
        try
        {
            string name = backend.DisplayName ?? token.Game.ToString();
            var status = backend.GetStatus(token);
            return _router.IsCurrent(token)
                ? new GameUiSnapshot(token, name, status.Text ?? "", status.IsFaulted, true)
                : Empty(_router.Capture());
        }
        catch (Exception error)
        {
            var recorded = Record(token, "status", error);
            return _router.IsCurrent(token) ? Fault(token, recorded) : Empty(_router.Capture());
        }
    }
    private bool Dispatch(string operation, Action<IGameUiBackend, GameOwnershipToken> action, bool allowFaultedStatus = false)
    {
        var token = _router.Capture();
        if (!Select(token, out var backend) || CurrentFailure(token) is not null) return false;
        try
        {
            if ((!allowFaultedStatus && backend.GetStatus(token).IsFaulted) || !_router.IsCurrent(token)) return false;
            action(backend, token);
            return true;
        }
        catch (Exception error) { Record(token, operation, error); return false; }
    }
    private bool Select(GameOwnershipToken token, out IGameUiBackend backend)
    {
        backend = null!;
        return _router.IsCurrent(token) && _backends.TryGetValue(token.Game, out backend!);
    }
    private BackendFailure? CurrentFailure(GameOwnershipToken token) => _failures.TryGetValue(token.Game, out var value)
        && value.Ownership == token ? value : null;
    private BackendFailure Record(GameOwnershipToken token, string operation, Exception error)
    {
        var failure = new BackendFailure(token, operation, error.Message);
        // A reentrant status/callback can invalidate ownership, dispatch a new
        // generation, then finish its older frame with an exception. Preserve
        // the newer generation's failure instead of reopening that backend.
        if (!_failures.TryGetValue(token.Game, out var existing) || existing.Generation <= token.Generation)
            _failures[token.Game] = failure;
        return failure;
    }
    private static GameUiSnapshot Empty(GameOwnershipToken token) => new(token, "", "", false, false);
    private static GameUiSnapshot Fault(GameOwnershipToken token, BackendFailure failure) => new(token,
        token.Game.ToString(), failure.Operation + ": " + failure.Message, true, true);
}
