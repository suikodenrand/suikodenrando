using System;
using System.Threading;

namespace SuikodenRandomizer.AvatarPlugin;

internal enum NuzlockeRetryResourceStatus { Idle, Loading, Completed, Failed, Cancelled }

/// <summary>Wait for one asynchronous native load before scheduling another battle.</summary>
internal sealed class NuzlockeRetryResourceGate
{
    private readonly Action<Action> _load;
    private readonly Func<bool> _ready;
    private readonly Action _resume;
    private readonly Action<Exception> _fault;
    private readonly Func<double> _clock;
    private readonly double _timeoutSeconds;
    private double _started;
    private int _loadCompleted;

    internal NuzlockeRetryResourceStatus Status { get; private set; }

    internal NuzlockeRetryResourceGate(Action<Action> load, Func<bool> ready, Action resume,
        Action<Exception> fault, Func<double> clock, double timeoutSeconds = 30)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _ready = ready ?? throw new ArgumentNullException(nameof(ready));
        _resume = resume ?? throw new ArgumentNullException(nameof(resume));
        _fault = fault ?? throw new ArgumentNullException(nameof(fault));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        _timeoutSeconds = timeoutSeconds;
    }

    internal void Poll()
    {
        if (Status is NuzlockeRetryResourceStatus.Completed or NuzlockeRetryResourceStatus.Failed or NuzlockeRetryResourceStatus.Cancelled) return;
        try
        {
            if (Status == NuzlockeRetryResourceStatus.Idle)
            {
                _started = _clock();
                Status = NuzlockeRetryResourceStatus.Loading;
                _load(() => Volatile.Write(ref _loadCompleted, 1));
                // A synchronous completion still resumes through a subsequent
                // native game-step poll instead of reentering the current one.
                return;
            }
            if (_clock() - _started >= _timeoutSeconds)
                throw new TimeoutException("Shared battle animation resources did not reload within 30 seconds.");
            if (Volatile.Read(ref _loadCompleted) == 0) return;
            if (!_ready()) throw new InvalidOperationException("The native loader completed without valid shared battle animation resources.");
            Status = NuzlockeRetryResourceStatus.Completed;
            _resume();
        }
        catch (Exception error)
        {
            Status = NuzlockeRetryResourceStatus.Failed;
            _fault(error);
        }
    }

    internal void Cancel() => Status = NuzlockeRetryResourceStatus.Cancelled;
}
