using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;

namespace 币安量化机器人.Persistence
{
    /// <summary>
    /// 使用 LiteDB 的简单仓库实现。数据库文件位于应用程序目录下 `data.db`。
    /// </summary>
    public class LiteDbRepository : IRepository, IDisposable
    {
        private readonly LiteDatabase _db;

        public LiteDbRepository(string filePath = "data.db")
        {
            _db = new LiteDatabase(filePath);
        }

        public Task InitializeAsync()
        {
            // 确保集合存在
            _db.GetCollection("pending_orders");
            _db.GetCollection("positions");
            _db.GetCollection("events");
            return Task.CompletedTask;
        }

        public Task SavePendingOrderAsync(string orderId, string payload)
        {
            var col = _db.GetCollection("pending_orders");
            col.Upsert(new BsonDocument { ["_id"] = orderId, ["payload"] = payload });
            return Task.CompletedTask;
        }

        public Task RemovePendingOrderAsync(string orderId)
        {
            var col = _db.GetCollection("pending_orders");
            col.Delete(orderId);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<(string OrderId, string Payload)>> GetPendingOrdersAsync()
        {
            var col = _db.GetCollection("pending_orders");
            var list = col.FindAll().Select(d => (d["_id"].AsString, d["payload"].AsString));
            return Task.FromResult((IEnumerable<(string, string)>)list);
        }

        public Task SavePositionAsync(string positionId, string payload)
        {
            var col = _db.GetCollection("positions");
            col.Upsert(new BsonDocument { ["_id"] = positionId, ["payload"] = payload });
            return Task.CompletedTask;
        }

        public Task<string> GetPositionAsync(string positionId)
        {
            var col = _db.GetCollection("positions");
            var doc = col.FindById(positionId);
            return Task.FromResult(doc == null ? null : doc["payload"].AsString);
        }

        public Task AppendEventAsync(string eventType, string payload, DateTime timestamp)
        {
            var col = _db.GetCollection("events");
            col.Insert(new BsonDocument { ["type"] = eventType, ["payload"] = payload, ["ts"] = timestamp.ToUniversalTime() });
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
