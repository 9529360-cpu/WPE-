using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 模块注册和管理服务
    /// 提供插件化的模块加载机制，降低MainWindow耦合度
    /// </summary>
    public interface IModuleRegistry
    {
        void RegisterModule(string tag, Type moduleType, string category = "其他");
        UserControl? CreateModule(string tag);
        IEnumerable<ModuleInfo> GetAllModules();
        IEnumerable<ModuleInfo> GetModulesByCategory(string category);
        bool IsModuleRegistered(string tag);
    }

    public class ModuleInfo
    {
        public string Tag { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public Type ModuleType { get; set; } = null!;
        public string Description { get; set; } = string.Empty;
    }

    public class ModuleRegistry : IModuleRegistry
    {
        private readonly Dictionary<string, ModuleInfo> _modules = new();
        private readonly ILogger _logger = LoggerFactory.CreateLogger<ModuleRegistry>();

        public ModuleRegistry()
        {
            RegisterDefaultModules();
        }

        private void RegisterDefaultModules()
        {
            // 市场与行情
            RegisterModule("行情", typeof(Modules.Market.RealtimeView), "市场与行情");
            RegisterModule("资金费率", typeof(Modules.Market.FundingView), "市场与行情");

            // 策略与 AI
            RegisterModule("策略配置", typeof(Modules.Strategy.SettingsView), "策略与AI");
            RegisterModule("策略库", typeof(Modules.Strategy.TemplateHub), "策略与AI");
            RegisterModule("AI", typeof(Modules.AI.ModelHub), "策略与AI");

            // 优化与研发
            RegisterModule("优化", typeof(Modules.Optimize.WfoOptimizer), "优化与研发");
            RegisterModule("回测", typeof(Modules.Research.BacktestView), "优化与研发");
            RegisterModule("纸交易", typeof(Modules.Paper.PaperTradeView), "优化与研发");

            // 实盘与风控
            RegisterModule("交易", typeof(Modules.Trade.TradeView), "实盘与风控");
            RegisterModule("持仓订单", typeof(Modules.Trade.PositionsOrdersView), "实盘与风控");
            RegisterModule("风控", typeof(Modules.Risk.RiskCenterView), "实盘与风控");
            RegisterModule("预警", typeof(Modules.Alert.AlertCenterView), "实盘与风控");

            // 账户与连接
            RegisterModule("账户", typeof(Modules.Account.AccountFundsView), "账户与连接");
            RegisterModule("API", typeof(Modules.Account.ApiManagerView), "账户与连接");

            // 系统
            RegisterModule("设置", typeof(Modules.Settings.SystemSettingsView), "系统");
            RegisterModule("诊断", typeof(Modules.Diagnostics.DiagnosticsView), "系统");

            _logger.Info($"已注册 {_modules.Count} 个模块");
        }

        public void RegisterModule(string tag, Type moduleType, string category = "其他")
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("Tag不能为空", nameof(tag));

            if (moduleType == null)
                throw new ArgumentNullException(nameof(moduleType));

            if (!typeof(UserControl).IsAssignableFrom(moduleType))
                throw new ArgumentException($"模块类型必须继承自UserControl: {moduleType.FullName}");

            var moduleInfo = new ModuleInfo
            {
                Tag = tag,
                Category = category,
                ModuleType = moduleType,
                Description = $"{category} - {tag}"
            };

            _modules[tag] = moduleInfo;
            _logger.Debug($"注册模块: {tag} ({moduleType.Name})");
        }

        public UserControl? CreateModule(string tag)
        {
            if (!_modules.TryGetValue(tag, out var moduleInfo))
            {
                _logger.Warning($"尝试创建未注册的模块: {tag}");
                return null;
            }

            try
            {
                var instance = Activator.CreateInstance(moduleInfo.ModuleType);
                _logger.Info($"成功创建模块实例: {tag}");
                return instance as UserControl;
            }
            catch (Exception ex)
            {
                _logger.Error($"创建模块实例失败: {tag}", ex);
                return null;
            }
        }

        public IEnumerable<ModuleInfo> GetAllModules()
        {
            return _modules.Values.OrderBy(m => m.Category).ThenBy(m => m.Tag);
        }

        public IEnumerable<ModuleInfo> GetModulesByCategory(string category)
        {
            return _modules.Values
                .Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Tag);
        }

        public bool IsModuleRegistered(string tag)
        {
            return _modules.ContainsKey(tag);
        }
    }
}
