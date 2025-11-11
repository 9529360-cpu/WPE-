# 🚀 DeepSeek API 快速配置 - 立即使用！

**目标**: 3分钟内完成DeepSeek API配置并启用AI功能

---

## 📝 您的API Key

```
sk-00280192bd6d4971be8eab1cb80b4a13
```

---

## ⚡ 方法1: 一键配置 (推荐)

### Step 1: 打开PowerShell

```
按 Win + X → 选择 "Windows PowerShell"
```

### Step 2: 运行配置脚本

```powershell
cd "C:\Users\wangh\source\repos\9529360-cpu\WPE-"

.\Scripts\Setup-DeepSeekAPI.ps1 -ApiKey "sk-00280192bd6d4971be8eab1cb80b4a13"
```

### Step 3: 等待完成

```
脚本会自动:
✅ 设置环境变量
✅ 更新配置文件
✅ 测试API连接
```

### Step 4: 重启应用

```powershell
# 关闭当前应用
# 然后运行:
dotnet run
```

**完成！** 🎉

---

## 🔧 方法2: 手动配置 (5分钟)

### Option A: 使用环境变量

#### Windows PowerShell

```powershell
# 设置环境变量
[System.Environment]::SetEnvironmentVariable("TRADING_DEEPSEEK_API_KEY", "sk-00280192bd6d4971be8eab1cb80b4a13", "User")

# 验证
$env:TRADING_DEEPSEEK_API_KEY
```

#### Windows CMD

```cmd
setx TRADING_DEEPSEEK_API_KEY "sk-00280192bd6d4971be8eab1cb80b4a13"
```

### Option B: 修改配置文件

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

保存文件，重启应用。

---

## ✅ 验证配置

### 1. 检查日志

启动应用后，查看日志：

```
位置: bin\Debug\net8.0-windows\Logs\app-[日期].log

应该看到:
✅ [ConfigService] 使用环境变量的 DeepSeek API Key
✅ [AIAssistant] AI助手已启用
```

### 2. 测试AI助手

1. 打开应用
2. 点击左侧 [AI智能助手]
3. 输入: "你好"
4. 点击 [发送]
5. AI应该正常回复

### 3. 检查状态

```
右上角应该显示: "AI就绪 ✅"
```

---

## 🎯 启用AI交易

### 1. 进入统一仪表盘

```
左侧导航 → [📊 统一仪表盘]
```

### 2. 启动AI

```
点击 [▶️ 启动AI] 按钮
```

### 3. 确认启动

```
确认对话框 → 点击 [是]
```

### 4. 监控运行

```
查看:
• 实时信号
• 持仓变化
• 收益情况
```

---

## 🐛 常见问题

### Q: 脚本执行被阻止？

**错误**: "无法加载，因为在此系统上禁止运行脚本"

**解决**:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

---

### Q: API Key仍然无效？

**检查清单**:
- [ ] API Key正确复制（没有多余空格）
- [ ] 已重启应用
- [ ] 网络连接正常
- [ ] DeepSeek服务可用

**重新获取API Key**:
1. 访问: https://platform.deepseek.com/
2. 登录账号
3. API Keys → Create new key
4. 复制新的API Key
5. 重新运行配置脚本

---

### Q: AI助手显示"演示模式"？

**原因**: API Key未正确加载

**解决**:
1. 检查环境变量: `$env:TRADING_DEEPSEEK_API_KEY`
2. 检查配置文件: `appsettings.json`
3. 重启应用
4. 查看日志文件

---

### Q: AI交易无法启动？

**原因**: 依赖服务未初始化

**解决**:
1. 确保DeepSeek API配置正确
2. 确保Binance API配置正确（如果要实盘交易）
3. 查看日志: `Logs/app-*.log`
4. 重启应用

---

## 📞 获取帮助

### 文档

- **配置指南**: `Docs/DeepSeek_API_Configuration_Guide.md`
- **快速修复**: `Docs/DeepSeek_API_Quick_Fix_Guide.md`
- **完整指南**: `README.md`

### 日志位置

```
bin\Debug\net8.0-windows\Logs\app-[日期].log
```

### 检查清单

- [ ] API Key格式正确 (sk-开头，32位字符)
- [ ] 环境变量已设置
- [ ] 配置文件已更新
- [ ] 应用已重启
- [ ] 日志显示AI已启用
- [ ] AI助手可以正常对话

---

## 🎉 快速开始命令

```powershell
# 1. 配置API Key
.\Scripts\Setup-DeepSeekAPI.ps1 -ApiKey "sk-00280192bd6d4971be8eab1cb80b4a13"

# 2. 重启应用
dotnet run

# 3. 测试AI助手
# 打开应用 → AI智能助手 → 发送消息

# 4. 启用AI交易 (可选)
# 统一仪表盘 → 启动AI
```

---

**配置完成！开始您的AI交易之旅！** 🚀✨

**预计时间**: 3分钟  
**成功率**: 99%  
**难度**: ⭐ 非常简单
