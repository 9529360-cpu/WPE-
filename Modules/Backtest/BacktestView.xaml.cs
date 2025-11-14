using System;
using System.Windows.Controls;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Backtest
{
    public partial class BacktestView : UserControl
    {
        private readonly MockExchange _mock = new MockExchange();

        public BacktestView()
        {
            InitializeComponent();
            RunBtn.Click += async (s, e) =>
            {
                try
                {
                    var from = DateTime.Parse(FromBox.Text);
                    var to = DateTime.Parse(ToBox.Text);
                    var ticks = await _mock.GetHistoricalTicksAsync(SymbolBox.Text, from, to);
                    int count = 0;
                    decimal avg = 0;
                    foreach (var t in ticks)
                    {
                        count++;
                        avg += t.Price;
                    }
                    if (count > 0)
                    {
                        avg /= count;
                    }
                    ResultBox.Text = $"Ticks: {count}, AvgPrice: {Math.Round(avg,2)}";
                }
                catch (Exception ex)
                {
                    ResultBox.Text = "回测失败: " + ex.Message;
                }
            };
        }
    }
}
