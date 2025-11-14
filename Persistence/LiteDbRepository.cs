using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Persistence;

namespace 币安量化机器人.Persistence
{
    /// <summary>
    /// Simple file-backed repository used as placeholder when LiteDB package is not available.
    /// It stores pending orders as individual files under a folder.
    /// This keeps repository functional without adding external NuGet dependencies.
    /// </summary>
    public class LiteDbRepository : IRepository
    {
        private readonly string _dir;

        public LiteDbRepository()
        {
            _dir = Path.Combine(AppContext.BaseDirectory, "persist");
            Directory.CreateDirectory(_dir);
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task<IEnumerable<(string OrderId, string Payload)>> GetPendingOrdersAsync()
        {
            var list = new List<(string OrderId, string Payload)>();
            foreach (var f in Directory.GetFiles(_dir, "*.order"))
            {
                try
                {
                    var id = Path.GetFileNameWithoutExtension(f);
                    var payload = File.ReadAllText(f);
                    list.Add((id, payload));
                }
                catch { }
            }
            return Task.FromResult<IEnumerable<(string OrderId, string Payload)>>(list);
        }

        public Task SavePendingOrderAsync(string orderId, string payload)
        {
            var path = Path.Combine(_dir, orderId + ".order");
            File.WriteAllText(path, payload);
            return Task.CompletedTask;
        }

        public Task RemovePendingOrderAsync(string orderId)
        {
            var path = Path.Combine(_dir, orderId + ".order");
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        public Task SavePositionAsync(string positionId, string payload)
        {
            var path = Path.Combine(_dir, positionId + ".pos");
            File.WriteAllText(path, payload);
            return Task.CompletedTask;
        }

        public Task<string> GetPositionAsync(string positionId)
        {
            var path = Path.Combine(_dir, positionId + ".pos");
            if (!File.Exists(path)) return Task.FromResult<string>(null);
            return Task.FromResult(File.ReadAllText(path));
        }

        public Task AppendEventAsync(string eventType, string payload, DateTime timestamp)
        {
            var path = Path.Combine(_dir, "events.log");
            File.AppendAllText(path, $"[{timestamp:O}] {eventType} {payload}\n");
            return Task.CompletedTask;
        }
    }
}
