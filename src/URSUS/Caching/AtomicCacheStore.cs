using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using URSUS.DataSources;

namespace URSUS.Caching;

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();
    private SystemClock() { }
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public enum AcquisitionOrigin { Network, Embedded }
public enum DeliveryOrigin { Network, Cache, Embedded }

public sealed record PersistentCacheKey(string Value)
{
    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    { "key", "apiKey", "serviceKey", "token", "authorization" };

    public static PersistentCacheKey Create(
        string source,
        int schemaVersion,
        QueryIntent intent,
        IReadOnlyDictionary<string, string>? parameters,
        IEnumerable<string>? districtCodes,
        CoordinateReferenceSystem crs)
    {
        var canonical = new StringBuilder()
            .Append(source.Trim()).Append('|')
            .Append(schemaVersion).Append('|')
            .Append(intent).Append('|').Append(crs);
        if (districtCodes != null)
            foreach (string code in districtCodes.Select(DistrictCode.CanonicalizeLegal)
                         .Where(code => code.Length > 0).Distinct(StringComparer.Ordinal)
                         .OrderBy(code => code, StringComparer.Ordinal))
                canonical.Append("|d:").Append(code);
        if (parameters != null)
            foreach (var pair in parameters.Where(pair => !SecretNames.Contains(pair.Key))
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                canonical.Append("|p:").Append(pair.Key.Trim()).Append('=').Append(pair.Value.Trim());
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return new PersistentCacheKey(Convert.ToHexString(hash).ToLowerInvariant());
    }
}

public sealed record CacheRead<T>(
    T Value,
    DateTimeOffset RetrievedAt,
    AcquisitionOrigin AcquisitionOrigin,
    DeliveryOrigin DeliveryOrigin,
    TimeSpan CacheAge);

internal sealed record PersistedCacheEnvelope<T>(
    int SchemaVersion,
    DateTimeOffset RetrievedAt,
    AcquisitionOrigin AcquisitionOrigin,
    T Value);

public sealed class AtomicCacheStore
{
    private const int EnvelopeSchema = 1;
    private readonly string _root;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InflightOperation> _inflight = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AtomicCacheStore(string? rootOverride = null, IClock? clock = null)
    {
        _root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "URSUS", "cache");
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<CacheRead<T>> GetOrFetchAsync<T>(
        PersistentCacheKey key,
        bool forceRefresh,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken cancellationToken = default)
        => await GetOrFetchAsync(key, forceRefresh, _ => ttl, fetch, cancellationToken)
            .ConfigureAwait(false);

    public async Task<CacheRead<T>> GetOrFetchAsync<T>(
        PersistentCacheKey key,
        bool forceRefresh,
        Func<T, TimeSpan> ttlSelector,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ttlSelector);
        string inflightKey = $"{key.Value}|{forceRefresh}|{typeof(T).FullName}";
        var candidate = new InflightOperation(token => ExecuteOriginAsync(
            key, forceRefresh, ttlSelector, fetch, token).ContinueWith<object>(
                completed => completed.GetAwaiter().GetResult(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default));
        var operation = _inflight.GetOrAdd(inflightKey, candidate);
        if (!ReferenceEquals(operation, candidate)) candidate.Dispose();
        operation.AddWaiter();
        try
        {
            var result = await operation.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return (CacheRead<T>)result;
        }
        finally
        {
            int remaining = operation.RemoveWaiter();
            if (remaining == 0 || operation.Task.IsCompleted)
                _inflight.TryRemove(new KeyValuePair<string, InflightOperation>(inflightKey, operation));
        }
    }

    private async Task<CacheRead<T>> ExecuteOriginAsync<T>(
        PersistentCacheKey key,
        bool forceRefresh,
        Func<T, TimeSpan> ttlSelector,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(key.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = Path.Combine(_root, key.Value + ".json");
            if (!forceRefresh)
            {
                var hit = await TryReadAsync(path, ttlSelector, cancellationToken).ConfigureAwait(false);
                if (hit != null) return hit;
            }

            T value = await fetch(cancellationToken).ConfigureAwait(false);
            DateTimeOffset retrievedAt = _clock.UtcNow;
            var envelope = new PersistedCacheEnvelope<T>(EnvelopeSchema, retrievedAt,
                AcquisitionOrigin.Network, value);
            await WriteAtomicAsync(path, envelope, cancellationToken).ConfigureAwait(false);
            return new CacheRead<T>(value, retrievedAt, AcquisitionOrigin.Network,
                DeliveryOrigin.Network, TimeSpan.Zero);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class InflightOperation : IDisposable
    {
        private readonly CancellationTokenSource _originCancellation = new();
        private readonly Lazy<Task<object>> _task;
        private int _waiters;

        public InflightOperation(Func<CancellationToken, Task<object>> start)
            => _task = new Lazy<Task<object>>(
                () => start(_originCancellation.Token),
                LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<object> Task => _task.Value;
        public void AddWaiter() => Interlocked.Increment(ref _waiters);
        public int RemoveWaiter()
        {
            int remaining = Interlocked.Decrement(ref _waiters);
            if (remaining == 0 && _task.IsValueCreated && !_task.Value.IsCompleted)
                _originCancellation.Cancel();
            return remaining;
        }
        public void Dispose() => _originCancellation.Dispose();
    }

    private async Task<CacheRead<T>?> TryReadAsync<T>(
        string path, Func<T, TimeSpan> ttlSelector, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            PersistedCacheEnvelope<T>? envelope;
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                envelope = await JsonSerializer.DeserializeAsync<PersistedCacheEnvelope<T>>(
                    stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            if (envelope == null || envelope.SchemaVersion != EnvelopeSchema || envelope.Value is null)
            {
                TryDeleteCorrupt(path);
                return null;
            }
            TimeSpan ttl;
            try { ttl = ttlSelector(envelope.Value); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                       NullReferenceException)
            {
                TryDeleteCorrupt(path);
                return null;
            }
            if (ttl <= TimeSpan.Zero)
            {
                TryDeleteCorrupt(path);
                return null;
            }
            TimeSpan age = _clock.UtcNow - envelope.RetrievedAt;
            if (age < TimeSpan.Zero || age > ttl) return null;
            return new CacheRead<T>(envelope.Value, envelope.RetrievedAt,
                envelope.AcquisitionOrigin, DeliveryOrigin.Cache, age);
        }
        catch (JsonException) { TryDeleteCorrupt(path); return null; }
        catch (IOException) { return null; }
    }

    private static void TryDeleteCorrupt(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task WriteAtomicAsync<T>(
        string path, PersistedCacheEnvelope<T> envelope, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
