using System;
using System.Windows.Media;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Strategy
{
    public class StrategyRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Weight { get; set; }
        public bool IsRunning { get; set; }
        public decimal TotalReturn { get; set; }
        public double SharpeRatio { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public double WinRate { get; set; }
        public TimeSpan RunningTime { get; set; }

        // 新增显示字段
        public MarketType Market { get; set; }
        public StrategyStage Stage { get; set; }
        public string AuditSource { get; set; } = string.Empty;
        public DateTime? AuditTimestampUtc { get; set; }

        public string StatusText => IsRunning ? "运行中" : "已停止";
        public Brush StatusColor => IsRunning
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
            : new SolidColorBrush(Color.FromRgb(107, 114, 128));

        public string WeightText => $"{Weight:P0}";
        public double WeightPercent => Weight * 100;

        public string ReturnText => $"{TotalReturn:P2}";
        public string SharpeText => $"{SharpeRatio:F2}";
        public string TradesText => $"{TotalTrades} 笔";
        public string WinRateText => $"{WinRate:P0}";
        public string RunningTimeText => RunningTime.TotalHours >= 1
            ? $"{RunningTime.TotalHours:F1}h"
            : $"{RunningTime.TotalMinutes:F0}m";
    }
}
