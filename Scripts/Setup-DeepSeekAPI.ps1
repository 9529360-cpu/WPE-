<#
.SYNOPSIS
    自动配置DeepSeek API Key

.DESCRIPTION
    此脚本帮助您快速配置DeepSeek API Key到环境变量和配置文件

.PARAMETER ApiKey
    DeepSeek API Key (以sk-开头)

.PARAMETER ConfigOnly
    仅更新配置文件，不设置环境变量

.PARAMETER EnvOnly
    仅设置环境变量，不更新配置文件

.EXAMPLE
    .\Setup-DeepSeekAPI.ps1 -ApiKey "sk-00280192bd6d4971be8eab1cb80b4a13"

.EXAMPLE
    .\Setup-DeepSeekAPI.ps1 -ApiKey "sk-your-key" -ConfigOnly

.NOTES
    Author: AI Trading Bot
    Version: 1.0.0
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,
    
    [switch]$ConfigOnly,
    [switch]$EnvOnly
)

# 设置控制台编码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "    DeepSeek API 自动配置工具" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# 验证API Key格式
if (-not $ApiKey.StartsWith("sk-")) {
    Write-Host "❌ 错误: API Key 必须以 'sk-' 开头" -ForegroundColor Red
    exit 1
}

if ($ApiKey.Length -lt 20) {
    Write-Host "❌ 错误: API Key 长度不正确（太短）" -ForegroundColor Red
    exit 1
}

# 清理API Key（移除空格和非ASCII字符）
$CleanApiKey = -join ($ApiKey.ToCharArray() | Where-Object { [int]$_ -le 127 })

if ($CleanApiKey -ne $ApiKey) {
    Write-Host "⚠️  警告: API Key 包含非ASCII字符，已自动清理" -ForegroundColor Yellow
    $ApiKey = $CleanApiKey
}

Write-Host "✅ API Key 格式验证通过: $($ApiKey.Substring(0, 8))...****" -ForegroundColor Green
Write-Host ""

# 1. 设置环境变量 (除非指定ConfigOnly)
if (-not $ConfigOnly) {
    Write-Host "[1/3] 设置环境变量..." -ForegroundColor Cyan
    
    try {
        # 设置用户级环境变量
        [System.Environment]::SetEnvironmentVariable("TRADING_DEEPSEEK_API_KEY", $ApiKey, "User")
        
        # 同时设置当前会话的环境变量
        $env:TRADING_DEEPSEEK_API_KEY = $ApiKey
        
        Write-Host "✅ 环境变量已设置: TRADING_DEEPSEEK_API_KEY" -ForegroundColor Green
        
        # 验证
        $storedKey = [System.Environment]::GetEnvironmentVariable("TRADING_DEEPSEEK_API_KEY", "User")
        if ($storedKey -eq $ApiKey) {
            Write-Host "✅ 环境变量验证通过" -ForegroundColor Green
        } else {
            Write-Host "⚠️  警告: 环境变量可能未正确保存" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "❌ 设置环境变量失败: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Write-Host ""
}

# 2. 更新配置文件 (除非指定EnvOnly)
if (-not $EnvOnly) {
    Write-Host "[2/3] 更新配置文件..." -ForegroundColor Cyan
    
    # 查找配置文件
    $ConfigPath = Join-Path $PSScriptRoot "..\appsettings.json"
    
    if (Test-Path $ConfigPath) {
        try {
            # 读取配置文件
            $Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            
            # 确保AI配置节点存在
            if (-not $Config.PSObject.Properties["AI"]) {
                $Config | Add-Member -MemberType NoteProperty -Name "AI" -Value ([PSCustomObject]@{
                    DeepSeek = [PSCustomObject]@{
                        ApiKey = $ApiKey
                        Model = "deepseek-chat"
                        Temperature = 0.3
                        MaxTokens = 2000
                    }
                    EnableAITrading = $true
                })
            }
            else {
                # 更新API Key
                if (-not $Config.AI.PSObject.Properties["DeepSeek"]) {
                    $Config.AI | Add-Member -MemberType NoteProperty -Name "DeepSeek" -Value ([PSCustomObject]@{
                        ApiKey = $ApiKey
                        Model = "deepseek-chat"
                        Temperature = 0.3
                        MaxTokens = 2000
                    })
                }
                else {
                    $Config.AI.DeepSeek.ApiKey = $ApiKey
                }
                
                # 启用AI交易
                if ($Config.AI.PSObject.Properties["EnableAITrading"]) {
                    $Config.AI.EnableAITrading = $true
                }
                else {
                    $Config.AI | Add-Member -MemberType NoteProperty -Name "EnableAITrading" -Value $true
                }
            }
            
            # 保存配置文件（保持格式）
            $Config | ConvertTo-Json -Depth 10 | Set-Content $ConfigPath -Encoding UTF8
            
            Write-Host "✅ 配置文件已更新: $ConfigPath" -ForegroundColor Green
        }
        catch {
            Write-Host "❌ 更新配置文件失败: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    else {
        Write-Host "⚠️  警告: 未找到配置文件: $ConfigPath" -ForegroundColor Yellow
    }
    
    Write-Host ""
}

# 3. 测试API Key
Write-Host "[3/3] 测试API Key..." -ForegroundColor Cyan

try {
    $Headers = @{
        "Authorization" = "Bearer $ApiKey"
        "Content-Type" = "application/json"
    }
    
    $Body = @{
        model = "deepseek-chat"
        messages = @(
            @{
                role = "user"
                content = "Hello"
            }
        )
        max_tokens = 10
    } | ConvertTo-Json -Depth 10
    
    $Response = Invoke-RestMethod -Uri "https://api.deepseek.com/v1/chat/completions" `
                                   -Method Post `
                                   -Headers $Headers `
                                   -Body $Body `
                                   -TimeoutSec 10
    
    Write-Host "✅ API Key 测试通过！" -ForegroundColor Green
    Write-Host "✅ DeepSeek API 连接正常" -ForegroundColor Green
    Write-Host "✅ AI响应: $($Response.choices[0].message.content)" -ForegroundColor Green
}
catch {
    $ErrorMessage = $_.Exception.Message
    
    if ($ErrorMessage -like "*401*" -or $ErrorMessage -like "*Unauthorized*") {
        Write-Host "❌ API Key 无效或已过期" -ForegroundColor Red
        Write-Host "   请访问 https://platform.deepseek.com/ 获取新的API Key" -ForegroundColor Yellow
    }
    elseif ($ErrorMessage -like "*403*") {
        Write-Host "❌ API Key 权限不足" -ForegroundColor Red
    }
    elseif ($ErrorMessage -like "*429*") {
        Write-Host "⚠️  API 请求限额已用完" -ForegroundColor Yellow
    }
    else {
        Write-Host "❌ API 测试失败: $ErrorMessage" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "    配置完成！" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "📝 下一步操作:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. 重启应用以使环境变量生效" -ForegroundColor White
Write-Host "   dotnet run" -ForegroundColor Gray
Write-Host ""
Write-Host "2. 测试AI助手功能" -ForegroundColor White
Write-Host "   进入 [AI智能助手] → 发送测试消息" -ForegroundColor Gray
Write-Host ""
Write-Host "3. 启用AI交易 (可选)" -ForegroundColor White
Write-Host "   进入 [统一仪表盘] → 点击 ▶️ 启动AI" -ForegroundColor Gray
Write-Host ""

if (-not $ConfigOnly) {
    Write-Host "💡 提示: 环境变量已设置为用户级别，应用重启后自动生效" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "📖 更多帮助: Docs\DeepSeek_API_Configuration_Guide.md" -ForegroundColor Cyan
Write-Host ""

Write-Host "按任意键退出..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
