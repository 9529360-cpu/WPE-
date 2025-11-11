@echo off
chcp 65001 >nul

echo ════════════════════════════════════════
echo   Fixing All var Warnings
echo ════════════════════════════════════════
echo.

REM Fix AICentralCoordinator.cs
powershell -Command "(Get-Content 'Services\AI\AICentralCoordinator.cs' -Raw -Encoding UTF8) -replace '(\s+)SystemState\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)Dictionary<[^>]+>\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)decimal\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)AIDecision\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)bool\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)TimeSpan\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)MarketData\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)IReadOnlyList<[^>]+>\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)DecisionOutcome\?\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)TradingAccount\?\s+(\w+)\s*=', '$1var $2 =' -replace 'TryGetValue\(([^,]+),\s*out\s+decimal\s+(\w+)\)', 'TryGetValue($1, out var $2)' -replace '(\s+)MarketCondition\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)AccountStatus\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)StrategyStatus\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)RiskMetrics\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)SystemResources\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)double\[\]\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)double\s+(\w+)\s*=', '$1var $2 =' -replace 'for\s*\(\s*int\s+(\w+)\s*=', 'for (var $1 =' -replace '(\s+)int\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)string\[\]\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)AccountType\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)BacktestEvaluation\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)SimulationEvaluation\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)LiveEvaluation\s+(\w+)\s*=', '$1var $2 =' | Set-Content 'Services\AI\AICentralCoordinator.cs' -Encoding UTF8 -NoNewline"
echo Fixed: AICentralCoordinator.cs

REM Fix SystemResourceMonitor.cs
powershell -Command "(Get-Content 'Services\Performance\SystemResourceMonitor.cs' -Raw -Encoding UTF8) -replace '(\s+)DateTime\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)TimeSpan\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)double\s+(\w+)\s*=', '$1var $2 =' -replace '(\s+)int\s+(\w+)\s*=', '$1var $2 =' | Set-Content 'Services\Performance\SystemResourceMonitor.cs' -Encoding UTF8 -NoNewline"
echo Fixed: SystemResourceMonitor.cs

echo.
echo ════════════════════════════════════════
echo   All Fixed!
echo ════════════════════════════════════════
echo.
pause
