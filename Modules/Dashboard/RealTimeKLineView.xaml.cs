using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Threading;

namespace 币安量化机器人.Modules.Dashboard;

public partial class RealTimeKLineView : UserControl
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<double> _dummyPrices = new();

    public RealTimeKLineView()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, __) => RenderDummy();
        _refreshTimer.Start();

        // seed dummy data
        var r = new Random();
        for (int i = 0; i < 120; i++)
        {
            _dummyPrices.Add(50000 + r.NextDouble() * 2000 - 1000);
        }
    }

    private void RenderDummy()
    {
        // lightweight placeholder rendering: rotate prices and update canvases minimally
        if (_dummyPrices.Count == 0) return;
        _dummyPrices.RemoveAt(0);
        _dummyPrices.Add(_dummyPrices[^1] + (new Random().NextDouble() - 0.5) * 50);

        // For placeholder we won't do heavy drawing. Clear canvases to keep things responsive.
        PriceCanvas.Children.Clear();
        VolumeCanvas.Children.Clear();
    }
}
