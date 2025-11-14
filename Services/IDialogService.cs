using System;

namespace 币安量化机器人.Services
{
    public interface IDialogService
    {
        bool Confirm(string message, string caption);
        void ShowInfo(string message, string caption);
        void ShowError(string message, string caption);
    }
}
