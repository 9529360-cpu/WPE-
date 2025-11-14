using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace 币安量化机器人.Services
{
    // 简单的 Mock 交易所 / 历史数据回放器，用于回测与回放场景。
    public class MockExchange
    {
        public class Tick
        {
            public DateTime Timestamp { get; set; }
            public decimal Price { get; set; }
            public decimal Volume { get; set; }
        }

        // 为简化实现，生成合成数据。真实场景可替换为读取 CSV 或数据库历史数据。
        public Task<IEnumerable<Tick>> GetHistoricalTicksAsync(string symbol, DateTime from, DateTime to)
        {
            var list = new List<Tick>();
            var rnd = new Random(42);
            var t = from;
            decimal price = 100m;
            while (t <= to)
            {
                price += (decimal)(rnd.NextDouble() - 0.5) * 0.5m;
                list.Add(new Tick { Timestamp = t, Price = Math.Round(price, 2), Volume = (decimal)rnd.NextDouble() * 10 });
                t = t.AddSeconds(1);
            }
            return Task.FromResult((IEnumerable<Tick>)list);
        }
    }
}
