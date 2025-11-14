using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace 币安量化机器人.Persistence
{
    public interface IRepository
    {
        Task InitializeAsync();

        Task SavePendingOrderAsync(string orderId, string payload);
        Task RemovePendingOrderAsync(string orderId);
        Task<IEnumerable<(string OrderId, string Payload)>> GetPendingOrdersAsync();

        Task SavePositionAsync(string positionId, string payload);
        Task<string> GetPositionAsync(string positionId);

        Task AppendEventAsync(string eventType, string payload, DateTime timestamp);
    }
}
