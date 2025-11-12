using System;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services
{
    // Lightweight runtime shared state for UI-mode selection and global events
    public static class RuntimeState
    {
        private static AccountType _currentAccountType = AccountType.Simulated;

        public static AccountType CurrentAccountType
        {
            get => _currentAccountType;
            set
            {
                if (_currentAccountType != value)
                {
                    _currentAccountType = value;
                    OnAccountTypeChanged?.Invoke(_currentAccountType);
                }
            }
        }

        public static event Action<AccountType>? OnAccountTypeChanged;
    }
}
