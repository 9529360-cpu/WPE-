using System.Windows.Controls;
using System.Windows;
using 币安量化机器人.ViewModels;

namespace 币安量化机器人.Controls
{
    public partial class OrderExecutionControl : UserControl
    {
        public OrderExecutionControl()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = App.ServiceProvider?.GetService(typeof(OrderExecutionViewModel)) as OrderExecutionViewModel;
            }
        }
    }
}
