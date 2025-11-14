using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace 币安量化机器人.Services.Runtime;

public static class InMemoryLogBuffer
{
    private static readonly ConcurrentQueue<string> _queue = new();
    private const int MaxEntries = 2000;

    public static event Action<string>? LogAppended;

    public static void Append(string message)
    {
        if (message == null)
        {
            return;
        }
        _queue.Enqueue(message);
        // trim
        while (_queue.Count > MaxEntries && _queue.TryDequeue(out _)) { }
        LogAppended?.Invoke(message);
    }

    public static IReadOnlyList<string> ReadAll()
    {
        return _queue.ToArray();
    }

    public static void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
