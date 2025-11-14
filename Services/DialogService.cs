using System.Windows;

namespace 币安量化机器人.Services
{
    public class DialogService : IDialogService
    {
        public bool Confirm(string message, string caption)
        {
            var r = MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return r == MessageBoxResult.Yes;
        }

        public void ShowInfo(string message, string caption)
        {
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowError(string message, string caption)
        {
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
