using System.Collections.ObjectModel;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using System.Windows.Input;
using System.Threading.Tasks;
using System;

namespace 币安量化机器人.ViewModels
{
    public class OrderExecutionViewModel : ObservableObject
    {
        private readonly Services.IOrderExecutionService _orderService;

        public ObservableCollection<Models.Order> Orders { get; } = new ObservableCollection<Models.Order>();

        public ICommand CancelOrderCommand { get; }

        public OrderExecutionViewModel(Services.IOrderExecutionService orderService)
        {
            _orderService = orderService;
            CancelOrderCommand = new RelayCommand<string>(async id => await CancelOrderAsync(id));

            // initialize service
            _ = _orderService.InitializeAsync();

            // demo data
            Orders.Add(new Models.Order { Id = "o1", Symbol = "BTCUSDT", Side = "BUY", Quantity = 0.01, Status = "NEW" });
            Orders.Add(new Models.Order { Id = "o2", Symbol = "ETHUSDT", Side = "SELL", Quantity = 0.1, Status = "FILLED" });
        }

        private async Task CancelOrderAsync(string id)
        {
            try
            {
                await _orderService.CancelOrderAsync(id);
                var ord = FindOrderById(id);
                if (ord != null)
                {
                    ord.Status = "CANCELED";
                    RaisePropertyChanged(nameof(Orders));
                }
            }
            catch (Exception)
            {
                // TODO: 错误处理与回退
            }
        }

        private Models.Order FindOrderById(string id)
        {
            foreach (var o in Orders)
            {
                if (o.Id == id) return o;
            }
            return null;
        }
    }
}
