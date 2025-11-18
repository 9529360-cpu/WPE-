using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace 币安量化机器人.Resources
{
    /// <summary>
    /// 字符串资源管理器
    /// 支持多语言国际化
    /// </summary>
    public class Strings
    {
        private static ResourceManager? _resourceManager;
        private static CultureInfo? _resourceCulture;

        private static ResourceManager ResourceManager
        {
            get
            {
                if (_resourceManager == null)
                {
                    _resourceManager = new ResourceManager("币安量化机器人.Resources.Strings", typeof(Strings).Assembly);
                }
                return _resourceManager;
            }
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static CultureInfo Culture
        {
            get { return _resourceCulture ?? CultureInfo.CurrentUICulture; }
            set { _resourceCulture = value; }
        }

        // 通用
        public static string AppName => GetString("AppName") ?? "币安量化机器人";
        public static string OK => GetString("OK") ?? "确定";
        public static string Cancel => GetString("Cancel") ?? "取消";
        public static string Save => GetString("Save") ?? "保存";
        public static string Delete => GetString("Delete") ?? "删除";
        public static string Edit => GetString("Edit") ?? "编辑";
        public static string Export => GetString("Export") ?? "导出";
        public static string Import => GetString("Import") ?? "导入";
        public static string Close => GetString("Close") ?? "关闭";
        public static string Yes => GetString("Yes") ?? "是";
        public static string No => GetString("No") ?? "否";
        public static string Error => GetString("Error") ?? "错误";
        public static string Warning => GetString("Warning") ?? "警告";
        public static string Info => GetString("Info") ?? "信息";
        public static string Success => GetString("Success") ?? "成功";
        public static string Failed => GetString("Failed") ?? "失败";
        public static string Loading => GetString("Loading") ?? "加载中...";
        public static string Running => GetString("Running") ?? "运行中...";
        public static string Completed => GetString("Completed") ?? "已完成";
        public static string Cancelled => GetString("Cancelled") ?? "已取消";

        // 模块名称
        public static string Module_Market => GetString("Module_Market") ?? "行情";
        public static string Module_Funding => GetString("Module_Funding") ?? "资金费率";
        public static string Module_Strategy => GetString("Module_Strategy") ?? "策略配置";
        public static string Module_StrategyHub => GetString("Module_StrategyHub") ?? "策略库";
        public static string Module_AI => GetString("Module_AI") ?? "AI";
        public static string Module_Optimization => GetString("Module_Optimization") ?? "优化";
        public static string Module_Backtest => GetString("Module_Backtest") ?? "回测";
        public static string Module_PaperTrade => GetString("Module_PaperTrade") ?? "纸交易";
        public static string Module_Trade => GetString("Module_Trade") ?? "交易";
        public static string Module_Positions => GetString("Module_Positions") ?? "持仓订单";
        public static string Module_Risk => GetString("Module_Risk") ?? "风控";
        public static string Module_Alert => GetString("Module_Alert") ?? "预警";
        public static string Module_Account => GetString("Module_Account") ?? "账户";
        public static string Module_API => GetString("Module_API") ?? "API";
        public static string Module_Settings => GetString("Module_Settings") ?? "设置";
        public static string Module_Diagnostics => GetString("Module_Diagnostics") ?? "诊断";

        // 错误消息
        public static string Error_ModuleNotRegistered => GetString("Error_ModuleNotRegistered") ?? "模块未注册";
        public static string Error_ModuleLoadFailed => GetString("Error_ModuleLoadFailed") ?? "模块加载失败";
        public static string Error_InvalidConfiguration => GetString("Error_InvalidConfiguration") ?? "配置无效";
        public static string Error_FileNotFound => GetString("Error_FileNotFound") ?? "文件未找到";
        public static string Error_SaveFailed => GetString("Error_SaveFailed") ?? "保存失败";
        public static string Error_ExportFailed => GetString("Error_ExportFailed") ?? "导出失败";
        public static string Error_ImportFailed => GetString("Error_ImportFailed") ?? "导入失败";
        public static string Error_NetworkError => GetString("Error_NetworkError") ?? "网络错误";
        public static string Error_UnexpectedError => GetString("Error_UnexpectedError") ?? "未预期的错误";

        // 状态消息
        public static string Status_Ready => GetString("Status_Ready") ?? "就绪";
        public static string Status_Loading => GetString("Status_Loading") ?? "正在加载...";
        public static string Status_Saving => GetString("Status_Saving") ?? "正在保存...";
        public static string Status_Processing => GetString("Status_Processing") ?? "处理中...";
        public static string Status_Complete => GetString("Status_Complete") ?? "完成";

        // 验证消息
        public static string Validation_Required => GetString("Validation_Required") ?? "此项为必填项";
        public static string Validation_InvalidValue => GetString("Validation_InvalidValue") ?? "无效的值";
        public static string Validation_OutOfRange => GetString("Validation_OutOfRange") ?? "超出范围";
        public static string Validation_TooLarge => GetString("Validation_TooLarge") ?? "数值过大";
        public static string Validation_TooSmall => GetString("Validation_TooSmall") ?? "数值过小";

        // 优化相关
        public static string Optimization_Started => GetString("Optimization_Started") ?? "优化已开始";
        public static string Optimization_Completed => GetString("Optimization_Completed") ?? "优化已完成";
        public static string Optimization_Cancelled => GetString("Optimization_Cancelled") ?? "优化已取消";
        public static string Optimization_Failed => GetString("Optimization_Failed") ?? "优化失败";
        public static string Optimization_SearchSpaceTooLarge => GetString("Optimization_SearchSpaceTooLarge") ?? "搜索空间过大";

        // Helper method
        private static string? GetString(string name)
        {
            try
            {
                return ResourceManager.GetString(name, _resourceCulture);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 格式化字符串
        /// </summary>
        public static string Format(string format, params object[] args)
        {
            return string.Format(Culture, format, args);
        }
    }
}
