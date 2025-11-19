using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using 币安量化机器人.Infrastructure.Data;
using 币安量化机器人.Core.Models;
using Xunit;

namespace 币安量化机器人.Tests
{
    public class TechnicalIndicatorEngineerTests
    {
        [Fact]
        public async Task TransformAsync_Should_Handle_MissingFields()
        {
            var engineer = new TechnicalIndicatorEngineer();
            var payload = new Dictionary<string, object>(); // missing close/high/low
            var frame = new RawDataFrame("test", "BTCUSDT", DateTime.UtcNow, payload);

            var features = await engineer.TransformAsync(frame);

            Assert.NotNull(features);
            Assert.True(features.ContainsKey("return"));
            Assert.True(features.ContainsKey("hl_range"));
            Assert.True(features.ContainsKey("volatility"));
        }

        [Fact]
        public async Task TransformAsync_Should_ComputeFeatures_For_NormalInput()
        {
            var engineer = new TechnicalIndicatorEngineer();
            var payload = new Dictionary<string, object>
            {
                ["close"] = 100.0,
                ["high"] = 110.0,
                ["low"] = 90.0
            };
            var frame = new RawDataFrame("test", "BTCUSDT", DateTime.UtcNow, payload);

            var features = await engineer.TransformAsync(frame);

            Assert.Equal(100.0, payload["close"]);
            Assert.NotNull(features);
            Assert.Equal((100.0 - 90.0) / (110.0 - 90.0), features["return"]);
        }
    }
}
