using System.Threading.Channels;

namespace Armament.GameServer.Persistence;

public sealed class BoundedLootPersistenceQueue : ILootPersistenceSink, IAsyncDisposable
{
    private readonly Channel<LootPersistenceRequest> _channel;
    private readonly Func<LootPersistenceRequest, CancellationToken, Task> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    private long _processedCount;
    private long _droppedCount;
    private long _failedCount;

    public BoundedLootPersistenceQueue(
        int capacity,
        Func<LootPersistenceRequest, CancellationToken, Task> handler,
        int maxAttempts = 3,
        TimeSpan? retryDelay = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        _handler = handler;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(20);

        _channel = Channel.CreateBounded<LootPersistenceRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _workerTask = Task.Run(WorkerAsync);
    }

    public long ProcessedCount => Interlocked.Read(ref _processedCount);
    public long DroppedCount => Interlocked.Read(ref _droppedCount);
    public long FailedCount => Interlocked.Read(ref _failedCount);

    public bool TryEnqueue(LootPersistenceRequest request)
    {
        var wrote = _channel.Writer.TryWrite(request);
        if (!wrote)
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return wrote;
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                await ProcessSingleAsync(request, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessSingleAsync(LootPersistenceRequest request, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (attempt < _maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await _handler(request, cancellationToken);
                Interlocked.Increment(ref _processedCount);
                return;
            }
            catch
            {
                Interlocked.Increment(ref _failedCount);
                if (attempt >= _maxAttempts)
                {
                    return;
                }

                try
                {
                    await Task.Delay(_retryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            await _workerTask;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}
