using System;
using System.Threading;

namespace SuikodenRandomizer.Shared;

public enum ActiveGame { None = 0, Gsd1 = 1, Gsd2 = 2 }

/// <summary>Transient ownership, never a saved campaign or settings identity.</summary>
public readonly struct GameOwnershipToken : IEquatable<GameOwnershipToken>
{
    private readonly long _router;
    public ActiveGame Game { get; }
    public long Generation { get; }
    internal GameOwnershipToken(long router, ActiveGame game, long generation)
    { _router = router; Game = game; Generation = generation; }
    public bool Equals(GameOwnershipToken other) => _router == other._router
        && Game == other.Game && Generation == other.Generation;
    public override bool Equals(object? other) => other is GameOwnershipToken token && Equals(token);
    public override int GetHashCode() => HashCode.Combine(_router, Game, Generation);
    public static bool operator ==(GameOwnershipToken left, GameOwnershipToken right) => left.Equals(right);
    public static bool operator !=(GameOwnershipToken left, GameOwnershipToken right) => !left.Equals(right);
}

/// <summary>Routes confirmed game contexts; contains no native state or filesystem access.</summary>
public sealed class ActiveGameRouter
{
    private static long _nextRouter;
    private readonly long _identity = Interlocked.Increment(ref _nextRouter);
    private readonly object _gate = new();
    private readonly Func<string?, ActiveGame> _classify;
    private ActiveGame _game;
    private long _generation;
    private string? _classificationFailure;
    public ActiveGameRouter(Func<string?, ActiveGame>? classifier = null) => _classify = classifier ?? ClassifyScene;
    public ActiveGame CurrentGame { get { lock (_gate) return _game; } }
    public long Generation { get { lock (_gate) return _generation; } }
    public string? LastClassificationFailure { get { lock (_gate) return _classificationFailure; } }

    public static ActiveGame ClassifyScene(string? sceneName) => sceneName switch
    { "GSD1" => ActiveGame.Gsd1, "GSD2" => ActiveGame.Gsd2, _ => ActiveGame.None };

    public GameOwnershipToken ObserveScene(string? sceneName)
    {
        ActiveGame selected;
        string? failure = null;
        try
        {
            selected = _classify(sceneName);
            if (selected != ActiveGame.None && selected != ActiveGame.Gsd1 && selected != ActiveGame.Gsd2)
            { selected = ActiveGame.None; failure = "Scene classifier returned an unsupported game."; }
        }
        catch (Exception error) { selected = ActiveGame.None; failure = error.Message; }
        return ObserveSelection(selected, failure);
    }

    /// <summary>
    /// The selected game owns execution only while its root scene is loaded.
    /// An additive battle/background becoming active does not leave that game.
    /// A selection change revokes the old owner even before its root unloads.
    /// </summary>
    public GameOwnershipToken ObserveGameContext(ActiveGame selectedGame, bool gsd1RootLoaded, bool gsd2RootLoaded)
    {
        var selected = selectedGame switch
        {
            ActiveGame.Gsd1 when gsd1RootLoaded => ActiveGame.Gsd1,
            ActiveGame.Gsd2 when gsd2RootLoaded => ActiveGame.Gsd2,
            _ => ActiveGame.None
        };
        string? failure = selectedGame != ActiveGame.None && selectedGame != ActiveGame.Gsd1 && selectedGame != ActiveGame.Gsd2
            ? "Game selection returned an unsupported game." : null;
        return ObserveSelection(selected, failure);
    }

    private GameOwnershipToken ObserveSelection(ActiveGame selected, string? failure)
    {
        lock (_gate)
        {
            _classificationFailure = failure;
            if (selected != _game) { _generation = checked(_generation + 1); _game = selected; }
            return Token();
        }
    }
    public GameOwnershipToken Capture() { lock (_gate) return Token(); }
    public bool TryCapture(ActiveGame expected, out GameOwnershipToken token)
    {
        token = Capture();
        return expected != ActiveGame.None && token.Game == expected;
    }
    /// <summary>Invalidates callbacks for a load/reset within the same named game scene.</summary>
    public GameOwnershipToken Invalidate()
    { lock (_gate) { _generation = checked(_generation + 1); return Token(); } }
    public bool IsCurrent(GameOwnershipToken token)
    { lock (_gate) return _game != ActiveGame.None && token == Token(); }
    /// <summary>
    /// Checks immediately before synchronous dispatch. Native effects must still
    /// run on the host's Unity thread; this does not make an async body atomic.
    /// Re-check the token before subsequent phases of a callback.
    /// </summary>
    public bool TryRun(GameOwnershipToken token, Action callback)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        if (!IsCurrent(token)) return false;
        callback();
        return true;
    }
    private GameOwnershipToken Token() => new(_identity, _game, _generation);
}
