# DeepSeek API 配置指南

## 🎯 问题诊断

根据日志分析，发现以下问题：

1. **API Key无效**: `sk-00280192bd6d4971be8eab1cb80b4a13` 认证失败
2. **非ASCII字符**: API Key包含非ASCII字符被过滤
3. **AI初始化跳过**: UnifiedDashboard跳过AI初始化

---

## ✅ 解决方案

### 方案1: 使用环境变量 (推荐)

#### Windows PowerShell

```powershell
# 设置DeepSeek API Key
[System.Environment]::SetEnvironmentVariable("TRADING_DEEPSEEK_API_KEY", "sk-00280192bd6d4971be8eab1cb80b4a13", "User")

# 设置Binance API Key (如果有)
[System.Environment]::SetEnvironmentVariable("TRADING_BINANCE_API_KEY", "your_binance_api_key", "User")
[System.Environment]::SetEnvironmentVariable("TRADING_BINANCE_SECRET_KEY", "your_binance_secret_key", "User")

# 验证设置
$env:TRADING_DEEPSEEK_API_KEY

# 重启应用生效
```

#### Windows CMD

```cmd
:: 设置环境变量
setx TRADING_DEEPSEEK_API_KEY "sk-00280192bd6d4971be8eab1cb80b4a13"

:: 验证
echo %TRADING_DEEPSEEK_API_KEY%

:: 重启应用
```

---

### 方案2: 直接修改配置文件

编辑 `appsettings.json`:

```json
{
  "AI": {
    "DeepSeek": {
      "ApiKey": "sk-00280192bd6d4971be8eab1cb80b4a13",
      "Model": "deepseek-chat",
      "Temperature": 0.3,
      "MaxTokens": 2000
    },
    "EnableAITrading": true
  }
}
```

**注意**: 确保API Key没有多余的空格和特殊字符！

---

### 方案3: 使用自动配置脚本

运行 `Scripts/Setup-DeepSeekAPI.ps1`:

```powershell
.\Scripts\Setup-DeepSeekAPI.ps1 -ApiKey "sk-00280192bd6d4971be8eab1cb80b4a13"
```

---

## 🔍 验证API Key

### 方法1: 使用PowerShell测试

```powershell
$apiKey = "sk-00280192bd6d4971be8eab1cb80b4a13"
$headers = @{
    "Authorization" = "Bearer $apiKey"
    "Content-Type" = "application/json"
}

$body = @{
    model = "deepseek-chat"
    messages = @(
        @{
            role = "user"
            content = "Hello"
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-RestMethod -Uri "https://api.deepseek.com/v1/chat/completions" -Method Post -Headers $headers -Body $body
    Write-Host "✅ API Key 有效！" -ForegroundColor Green
    Write-Host "响应: $($response.choices[0].message.content)"
} catch {
    Write-Host "❌ API Key 无效或请求失败" -ForegroundColor Red
    Write-Host "错误: $($_.Exception.Message)"
}
```

### 方法2: 在应用内测试

1. 打开应用
2. 进入 [设置] → [API管理]
3. 找到 "DeepSeek AI" 部分
4. 输入API Key: `sk-00280192bd6d4971be8eab1cb80b4a13`
5. 点击 [测试连接]
6. 查看结果

---

## 🚨 常见错误

### 错误1: "Authentication Fails, Your api key is invalid"

**原因**:
- API Key输入错误
- API Key已过期
- API Key包含隐藏字符

**解决**:
1. 重新复制API Key
2. 确认没有多余空格
3. 检查API Key是否以 `sk-` 开头
4. 在DeepSeek平台重新生成

---

### 错误2: "API Key 包含非 ASCII 字符"

**原因**:
- 从配置文件读取时包含了隐藏字符(如 `•`)
- 复制粘贴时带入了特殊字符

**解决**:
```csharp
// 代码已自动过滤非ASCII字符
// 如果仍有问题，手动清理:
string cleanKey = new string(apiKey.Where(c => c <= 127).ToArray());
```

---

### 错误3: "AI交易初始化已跳过"

**原因**:
- DeepSeek API Key未配置
- 依赖服务初始化失败

**解决**:
1. 确保API Key已正确配置
2. 重启应用
3. 查看日志: `Logs/app-*.log`

---

## 📝 完整配置清单

### 1. DeepSeek API Key 配置

```json
{
  "AI": {
    "DeepSeek": {
      "ApiKey": "sk-00280192bd6d4971be8eab1cb80b4a13",
      "Model": "deepseek-chat",
      "Temperature": 0.3,
      "MaxTokens": 2000
    },
    "EnableAITrading": true
  }
}
```

### 2. Binance API配置 (可选)

```json
{
  "Api": {
    "Binance": {
      "ApiKey": "your_binance_api_key",
      "SecretKey": "your_binance_secret_key",
      "RestEndpoint": "https://fapi.binance.com",
      "WebSocketEndpoint": "wss://fstream.binance.com"
    }
  }
}
```

### 3. 交易配置

```json
{
  "Trading": {
    "MinConfidenceThreshold": 0.7,
    "MaxPositionSizePercent": 0.1,
    "DefaultStopLossPercent": 0.02,
    "EnableAutoTrading": true
  }
}
```

---

## 🎯 验证步骤

1. **配置API Key**
   ```powershell
   # 环境变量方式
   setx TRADING_DEEPSEEK_API_KEY "sk-00280192bd6d4971be8eab1cb80b4a13"
   
   # 或修改 appsettings.json
   ```

2. **重启应用**
   ```
   关闭应用 → 重新打开
   ```

3. **测试AI助手**
   ```
   进入 AI智能助手 → 发送测试消息
   ```

4. **查看日志**
   ```
   检查 Logs/app-*.log
   应该看到: "[AIAssistant] AI助手已启用"
   ```

---

## 💡 推荐配置流程

```
1. 获取有效的DeepSeek API Key
   ↓
2. 设置环境变量 (推荐)
   setx TRADING_DEEPSEEK_API_KEY "sk-your-key-here"
   ↓
3. 重启应用
   ↓
4. 测试AI功能
   AI智能助手 → 发送消息
   ↓
5. 启用AI交易 (可选)
   统一仪表盘 → 启动AI
```

---

## 🆘 仍然无法工作?

### 检查清单

- [ ] API Key正确且有效
- [ ] 没有多余空格和特殊字符
- [ ] 已重启应用
- [ ] 网络连接正常
- [ ] DeepSeek服务可用
- [ ] 查看了日志文件

### 日志位置

```
Windows: C:\Users\[用户名]\source\repos\9529360-cpu\WPE-\bin\Debug\net8.0-windows\Logs\app-*.log
```

### 获取帮助

1. 查看日志详细错误
2. 截图错误信息
3. 查看文档: `DeepSeek_API_Quick_Fix_Guide.md`

---

**配置指南完成！祝您配置顺利！** 🚀✨
