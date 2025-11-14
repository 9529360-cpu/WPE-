using System.Threading.Tasks;
using System;
using System.Threading;

namespace 币安量化机器人.Services
{
    public class RiskManager : global::币安量化机器人.Core.IRiskManager, global::币安量化机器人.Core.Risk.IRiskManager
    {
        public double MaxOrderQuantity { get; set; } = 100;
        public double MaxPositionSize { get; set; } = 1000;
        public double DailyLossLimit { get; set; } = 10000;

        public Task<global::币安量化机器人.Core.RiskCheckResult> CheckOrderAsync(global::币安量化机器人.Core.OrderRequestEvent request)
        {
            if (request.Quantity <= 0)
            {
                return Task.FromResult(new global::币安量化机器人.Core.RiskCheckResult { Passed = false, Reason = "数量必须大于 0" });
            }

            if (request.Quantity > MaxOrderQuantity)
            {
                return Task.FromResult(new global::币安量化机器人.Core.RiskCheckResult { Passed = false, Reason = "超出单笔数量限制" });
            }

            return Task.FromResult(new global::币安量化机器人.Core.RiskCheckResult { Passed = true });
        }

        // Core.Risk.IRiskManager implementation
        #pragma warning disable CS0067
                public event EventHandler<global::币安量化机器人.Core.Risk.RiskEvent>? RiskTriggered; // reserved for subscribers
        #pragma warning restore CS0067

        // Add explicit properties to RiskProfile mapping so Service assignment compiles
        public global::币安量化机器人.Core.Risk.RiskProfile CurrentProfile { get; private set; } = new global::币安量化机器人.Core.Risk.RiskProfile();

        public void Configure(global::币安量化机器人.Core.Risk.RiskConfiguration configuration)
        {
            if (configuration == null)
            {
                return;
            }

            CurrentProfile = new global::币安量化机器人.Core.Risk.RiskProfile();

            MaxOrderQuantity = configuration.MaxOrderQuantity;
            MaxPositionSize = configuration.MaxPositionSize;
            DailyLossLimit = configuration.DailyLossLimit;
        }

        public ValueTask UpdateAsync(global::币安量化机器人.Core.Risk.PositionSnapshot position, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public bool Approve(global::币安量化机器人.Core.Risk.TradeAction action)
        {
            if (action == null)
            {
                return true;
            }
            if (action.Quantity > MaxOrderQuantity)
            {
                return false;
            }
            return true;
        }

        public ValueTask RecordFillAsync(global::币安量化机器人.Core.Risk.TradeFill fill, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
