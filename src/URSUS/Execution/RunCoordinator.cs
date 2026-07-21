using URSUS.Analysis;
using URSUS.Caching;

namespace URSUS.Execution;

public enum RunState { Idle, Running, CancelRequested, Canceled, Succeeded, Faulted }

public sealed record RunCoordinatorStatus(
    RunState State, long Generation, string? QueryFingerprint, bool IsStale,
    double Progress, string? Stage, string? Error);

public sealed class RunCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<AnalysisRequest, CancellationToken, IProgress<AnalysisProgress>?, Task<SolverResult>> _execute;
    private readonly IClock _clock;
    private readonly RisingEdgeGate _runGate = new();
    private readonly RisingEdgeGate _cancelGate = new();
    private CancellationOwner? _currentCancellation;
    private Task? _currentTask;
    private SolverResult? _lastSuccessfulResult;
    private DateTimeOffset _lastProgressAt = DateTimeOffset.MinValue;
    private string? _observedFingerprint;
    private bool _disposed;
    private RunCoordinatorStatus _status = new(RunState.Idle, 0, null, false, 0, null, null);

    public RunCoordinator(
        Func<AnalysisRequest, CancellationToken, IProgress<AnalysisProgress>?, Task<SolverResult>> execute,
        IClock? clock = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _clock = clock ?? SystemClock.Instance;
    }

    public RunCoordinatorStatus Status { get { lock (_sync) return _status; } }
    public SolverResult? LastSuccessfulResult { get { lock (_sync) return _lastSuccessfulResult; } }
    public AnalysisSnapshot? LastSuccessfulSnapshot { get { lock (_sync) return _lastSuccessfulResult?.Snapshot; } }
    public Task? CurrentTask { get { lock (_sync) return _currentTask; } }
    public event Action<RunCoordinatorStatus>? Changed;

    public long? ObserveRun(bool value, AnalysisRequest request)
    {
        bool trigger = ObserveRunSignal(value);
        if (!value) return null;
        ObserveQuery(request);
        return trigger ? StartObserved(request) : null;
    }

    public bool ObserveRunSignal(bool value)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _runGate.Observe(value);
        }
    }

    public void ObserveQuery(AnalysisRequest request)
    {
        bool changed = false;
        lock (_sync)
        {
            ThrowIfDisposed();
            _observedFingerprint = request.QueryFingerprint;
            if (_status.QueryFingerprint != null)
            {
                bool stale = _status.QueryFingerprint != request.QueryFingerprint;
                changed = _status.IsStale != stale;
                _status = _status with { IsStale = stale };
            }
        }
        if (changed) Publish();
    }

    public void MarkObservedInvalid()
    {
        bool changed = false;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_status.QueryFingerprint != null && !_status.IsStale)
            {
                _status = _status with { IsStale = true };
                changed = true;
            }
        }
        if (changed) Publish();
    }

    public void MarkObservedCurrent(string queryFingerprint)
    {
        if (string.IsNullOrWhiteSpace(queryFingerprint))
            throw new ArgumentException("Query fingerprint is required.", nameof(queryFingerprint));
        bool changed = false;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_status.IsStale || _status.QueryFingerprint != queryFingerprint)
            {
                _status = _status with
                {
                    QueryFingerprint = queryFingerprint,
                    IsStale = false,
                };
                changed = true;
            }
        }
        if (changed) Publish();
    }

    public bool CancelGeneration(long generation)
    {
        CancellationOwner? cancellation = null;
        lock (_sync)
        {
            if (_disposed || _status.Generation != generation ||
                _status.State is not (RunState.Running or RunState.CancelRequested)) return false;
            _status = _status with { State = RunState.CancelRequested };
            cancellation = _currentCancellation;
        }
        Publish();
        cancellation?.Cancel();
        return true;
    }

    public long StartObserved(AnalysisRequest request) => Start(request);

    public bool ObserveCancel(bool value)
    {
        CancellationOwner? cancellation = null;
        bool trigger;
        lock (_sync)
        {
            ThrowIfDisposed();
            trigger = _cancelGate.Observe(value);
            if (trigger && _status.State == RunState.Running)
            {
                _status = _status with { State = RunState.CancelRequested };
                cancellation = _currentCancellation;
            }
            else trigger = false;
        }
        if (!trigger) return false;
        Publish();
        cancellation!.Cancel();
        return true;
    }

    private long Start(AnalysisRequest request)
    {
        CancellationOwner? previous;
        var cancellation = new CancellationOwner();
        long generation;
        lock (_sync)
        {
            ThrowIfDisposed();
            previous = _currentCancellation;
            _currentCancellation = cancellation;
            generation = _status.Generation + 1;
            _lastProgressAt = DateTimeOffset.MinValue;
            _status = new RunCoordinatorStatus(RunState.Running, generation,
                request.QueryFingerprint, false, 0, "starting", null);
        }
        previous?.Cancel();
        Publish();
        var progress = new InlineProgress(value => AcceptProgress(generation, value));
        Task execution = ExecuteGenerationAsync(generation, request, cancellation, progress);
        lock (_sync)
        {
            if (!_disposed && _status.Generation == generation) _currentTask = execution;
        }
        return generation;
    }

    private async Task ExecuteGenerationAsync(long generation, AnalysisRequest request,
        CancellationOwner cancellation, IProgress<AnalysisProgress> progress)
    {
        try
        {
            SolverResult? result = null;
            Exception? failure = null;
            try { result = await _execute(request, cancellation.Token, progress).ConfigureAwait(false); }
            catch (Exception ex) { failure = ex; }

            lock (_sync)
            {
                if (_disposed || _status.Generation != generation) return;
                bool canceled = cancellation.IsCancellationRequested ||
                    _status.State == RunState.CancelRequested || failure is OperationCanceledException;
                if (canceled)
                    _status = _status with { State = RunState.Canceled, Stage = "canceled", Error = null };
                else if (failure != null)
                    _status = _status with { State = RunState.Faulted, Stage = "faulted", Error = failure.Message };
                else
                {
                    _lastSuccessfulResult = result!;
                    bool stale = _observedFingerprint != request.QueryFingerprint;
                    _status = _status with { State = RunState.Succeeded, Progress = 1,
                        Stage = "completed", Error = null, IsStale = stale };
                }
            }
            Publish();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_currentCancellation, cancellation)) _currentCancellation = null;
            }
            cancellation.Retire();
        }
    }

    private void AcceptProgress(long generation, AnalysisProgress value)
    {
        bool accepted = false;
        lock (_sync)
        {
            if (_disposed || _status.Generation != generation || _status.State != RunState.Running) return;
            double next = value.ClampedFraction;
            if (next < _status.Progress) return;
            DateTimeOffset now = _clock.UtcNow;
            if (next < 1 && now - _lastProgressAt < TimeSpan.FromMilliseconds(100)) return;
            _lastProgressAt = now;
            _status = _status with { Progress = next, Stage = value.Stage };
            accepted = true;
        }
        if (accepted) Publish();
    }

    private void Publish()
    {
        Action<RunCoordinatorStatus>? changed;
        RunCoordinatorStatus snapshot;
        lock (_sync)
        {
            if (_disposed) return;
            changed = Changed;
            snapshot = _status;
        }
        changed?.Invoke(snapshot);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RunCoordinator));
    }

    public void Dispose()
    {
        CancellationOwner? cancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            cancellation = _currentCancellation;
            _currentCancellation = null;
        }
        cancellation?.Cancel();
    }

    private sealed class InlineProgress : IProgress<AnalysisProgress>
    {
        private readonly Action<AnalysisProgress> _report;
        public InlineProgress(Action<AnalysisProgress> report) => _report = report;
        public void Report(AnalysisProgress value) => _report(value);
    }

    private sealed class CancellationOwner
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _source = new();
        private int _activeCancels;
        private bool _retired;
        private bool _disposed;

        public CancellationToken Token => _source.Token;
        public bool IsCancellationRequested => _source.IsCancellationRequested;

        public void Cancel()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _activeCancels++;
            }
            try { _source.Cancel(); }
            finally
            {
                lock (_gate)
                {
                    _activeCancels--;
                    DisposeIfReady();
                }
            }
        }

        public void Retire()
        {
            lock (_gate)
            {
                _retired = true;
                DisposeIfReady();
            }
        }

        private void DisposeIfReady()
        {
            if (_disposed || !_retired || _activeCancels != 0) return;
            _disposed = true;
            _source.Dispose();
        }
    }
}
