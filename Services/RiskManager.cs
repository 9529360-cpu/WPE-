using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 简单风控实现：支持最大单笔数量、最大持仓量与日损阈值（暂不持久化）。
    /// 风控规则为可读写字段，后续可接入 UI 配置。
    /// </summary>
    public class RiskManager : IRiskManager
    {
        public double MaxOrderQuantity { get; set; } = 100;
        public double MaxPositionSize { get; set; } = 1000;
        public double DailyLossLimit { get; set; } = 10000;

        public Task<RiskCheckResult> CheckOrderAsync(OrderRequestEvent request)
        {
            if (request.Quantity <= 0)
            {
                return Task.FromResult(new RiskCheckResult { Passed = false, Reason = "数量必须大于 0" });
            }

            if (request.Quantity > MaxOrderQuantity)
            {
                return Task.FromResult(new RiskCheckResult { Passed = false, Reason = "超出单笔数量限制" });
            }

            // TODO: 检查持仓和日损（需要持久化/统计信息）
            return Task.FromResult(new RiskCheckResult { Passed = true });
        }
    }
}
