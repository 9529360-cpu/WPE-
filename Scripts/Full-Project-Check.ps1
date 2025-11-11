# ============================================
# 全项目系统性检查脚本
# ============================================
# 用途: 检查所有代码逻辑和UI完整性
# 作者: AI Assistant
# 日期: 2025-01-11
# ============================================

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  全项目系统性检查" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

$RootPath = Split-Path -Parent $PSScriptRoot
$IssuesFound = @()
$ChecksPassed = 0
$ChecksFailed = 0

# ============================================
# 1. 检查配置文件完整性
# ============================================
Write-Host "━━━ 1. 检查配置文件 ━━━" -ForegroundColor Yellow

$AppsettingsPath = Join-Path $RootPath "appsettings.json"
if (Test-Path $AppsettingsPath) {
    Write-Host "✅ appsettings.json 存在" -ForegroundColor Green
    $ChecksPassed++
    
    try {
        $Config = Get-Content $AppsettingsPath -Raw | ConvertFrom-Json
        
        # 检查必要的配置节
        $RequiredSections = @("App", "Logging", "Trading", "Api", "Risk", "Backtest", "AI", "Database")
        foreach ($Section in $RequiredSections) {
            if ($Config.PSObject.Properties.Name -contains $Section) {
                Write-Host "  ✓ 配置节 '$Section' 存在" -ForegroundColor Gray
            }
            else {
                $Issue = "⚠️  缺少配置节: $Section"
                Write-Host $Issue -ForegroundColor Red
                $IssuesFound += $Issue
                $ChecksFailed++
            }
        }
    }
    catch {
        $Issue = "❌ appsettings.json 格式错误: $($_.Exception.Message)"
        Write-Host $Issue -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}
else {
    $Issue = "❌ appsettings.json 不存在"
    Write-Host $Issue -ForegroundColor Red
    $IssuesFound += $Issue
    $ChecksFailed++
}

Write-Host ""

# ============================================
# 2. 检查Services层完整性
# ============================================
Write-Host "━━━ 2. 检查Services层 ━━━" -ForegroundColor Yellow

$ServicesPath = Join-Path $RootPath "Services"
$RequiredServices = @(
    "BinanceApiClient.cs",
    "BinanceStreamClient.cs",
    "ConfigurationService.cs",
    "DataCacheService.cs",
    "LogService.cs",
    "ServiceLocator.cs",
    "TradingAccountManager.cs",
    "PositionManager.cs",
    "RiskEngine.cs"
)

foreach ($Service in $RequiredServices) {
    $ServicePath = Join-Path $ServicesPath $Service
    if (Test-Path $ServicePath) {
        Write-Host "  ✓ $Service" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "  ❌ 缺少: $Service"
        Write-Host $Issue -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# ============================================
# 3. 检查Modules层UI完整性
# ============================================
Write-Host "━━━ 3. 检查Modules层UI ━━━" -ForegroundColor Yellow

$ModulesPath = Join-Path $RootPath "Modules"
$RequiredModules = @(
    @{Name="Dashboard"; Files=@("UnifiedDashboardView.xaml", "UnifiedDashboardView.xaml.cs")},
    @{Name="AI"; Files=@("AIAssistantView.xaml", "AIAssistantView.xaml.cs", "AICentralCoordinatorView.xaml", "AICentralCoordinatorView.xaml.cs")},
    @{Name="Settings"; Files=@("ApiManagerView.xaml", "ApiManagerView.xaml.cs", "SettingsView.xaml", "SettingsView.xaml.cs")},
    @{Name="Trade"; Files=@("TradeView.xaml", "TradeView.xaml.cs", "PositionsOrdersView.xaml", "PositionsOrdersView.xaml.cs")},
    @{Name="Market"; Files=@("RealtimeView.xaml", "RealtimeView.xaml.cs")},
    @{Name="Account"; Files=@("AccountFundsView.xaml", "AccountFundsView.xaml.cs")}
)

foreach ($Module in $RequiredModules) {
    $ModulePath = Join-Path $ModulesPath $Module.Name
    Write-Host "  检查模块: $($Module.Name)" -ForegroundColor Cyan
    
    foreach ($File in $Module.Files) {
        $FilePath = Join-Path $ModulePath $File
        if (Test-Path $FilePath) {
            Write-Host "    ✓ $File" -ForegroundColor Green
            $ChecksPassed++
        }
        else {
            $Issue = "    ❌ 缺少: $($Module.Name)\$File"
            Write-Host $Issue -ForegroundColor Red
            $IssuesFound += $Issue
            $ChecksFailed++
        }
    }
}

Write-Host ""

# ============================================
# 4. 检查Models层完整性
# ============================================
Write-Host "━━━ 4. 检查Models层 ━━━" -ForegroundColor Yellow

$ModelsPath = Join-Path $RootPath "Models"
$RequiredModels = @(
    "TradingAccount.cs",
    "AccountProfile.cs",
    "OrderModels.cs",
    "PerformanceModels.cs",
    "Configuration\TradingConfig.cs",
    "Configuration\ApiConfig.cs",
    "Configuration\RiskConfig.cs",
    "Configuration\BacktestConfig.cs"
)

foreach ($Model in $RequiredModels) {
    $ModelPath = Join-Path $ModelsPath $Model
    if (Test-Path $ModelPath) {
        Write-Host "  ✓ $Model" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "  ❌ 缺少: $Model"
        Write-Host $Issue -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# ============================================
# 5. 检查MainWindow和App.xaml
# ============================================
Write-Host "━━━ 5. 检查主窗口 ━━━" -ForegroundColor Yellow

$MainFiles = @("MainWindow.xaml", "MainWindow.xaml.cs", "App.xaml", "App.xaml.cs")
foreach ($File in $MainFiles) {
    $FilePath = Join-Path $RootPath $File
    if (Test-Path $FilePath) {
        Write-Host "  ✓ $File" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "  ❌ 缺少: $File"
        Write-Host $Issue -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# ============================================
# 6. 检查编译
# ============================================
Write-Host "━━━ 6. 编译检查 ━━━" -ForegroundColor Yellow

$CsprojPath = Join-Path $RootPath "币安量化机器人.csproj"
if (Test-Path $CsprojPath) {
    Write-Host "  正在编译项目..." -ForegroundColor Cyan
    
    $BuildOutput = dotnet build $CsprojPath --verbosity quiet 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✅ 编译成功" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "  ❌ 编译失败"
        Write-Host $Issue -ForegroundColor Red
        Write-Host "  输出:" -ForegroundColor Gray
        $BuildOutput | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# ============================================
# 7. 生成报告
# ============================================
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  检查结果汇总" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "✅ 通过检查: $ChecksPassed 项" -ForegroundColor Green
Write-Host "❌ 失败检查: $ChecksFailed 项" -ForegroundColor Red
Write-Host ""

if ($IssuesFound.Count -gt 0) {
    Write-Host "发现的问题:" -ForegroundColor Yellow
    foreach ($Issue in $IssuesFound) {
        Write-Host "  • $Issue" -ForegroundColor Red
    }
}
else {
    Write-Host "🎉 所有检查通过！" -ForegroundColor Green
}

Write-Host ""

# 保存报告
$ReportPath = Join-Path $RootPath "Docs" "Project_Health_Check_Report.md"
$ReportContent = @"
# 项目健康检查报告

生成时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

## 检查统计

- ✅ 通过: $ChecksPassed 项
- ❌ 失败: $ChecksFailed 项

## 检查项目

### 1. 配置文件
- appsettings.json

### 2. Services层
- BinanceApiClient.cs
- ConfigurationService.cs
- TradingAccountManager.cs
- 等...

### 3. Modules层UI
- Dashboard
- AI
- Settings
- Trade
- Market
- Account

### 4. Models层
- TradingAccount
- Configuration类
- 等...

### 5. 主窗口
- MainWindow.xaml
- App.xaml

### 6. 编译状态
$(if ($LASTEXITCODE -eq 0) { "✅ 编译成功" } else { "❌ 编译失败" })

"@

if ($IssuesFound.Count -gt 0) {
    $ReportContent += "`n## 发现的问题`n`n"
    foreach ($Issue in $IssuesFound) {
        $ReportContent += "- $Issue`n"
    }
}

New-Item -Path (Split-Path $ReportPath -Parent) -ItemType Directory -Force | Out-Null
Set-Content -Path $ReportPath -Value $ReportContent -Encoding UTF8

Write-Host "报告已保存: $ReportPath" -ForegroundColor Cyan
Write-Host ""
