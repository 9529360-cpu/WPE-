using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Core
{
    public class RiskCheckResult
    {
        public bool Passed { get; set; }
        public string Reason { get; set; }
    }

    public interface IRiskManager
    {
        Task<RiskCheckResult> CheckOrderAsync(OrderRequestEvent request);
    }
}
