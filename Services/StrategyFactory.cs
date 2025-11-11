using System.Collections.Generic;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Core.Strategies;

namespace 币安量化机器人.Services;

public sealed class StrategyFactory
{
    private readonly IMultiTimeframeAnalyzer _analyzer;
    private readonly IMachineLearningSignalGenerator _mlGen;
    private readonly IFeatureStore _featureStore;

    public StrategyFactory(IMultiTimeframeAnalyzer analyzer, IMachineLearningSignalGenerator mlGen, IFeatureStore featureStore)
    {
        _analyzer = analyzer; _mlGen = mlGen; _featureStore = featureStore;
    }

    public ITradingStrategy CreateFromInstance(StrategyInstance instance)
    {
        var parameters = new StrategyParameters(instance.Parameters);
        return instance.Type switch
        {
            "MeanReversion" => new MeanReversionStrategy(_analyzer, _mlGen, _featureStore, parameters),
            "Momentum" => new MomentumStrategy(_analyzer, parameters),
            _ => new MeanReversionStrategy(_analyzer, _mlGen, _featureStore, parameters)
        };
    }
}
