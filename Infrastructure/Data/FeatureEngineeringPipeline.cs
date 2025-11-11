using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Data;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Infrastructure.Data;

public sealed class TechnicalIndicatorEngineer : IFeatureEngineer
{
    public string Name => "TechnicalIndicators";

    public ValueTask<IReadOnlyDictionary<string, double>> TransformAsync(RawDataFrame frame, CancellationToken cancellationToken = default)
    {
        double close = Convert.ToDouble(frame.Payload["close"]);
        double high = Convert.ToDouble(frame.Payload["high"]);
        double low = Convert.ToDouble(frame.Payload["low"]);
        double range = high - low;
        var features = new Dictionary<string, double>
        {
            ["return"] = range == 0 ? 0 : (close - low) / range,
            ["hl_range"] = range,
            ["volatility"] = range / (close == 0 ? 1 : close)
        };

        return ValueTask.FromResult<IReadOnlyDictionary<string, double>>(features);
    }
}

public sealed class LagFeatureEngineer : IFeatureEngineer
{
    private readonly Queue<double> _closeHistory = new();
    private readonly int _lag;

    public LagFeatureEngineer(int lag = 5)
    {
        _lag = lag;
    }

    public string Name => "LagFeatures";

    public ValueTask<IReadOnlyDictionary<string, double>> TransformAsync(RawDataFrame frame, CancellationToken cancellationToken = default)
    {
        double close = Convert.ToDouble(frame.Payload["close"]);
        _closeHistory.Enqueue(close);
        if (_closeHistory.Count > _lag)
        {
            _closeHistory.Dequeue();
        }

        var features = _closeHistory.Select((value, index) => (value, index))
            .ToDictionary(x => $"lag_{x.index}", x => x.value);

        return ValueTask.FromResult<IReadOnlyDictionary<string, double>>(features);
    }
}
