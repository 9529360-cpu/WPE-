using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Core.Risk;

public class BlacklistManager
{
    private readonly ConcurrentDictionary<string, int> _lossCounters = new();
    private readonly ConcurrentDictionary<string, DateTime> _blacklist = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromHours(1);
    private int _threshold;

    public IReadOnlyCollection<string> Symbols => _blacklist.Keys.ToList();

    public void Configure(RiskConfiguration configuration)
    {
        if (configuration == null)
        {
            return;
        }

        // BlacklistThreshold is double; store as int threshold (rounded)
        _threshold = (int)Math.Round(configuration.BlacklistThreshold);
    }

    public void Update(string symbol, int consecutiveLosses)
    {
        if (consecutiveLosses >= _threshold)
        {
            _blacklist[symbol] = DateTime.UtcNow.Add(_cooldown);
        }
        else
        {
            _lossCounters[symbol] = consecutiveLosses;
        }

        ClearExpired();
    }

    public bool IsBlacklisted(TradeActionType actionType, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        // currently not using actionType in decision, future extension point
        return _blacklist.ContainsKey(symbol);
    }

    // Overload for callers that only provide symbol
    public bool IsBlacklisted(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return _blacklist.ContainsKey(symbol);
    }

    // Record a fill to adjust internal counters / cooling down logic
    public void RecordFill(string symbol, TradeActionType actionType)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        // on successful fill reduce consecutive loss counter for symbol (if present)
        _lossCounters.AddOrUpdate(symbol, 0, (_, old) => Math.Max(0, old - 1));
    }


    private void ClearExpired()
    {
        foreach (KeyValuePair<string, DateTime> item in _blacklist.ToArray())
        {
            if (item.Value <= DateTime.UtcNow)
            {
                _blacklist.TryRemove(item.Key, out _);
            }
        }
    }
}

public sealed class ConsecutiveLossBlacklistRule : IRiskRule
{
    private readonly BlacklistManager _manager;
    private int _threshold = 3;

    public ConsecutiveLossBlacklistRule(BlacklistManager manager)
    {
        _manager = manager;
    }

    public string Name => "Blacklist";

    public void Configure(RiskConfiguration configuration)
    {
        _threshold = (int)configuration.BlacklistThreshold;
        _manager.Configure(configuration);
    }

    public RiskRuleResult Evaluate(in PositionSnapshot snapshot)
    {
        if (snapshot.ConsecutiveLosingTrades >= _threshold)
        {
            return new RiskRuleResult(false, $"{snapshot.Symbol} reached {_threshold} losses");
        }

        return new RiskRuleResult(true);
    }
}
