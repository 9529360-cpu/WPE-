# ============================================
# Full Project Health Check Script (ASCII only)
# ============================================
# Purpose: Validate config, service/UI modules, and build status
# Author: AI Assistant
# Date: 2025-11-11
# ============================================

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  Full Project Health Check" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""

$RootPath = Split-Path -Parent $PSScriptRoot
$IssuesFound = @()
$ChecksPassed = 0
$ChecksFailed = 0

# 1. Check appsettings.json
Write-Host "--- 1. Config file ---" -ForegroundColor Yellow

$AppsettingsPath = Join-Path $RootPath "appsettings.json"
if (Test-Path $AppsettingsPath) {
    Write-Host "OK appsettings.json exists" -ForegroundColor Green
    $ChecksPassed++

    try {
        $raw = Get-Content $AppsettingsPath -Raw -Encoding UTF8
        # PowerShell 5 ConvertFrom-Json lacks -Depth; fallback to direct parse
        $Config = $raw | ConvertFrom-Json
        $RequiredSections = @("App", "Logging", "Trading", "Api", "Risk", "Backtest", "AI", "Database")
        foreach ($Section in $RequiredSections) {
            if ($Config.PSObject.Properties.Name -contains $Section) {
                Write-Host "  + section '$Section' found" -ForegroundColor Gray
            }
            else {
                $Issue = "Missing config section: $Section"
                Write-Host "  - $Issue" -ForegroundColor Red
                $IssuesFound += $Issue
                $ChecksFailed++
            }
        }
    }
    catch {
        $Issue = "appsettings.json invalid json: $($_.Exception.Message)"
        Write-Host "  - $Issue" -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}
else {
    $Issue = "appsettings.json not found"
    Write-Host "  - $Issue" -ForegroundColor Red
    $IssuesFound += $Issue
    $ChecksFailed++
}

Write-Host ""

# 2. Check Services layer
Write-Host "--- 2. Services layer ---" -ForegroundColor Yellow

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
        Write-Host "  + $Service" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "Missing service: $Service"
        Write-Host "  - $Issue" -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# 3. Check Modules UI layer
Write-Host "--- 3. Modules UI ---" -ForegroundColor Yellow

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
    Write-Host "  Checking module: $($Module.Name)" -ForegroundColor Cyan

    foreach ($File in $Module.Files) {
        $FilePath = Join-Path $ModulePath $File
        if (Test-Path $FilePath) {
            Write-Host "    + $File" -ForegroundColor Green
            $ChecksPassed++
        }
        else {
            $Issue = "Missing module file: $($Module.Name)\$File"
            Write-Host "    - $Issue" -ForegroundColor Red
            $IssuesFound += $Issue
            $ChecksFailed++
        }
    }
}

Write-Host ""

# 4. Check Models layer
Write-Host "--- 4. Models layer ---" -ForegroundColor Yellow

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
        Write-Host "  + $Model" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "Missing model: $Model"
        Write-Host "  - $Issue" -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# 5. Check MainWindow and App.xaml
Write-Host "--- 5. App entry ---" -ForegroundColor Yellow

$MainFiles = @("MainWindow.xaml", "MainWindow.xaml.cs", "App.xaml", "App.xaml.cs")
foreach ($File in $MainFiles) {
    $FilePath = Join-Path $RootPath $File
    if (Test-Path $FilePath) {
        Write-Host "  + $File" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "Missing entry file: $File"
        Write-Host "  - $Issue" -ForegroundColor Red
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# 6. Build
Write-Host "--- 6. Build ---" -ForegroundColor Yellow

$CsprojPath = Join-Path $RootPath "币安量化机器人.csproj"
if (Test-Path $CsprojPath) {
    Write-Host "  Building project..." -ForegroundColor Cyan

    $BuildOutput = dotnet build $CsprojPath --verbosity minimal 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  OK build success" -ForegroundColor Green
        $ChecksPassed++
    }
    else {
        $Issue = "Build failed"
        Write-Host "  - $Issue" -ForegroundColor Red
        Write-Host "  Output (first 60 lines):" -ForegroundColor Gray
        $BuildOutput | Select-Object -First 60 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        $IssuesFound += $Issue
        $ChecksFailed++
    }
}

Write-Host ""

# 7. Summary and save report
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Passed: $ChecksPassed" -ForegroundColor Green
Write-Host "Failed: $ChecksFailed" -ForegroundColor Red
Write-Host ""

if ($IssuesFound.Count -gt 0) {
    Write-Host "Issues:" -ForegroundColor Yellow
    foreach ($Issue in $IssuesFound) {
        Write-Host "  - $Issue" -ForegroundColor Red
    }
}
else {
    Write-Host "All checks passed" -ForegroundColor Green
}

Write-Host ""

$DocsDir = Join-Path $RootPath "Docs"
New-Item -Path $DocsDir -ItemType Directory -Force | Out-Null
$ReportPath = Join-Path $DocsDir "Project_Health_Check_Report.md"

$ReportContent = @"
# Project Health Check Report

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

## Stats

- Passed: $ChecksPassed
- Failed: $ChecksFailed

## Build
$(if ($LASTEXITCODE -eq 0) { "Build: OK" } else { "Build: Failed" })

"@

if ($IssuesFound.Count -gt 0) {
    $ReportContent += "`n## Issues`n`n"
    foreach ($Issue in $IssuesFound) {
        $ReportContent += "- $Issue`n"
    }
}

Set-Content -Path $ReportPath -Value $ReportContent -Encoding UTF8

Write-Host "Report saved: $ReportPath" -ForegroundColor Cyan
Write-Host ""
