using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Performance;

/// <summary>
/// 实时数据处理优化器
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 数据流批处理
/// 2. 内存池管理
/// 3. 并行处理优化
/// 4. 背压控制
/// 5. 智能缓存
/// 
/// 性能目标:
/// - 数据处理延迟 < 50ms
/// - 吞吐量 > 10,000条/秒
/// - 内存使用 < 500MB
/// - CPU占用 < 20%
/// </remarks>
public class RealtimeDataProcessor : IDisposable
{
    private readonly int _batchSize;
    private readonly TimeSpan _batchTimeout;
    private readonly int _maxQueueSize;
    private readonly int _parallelism;

    private readonly ConcurrentQueue<DataItem> _inputQueue;
    private readonly SemaphoreSlim _queueSemaphore;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _processingTasks;

    // 🆕 性能监控
    private long _processedCount;
    private long _droppedCount;
    private readonly Stopwatch _performanceTimer;

    // 🆕 内存池
    private readonly ObjectPool<List<DataItem>> _batchPool;

    public RealtimeDataProcessor(
        int batchSize = 100,
        int maxQueueSize = 10000,
        int parallelism = 4)
    {
        _batchSize = batchSize;
        _batchTimeout = TimeSpan.FromMilliseconds(100);
        _maxQueueSize = maxQueueSize;
        _parallelism = parallelism;

        _inputQueue = new ConcurrentQueue<DataItem>();
        _queueSemaphore = new SemaphoreSlim(_maxQueueSize);
        _cts = new CancellationTokenSource();
        _processingTasks = new Task[_parallelism];
        _performanceTimer = Stopwatch.StartNew();

        // 初始化内存池
        _batchPool = new ObjectPool<List<DataItem>>(
            () => new List<DataItem>(_batchSize),
            list => list.Clear(),
            maxSize: _parallelism * 2);

        // 启动处理任务
        for (int i = 0; i < _parallelism; i++)
        {
            _processingTasks[i] = Task.Run(() => ProcessingLoopAsync(_cts.Token));
        }

        LogService.Info("[RealtimeDataProcessor] 实时数据处理器已启动: BatchSize={BatchSize}, Parallelism={Parallelism}",
            _batchSize, _parallelism);
    }

    /// <summary>
    /// 提交数据项进行处理
    /// </summary>
    public async ValueTask<bool> SubmitAsync(DataItem item, CancellationToken ct = default)
    {
        // 背压控制
        if (_inputQueue.Count >= _maxQueueSize)
        {
            Interlocked.Increment(ref _droppedCount);
            LogService.Warning("[RealtimeDataProcessor] 队列已满，丢弃数据: QueueSize={Size}", _inputQueue.Count);
            return false;
        }

        // 等待队列空间
        if (!await _queueSemaphore.WaitAsync(0, ct))
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }

        try
        {
            _inputQueue.Enqueue(item);
            return true;
        }
        catch
        {
            _queueSemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 批量提交数据
    /// </summary>
    public async ValueTask<int> SubmitBatchAsync(IEnumerable<DataItem> items, CancellationToken ct = default)
    {
        int submitted = 0;
        foreach (DataItem item in items)
        {
            if (await SubmitAsync(item, ct))
            {
                submitted++;
            }
        }
        return submitted;
    }

    /// <summary>
    /// 处理循环
    /// </summary>
    private async Task ProcessingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 从内存池获取批次容器
                List<DataItem> batch = _batchPool.Rent();

                try
                {
                    // 收集批次
                    await CollectBatchAsync(batch, ct);

                    if (batch.Count == 0)
                    {
                        continue;
                    }

                    // 处理批次
                    await ProcessBatchAsync(batch, ct);

                    // 更新统计
                    Interlocked.Add(ref _processedCount, batch.Count);
                }
                finally
                {
                    // 归还到内存池
                    _batchPool.Return(batch);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[RealtimeDataProcessor] 处理循环异常");
                await Task.Delay(100, ct);
            }
        }
    }

    /// <summary>
    /// 收集批次数据
    /// </summary>
    private async Task CollectBatchAsync(List<DataItem> batch, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.Add(_batchTimeout);

        while (batch.Count < _batchSize && DateTime.UtcNow < deadline)
        {
            if (_inputQueue.TryDequeue(out DataItem item))
            {
                batch.Add(item);
                _queueSemaphore.Release();
            }
            else
            {
                // 没有数据，等待一小段时间
                await Task.Delay(1, ct);
            }
        }
    }

    /// <summary>
    /// 处理批次（可被子类重写）
    /// </summary>
    protected virtual async Task ProcessBatchAsync(List<DataItem> batch, CancellationToken ct)
    {
        // 默认实现：并行处理每个项
        await Parallel.ForEachAsync(batch, ct, async (item, token) =>
        {
            await ProcessItemAsync(item, token);
        });
    }

    /// <summary>
    /// 处理单个数据项（可被子类重写）
    /// </summary>
    protected virtual async ValueTask ProcessItemAsync(DataItem item, CancellationToken ct)
    {
        // 默认实现：模拟处理
        await Task.Delay(1, ct);

        // 触发处理完成事件
        DataProcessed?.Invoke(this, new DataProcessedEventArgs(item));
    }

    /// <summary>
    /// 数据处理完成事件
    /// </summary>
    public event EventHandler<DataProcessedEventArgs>? DataProcessed;

    /// <summary>
    /// 获取性能统计
    /// </summary>
    public PerformanceStats GetStats()
    {
        long processed = Interlocked.Read(ref _processedCount);
        long dropped = Interlocked.Read(ref _droppedCount);
        double elapsedSeconds = _performanceTimer.Elapsed.TotalSeconds;

        return new PerformanceStats
        {
            ProcessedCount = processed,
            DroppedCount = dropped,
            QueueSize = _inputQueue.Count,
            Throughput = elapsedSeconds > 0 ? processed / elapsedSeconds : 0,
            DropRate = processed + dropped > 0 ? (double)dropped / (processed + dropped) : 0,
            Uptime = _performanceTimer.Elapsed
        };
    }

    /// <summary>
    /// 优雅关闭
    /// </summary>
    public async Task ShutdownAsync(TimeSpan timeout = default)
    {
        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(30);
        }

        LogService.Info("[RealtimeDataProcessor] 开始优雅关闭...");

        // 等待队列清空
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (_inputQueue.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        // 取消处理任务
        _cts.Cancel();

        // 等待所有任务完成
        try
        {
            await Task.WhenAll(_processingTasks).WaitAsync(timeout);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        PerformanceStats stats = GetStats();
        LogService.Info("[RealtimeDataProcessor] 已关闭: 处理={Processed}, 丢弃={Dropped}, 吞吐量={Throughput:F0}/s",
            stats.ProcessedCount, stats.DroppedCount, stats.Throughput);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _queueSemaphore.Dispose();
    }
}

#region 数据模型

/// <summary>
/// 数据项
/// </summary>
public class DataItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object> Payload { get; set; } = new();
}

/// <summary>
/// 数据处理完成事件参数
/// </summary>
public class DataProcessedEventArgs : EventArgs
{
    public DataItem Item { get; }
    public DateTime ProcessedAt { get; }

    public DataProcessedEventArgs(DataItem item)
    {
        Item = item;
        ProcessedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 性能统计
/// </summary>
public class PerformanceStats
{
    public long ProcessedCount { get; init; }
    public long DroppedCount { get; init; }
    public int QueueSize { get; init; }
    public double Throughput { get; init; }
    public double DropRate { get; init; }
    public TimeSpan Uptime { get; init; }

    public override string ToString()
    {
        return $"Processed: {ProcessedCount}, Dropped: {DroppedCount}, Queue: {QueueSize}, " +
               $"Throughput: {Throughput:F0}/s, DropRate: {DropRate:P2}, Uptime: {Uptime:hh\\:mm\\:ss}";
    }
}

#endregion

#region 对象池

/// <summary>
/// 简单对象池实现
/// </summary>
public class ObjectPool<T> where T : class
{
    private readonly ConcurrentBag<T> _objects;
    private readonly Func<T> _objectFactory;
    private readonly Action<T> _resetAction;
    private readonly int _maxSize;

    public ObjectPool(Func<T> objectFactory, Action<T> resetAction, int maxSize = 100)
    {
        _objectFactory = objectFactory;
        _resetAction = resetAction;
        _maxSize = maxSize;
        _objects = new ConcurrentBag<T>();
    }

    public T Rent()
    {
        if (_objects.TryTake(out T obj))
        {
            return obj;
        }

        return _objectFactory();
    }

    public void Return(T obj)
    {
        if (_objects.Count < _maxSize)
        {
            _resetAction(obj);
            _objects.Add(obj);
        }
    }
}

#endregion
