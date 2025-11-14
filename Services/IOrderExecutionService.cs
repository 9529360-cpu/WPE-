using System.Threading.Tasks;

namespace 币安量化机器人.Services
{
    public interface IOrderExecutionService
    {
        Task<string> PlaceOrderAsync(object order);
        Task CancelOrderAsync(string orderId);
    }
}
