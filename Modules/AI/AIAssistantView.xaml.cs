using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http; // 🔧 添加这个 using
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.AI;

/// <summary>
/// AI智能交易助手 - 对话式交互界面
/// </summary>
public partial class AIAssistantView : UserControl
{
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private DeepSeekTradingAgent? _aiAgent; // 🔧 改为可变字段
    private readonly TradingAccountManager _accountManager;
    private readonly DataCacheService _cacheService;
    private bool _isProcessing = false;

    public AIAssistantView()
    {
        InitializeComponent();

        // 初始化服务
        _cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cacheService);

        // 🔧 初始化AI Agent
        InitializeAIAgent();

        MessagesControl.ItemsSource = _messages;

        // 添加欢迎消息
        AddWelcomeMessage();
    }

    /// <summary>
    /// 🔧 初始化AI Agent（支持重新初始化）
    /// </summary>
    private void InitializeAIAgent()
    {
        try
        {
            var aiConfig = ConfigurationService.GetAIConfig();

            if (!string.IsNullOrEmpty(aiConfig.DeepSeekApiKey))
            {
                // ✅ 使用配置的 API Key 初始化 AI Agent
                _aiAgent = new DeepSeekTradingAgent(aiConfig.DeepSeekApiKey);
                AIStatusText.Text = "AI就绪 ✅";
                LogService.Info("[AIAssistant] AI助手已启用，模型: {Model}, API Key: {MaskedKey}",
                    aiConfig.Model,
                    $"{aiConfig.DeepSeekApiKey.Substring(0, 8)}...{aiConfig.DeepSeekApiKey.Substring(aiConfig.DeepSeekApiKey.Length - 4)}");
            }
            else
            {
                _aiAgent = null;
                AIStatusText.Text = "演示模式 ⚠️";
                LogService.Warning("[AIAssistant] DeepSeek API Key 未配置，使用演示模式");
            }
        }
        catch (Exception ex)
        {
            _aiAgent = null;
            AIStatusText.Text = "离线 ❌";
            LogService.Error(ex, "[AIAssistant] AI引擎初始化失败");
        }
    }

    /// <summary>
    /// 🔧 添加欢迎消息
    /// </summary>
    private void AddWelcomeMessage()
    {
        string welcomeMessage = _aiAgent != null
            ? "👋 您好！我是AI交易助手（已启用真实AI）。\n\n"
            : "👋 您好！我是AI交易助手（演示模式）。\n\n";

        welcomeMessage +=
            "我可以帮您:\n" +
            "• 📊 分析市场趋势\n" +
            "• 💹 生成交易策略\n" +
            "• 🎯 优化参数配置\n" +
            "• ⚠️ 评估交易风险\n" +
            "• 📈 追踪绩效表现\n\n";

        if (_aiAgent == null)
        {
            welcomeMessage += "💡 配置 DeepSeek API Key 后可启用真实AI分析\n" +
                            "前往: 🔑 API 管理 → DeepSeek AI → 输入并保存 API Key";
        }
        else
        {
            welcomeMessage += "✨ 真实AI已启用，您可以直接提问！";
        }

        AddAIMessage(welcomeMessage);
    }

    /// <summary>
    /// 🔧 刷新AI配置（从API管理保存后调用）
    /// </summary>
    public void RefreshAIConfiguration()
    {
        try
        {
            LogService.Info("[AIAssistant] 正在刷新AI配置...");

            // 重新初始化AI Agent
            InitializeAIAgent();

            // 添加提示消息
            if (_aiAgent != null)
            {
                AddAIMessage("✅ AI配置已更新！现在可以使用真实AI分析了。");
            }
            else
            {
                AddAIMessage("⚠️ AI配置更新失败，请检查 API Key 是否正确。");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AIAssistant] 刷新AI配置失败");
            AddAIMessage($"❌ 配置刷新失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    /// <summary>
    /// 回车发送
    /// </summary>
    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    /// <summary>
    /// 快捷操作
    /// </summary>
    private async void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string action)
        {
            string message = action switch
            {
                "analyze_market" => "请分析当前市场状况",
                "recommend_strategy" => "请推荐适合当前市场的交易策略",
                "optimize_strategy" => "请帮我优化现有的交易策略",
                "assess_risk" => "请评估当前的交易风险",
                "show_performance" => "请展示我的交易绩效",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(message))
            {
                InputBox.Text = message;
                await SendMessageAsync();
            }
        }
    }

    /// <summary>
    /// 发送消息逻辑
    /// </summary>
    private async Task SendMessageAsync()
    {
        string? userInput = InputBox.Text?.Trim();

        if (string.IsNullOrEmpty(userInput) || userInput == "输入您的问题或指令...")
        {
            return;
        }

        if (_isProcessing)
        {
            MessageBox.Show("AI正在思考中，请稍候...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 🔧 改进：检查 AI 引擎状态并提供详细提示
        if (_aiAgent == null)
        {
            // 尝试重新初始化
            InitializeAIAgent();

            if (_aiAgent == null)
            {
                var aiConfig = ConfigurationService.GetAIConfig();

                string errorMessage = string.IsNullOrEmpty(aiConfig.DeepSeekApiKey)
                    ? "AI引擎未就绪 - DeepSeek API Key 未配置\n\n" +
                      "配置方法:\n" +
                      "1. 点击左侧 [🔑 API 管理]\n" +
                      "2. 找到 'DeepSeek AI 模型' 部分\n" +
                      "3. 输入您的 API Key\n" +
                      "4. 点击 [测试连接] 验证\n" +
                      "5. 点击 [保存] 使配置生效\n\n" +
                      "💡 获取 API Key: https://platform.deepseek.com/"
                    : "AI引擎初始化失败\n\n" +
                      $"API Key: {aiConfig.DeepSeekApiKey.Substring(0, 8)}...{aiConfig.DeepSeekApiKey.Substring(aiConfig.DeepSeekApiKey.Length - 4)}\n\n" +
                      "可能原因:\n" +
                      "1. API Key 无效或已过期\n" +
                      "2. 网络连接问题\n" +
                      "3. DeepSeek 服务暂时不可用\n\n" +
                      "建议:\n" +
                      "1. 重新获取有效的 API Key\n" +
                      "2. 检查网络连接\n" +
                      "3. 稍后重试";

                MessageBox.Show(
                    errorMessage,
                    "AI 引擎未就绪",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }
        }

        try
        {
            _isProcessing = true;
            SendButton.IsEnabled = false;
            StatusText.Text = "AI正在思考...";
            AIStatusText.Text = "思考中... 🤔";

            // 添加用户消息
            AddUserMessage(userInput);
            InputBox.Clear();

            // 滚动到底部
            MessageScroll.ScrollToBottom();

            // 构建上下文
            string context = BuildContext();

            // 🔧 调用真实AI
            string response = await Task.Run(async () =>
            {
                try
                {
                    // ✅ 调用真实 AI 对话模式
                    LogService.Info("[AIAssistant] 调用 AI 对话，用户输入: {Input}", userInput);
                    string? aiResponse = await _aiAgent.ChatAsync(userInput, context);
                    LogService.Info("[AIAssistant] AI 响应成功，长度: {Length}", aiResponse?.Length ?? 0);
                    return aiResponse ?? "AI 未返回响应";
                }
                catch (HttpRequestException httpEx)
                {
                    LogService.Error(httpEx, "[AIAssistant] HTTP 请求失败");

                    // 🔧 解析具体的 HTTP 错误
                    if (httpEx.Message.Contains("ASCII"))
                    {
                        return "❌ API Key 格式错误\n\n" +
                               "错误: API Key 包含非法字符\n\n" +
                               "解决方法:\n" +
                               "1. 前往 [API 管理]\n" +
                               "2. 重新粘贴 API Key\n" +
                               "3. 确保 API Key 只包含英文字母、数字和横杠\n" +
                               "4. 保存后重试";
                    }
                    else if (httpEx.Message.Contains("401"))
                    {
                        return "❌ API Key 无效\n\n" +
                               "错误: 身份验证失败 (401 Unauthorized)\n\n" +
                               "解决方法:\n" +
                               "1. 检查 API Key 是否正确\n" +
                               "2. 确认 API Key 未过期\n" +
                               "3. 重新获取有效的 API Key\n" +
                               "4. 在 [API 管理] 中更新并保存";
                    }
                    else if (httpEx.Message.Contains("429"))
                    {
                        return "❌ API 请求限额已用完\n\n" +
                               "错误: 请求频率过高 (429 Too Many Requests)\n\n" +
                               "解决方法:\n" +
                               "1. 等待几分钟后重试\n" +
                               "2. 检查 API Key 的配额\n" +
                               "3. 升级 DeepSeek 账户套餐";
                    }
                    else
                    {
                        return $"❌ 网络请求失败\n\n{httpEx.Message}\n\n" +
                               "请检查:\n" +
                               "1. 网络连接是否正常\n" +
                               "2. DeepSeek 服务是否可用\n" +
                               "3. 防火墙是否拦截了请求";
                    }
                }
                catch (TaskCanceledException)
                {
                    LogService.Warning("[AIAssistant] AI 请求超时");
                    return "❌ 请求超时\n\n" +
                           "AI 响应时间过长（超过30秒）\n\n" +
                           "建议:\n" +
                           "1. 简化您的问题\n" +
                           "2. 检查网络连接\n" +
                           "3. 稍后重试";
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[AIAssistant] AI调用失败");
                    return $"❌ AI处理出错\n\n{ex.Message}\n\n" +
                           "请检查:\n" +
                           "1. API Key 是否有效\n" +
                           "2. 网络连接是否正常\n" +
                           "3. DeepSeek 服务是否可用\n\n" +
                           "详细错误请查看日志文件: Logs/app-*.log";
                }
            });

            // 添加AI回复
            AddAIMessage(response);

            // 滚动到底部
            MessageScroll.ScrollToBottom();

            StatusText.Text = "AI助手就绪";
            AIStatusText.Text = "AI就绪 ✅";
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AIAssistant] 发送消息失败");
            AddAIMessage($"❌ 处理失败: {ex.Message}");
            AIStatusText.Text = "错误 ❌";
        }
        finally
        {
            _isProcessing = false;
            SendButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 构建上下文信息
    /// </summary>
    private string BuildContext()
    {
        var account = _accountManager.SimulatedAccount ?? _accountManager.LiveAccount;

        if (account == null)
        {
            return "账户信息不可用";
        }

        return $@"
当前账户信息:
- 类型: {account.Type}
- 净值: {account.NetValue:F2} USDT
- 可用余额: {account.AvailableBalance:F2} USDT
- 持仓价值: {account.PositionValue:F2} USDT
- 总盈亏: {account.TotalPnL:F2} USDT
- 交易次数: {account.TotalTrades} 笔
- 胜率: {account.WinRate:P0}
- 最大回撤: {account.MaxDrawdown:P2}
- 夏普比率: {account.SharpeRatio:F2}

最近消息历史:
{string.Join("\n", _messages.TakeLast(5).Select(m => $"{m.Role}: {m.Content}"))}
";
    }

    /// <summary>
    /// 添加用户消息
    /// </summary>
    private void AddUserMessage(string content)
    {
        _messages.Add(new ChatMessage
        {
            Role = "用户",
            Content = content,
            Time = DateTime.Now.ToString("HH:mm:ss")
        });
    }

    /// <summary>
    /// 添加AI消息
    /// </summary>
    private void AddAIMessage(string content)
    {
        _messages.Add(new ChatMessage
        {
            Role = "AI助手",
            Content = content,
            Time = DateTime.Now.ToString("HH:mm:ss")
        });
    }

    /// <summary>
    /// 获取风险等级
    /// </summary>
    private string GetRiskLevel(Models.TradingAccount account)
    {
        double availableRatio = (double)(account.AvailableBalance / account.NetValue);
        double absDrawdown = Math.Abs(account.MaxDrawdown);

        if (availableRatio >= 0.7 && absDrawdown <= 0.1)
        {
            return "低风险 ✅";
        }

        if (availableRatio >= 0.5 && absDrawdown <= 0.2)
        {
            return "中等风险 ⚠️";
        }

        return "高风险 ❌";
    }
}

/// <summary>
/// 聊天消息
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}
