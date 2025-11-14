using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI
{
    public interface ITradingAgent
    {
        Task<AITradingSignal> AnalyzeMarketSituationAsync(MarketDataSnapshot marketData, CancellationToken ct = default);
    }
}
