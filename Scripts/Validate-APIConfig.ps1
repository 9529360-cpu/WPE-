# API 配置验证脚本
# 用于验证 Binance 和 DeepSeek API 凭证是否正确配置

$ErrorActionPreference = "Stop"

Write-Host "=" * 80 -ForegroundColor Cyan
Write-Host "API 配置验证工具" -ForegroundColor Cyan
Write-Host "=" * 80 -ForegroundColor Cyan
Write-Host ""

# 1. 检查配置文件
Write-Host "[1/4] 检查配置文件..." -ForegroundColor Yellow

$configPath = "appsettings.json"
if (-not (Test-Path $configPath)) {
    Write-Host "❌ 配置文件不存在: $configPath" -ForegroundColor Red
    exit 1
}

try {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    Write-Host "✅ 配置文件加载成功" -ForegroundColor Green
} catch {
    Write-Host "❌ 配置文件格式错误: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# 2. 验证 Binance API 配置
Write-Host "[2/4] 验证 Binance API 配置..." -ForegroundColor Yellow

$binanceApiKey = $config.Api.Binance.ApiKey
$binanceSecretKey = $config.Api.Binance.SecretKey

if ([string]::IsNullOrWhiteSpace($binanceApiKey)) {
    Write-Host "❌ Binance API Key 未配置" -ForegroundColor Red
    $binanceValid = $false
} elseif ($binanceApiKey.Contains("••")) {
    Write-Host "⚠️  Binance API Key 包含掩码符号（可能不完整）" -ForegroundColor Yellow
    Write-Host "   当前值: $binanceApiKey" -ForegroundColor Gray
    $binanceValid = $false
} else {
    Write-Host "✅ Binance API Key 已配置" -ForegroundColor Green
    Write-Host "   长度: $($binanceApiKey.Length) 字符" -ForegroundColor Gray
    Write-Host "   前缀: $($binanceApiKey.Substring(0, [Math]::Min(8, $binanceApiKey.Length)))..." -ForegroundColor Gray
    $binanceValid = $true
}

if ([string]::IsNullOrWhiteSpace($binanceSecretKey)) {
    Write-Host "❌ Binance Secret Key 未配置" -ForegroundColor Red
    $binanceValid = $false
} elseif ($binanceSecretKey.Contains("••")) {
    Write-Host "⚠️  Binance Secret Key 包含掩码符号（可能不完整）" -ForegroundColor Yellow
    $binanceValid = $false
} else {
    Write-Host "✅ Binance Secret Key 已配置" -ForegroundColor Green
    Write-Host "   长度: $($binanceSecretKey.Length) 字符" -ForegroundColor Gray
    $binanceValid = $true
}

Write-Host ""

# 3. 验证 DeepSeek API 配置
Write-Host "[3/4] 验证 DeepSeek API 配置..." -ForegroundColor Yellow

$deepSeekApiKey = $config.AI.DeepSeek.ApiKey

if ([string]::IsNullOrWhiteSpace($deepSeekApiKey)) {
    Write-Host "❌ DeepSeek API Key 未配置" -ForegroundColor Red
    $deepSeekValid = $false
} elseif ($deepSeekApiKey.Contains("••")) {
    Write-Host "⚠️  DeepSeek API Key 包含掩码符号（可能不完整）" -ForegroundColor Yellow
    Write-Host "   当前值: $deepSeekApiKey" -ForegroundColor Gray
    $deepSeekValid = $false
} elseif (-not $deepSeekApiKey.StartsWith("sk-")) {
    Write-Host "❌ DeepSeek API Key 格式错误（应以 'sk-' 开头）" -ForegroundColor Red
    Write-Host "   当前值: $deepSeekApiKey" -ForegroundColor Gray
    $deepSeekValid = $false
} else {
    Write-Host "✅ DeepSeek API Key 已配置" -ForegroundColor Green
    Write-Host "   格式: 正确（以 sk- 开头）" -ForegroundColor Gray
    Write-Host "   长度: $($deepSeekApiKey.Length) 字符" -ForegroundColor Gray
    $deepSeekValid = $true
}

# 检查 AI 启用状态
$aiEnabled = $config.AI.EnableAITrading
if ($aiEnabled -eq $true) {
    Write-Host "✅ AI 交易已启用" -ForegroundColor Green
} else {
    Write-Host "⚠️  AI 交易未启用（EnableAITrading = $aiEnabled）" -ForegroundColor Yellow
}

Write-Host ""

# 4. 总结报告
Write-Host "[4/4] 配置验证总结" -ForegroundColor Yellow
Write-Host ""

Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "验证结果" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

if ($binanceValid) {
    Write-Host "✅ Binance API    - 配置正确" -ForegroundColor Green
} else {
    Write-Host "❌ Binance API    - 配置错误或不完整" -ForegroundColor Red
}

if ($deepSeekValid) {
    Write-Host "✅ DeepSeek API   - 配置正确" -ForegroundColor Green
} else {
    Write-Host "❌ DeepSeek API   - 配置错误或不完整" -ForegroundColor Red
}

Write-Host ""

if ($binanceValid -and $deepSeekValid) {
    Write-Host "🎉 所有 API 配置验证通过！" -ForegroundColor Green
    Write-Host ""
    Write-Host "接下来的步骤：" -ForegroundColor Cyan
    Write-Host "1. 启动应用: dotnet run" -ForegroundColor White
    Write-Host "2. 打开仪表盘，切换到 'AI' Tab" -ForegroundColor White
    Write-Host "3. 选择账户类型（模拟/实盘）" -ForegroundColor White
    Write-Host "4. 点击 '▶️ 启动AI' 开始交易" -ForegroundColor White
    Write-Host ""
    exit 0
} else {
    Write-Host "⚠️  部分 API 配置不完整" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "修复方法：" -ForegroundColor Cyan
    
    if (-not $binanceValid) {
        Write-Host ""
        Write-Host "Binance API 配置：" -ForegroundColor Yellow
        Write-Host "1. 前往 Binance 账户设置 > API 管理" -ForegroundColor White
        Write-Host "2. 创建新的 API Key（需要启用合约交易权限）" -ForegroundColor White
        Write-Host "3. 复制 API Key 和 Secret Key" -ForegroundColor White
        Write-Host "4. 在应用中：设置 > API 管理 > 保存" -ForegroundColor White
    }
    
    if (-not $deepSeekValid) {
        Write-Host ""
        Write-Host "DeepSeek API 配置：" -ForegroundColor Yellow
        Write-Host "1. 前往 DeepSeek 控制台: https://platform.deepseek.com/" -ForegroundColor White
        Write-Host "2. 创建 API Key（确保有额度）" -ForegroundColor White
        Write-Host "3. 复制 API Key（格式: sk-xxxxxxxx）" -ForegroundColor White
        Write-Host "4. 在应用中：设置 > API 管理 > 保存" -ForegroundColor White
    }
    
    Write-Host ""
    exit 1
}
