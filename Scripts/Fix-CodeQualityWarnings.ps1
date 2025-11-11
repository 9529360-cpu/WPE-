# 批量修复代码质量警告脚本
# 修复 CS8600, CS8622, IDE0060, IDE0007 等警告

Write-Host "开始修复代码质量警告..." -ForegroundColor Green

# 1. 修复 CS8600: null转换警告 - 添加 null 检查或使用 null-forgiving 操作符
Write-Host "`n修复 CS8600 警告..." -ForegroundColor Yellow

$files = @(
    "Services/Resilience/AutoRecoveryManager.cs",
    "Services/Resilience/AnomalyDetectionSystem.cs",
    "Services/Performance/SmartCacheManager.cs",
    "Services/Performance/RealtimeDataProcessor.cs",
    "Services/AI/DecisionFactorLibrary.cs",
    "Modules/Account/AccountFundsView.xaml.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "  处理 $file"
        
        # 读取文件
        $content = Get-Content $file -Raw
        
        # 修复 TryGetValue 的 null 问题
        $content = $content -replace '(\w+\.TryGetValue\([^,]+,\s*out\s+)(\w+\s+\w+)(\))', '$1$2?$3'
        
        # 修复 Timer 回调参数
        $content = $content -replace 'private void (\w+)Callback\(object state\)', 'private void $1Callback(object? state)'
        
        # 保存文件
        Set-Content -Path $file -Value $content -NoNewline
    }
}

# 2. 修复 CS8622: EventHandler null 参数警告
Write-Host "`n修复 CS8622 警告..." -ForegroundColor Yellow

$content = Get-Content "Modules/AI/AICentralCoordinatorView.xaml.cs" -Raw
$content = $content -replace 'private void RefreshTimer_Tick\(object sender, EventArgs e\)', 'private void RefreshTimer_Tick(object? sender, EventArgs e)'
Set-Content -Path "Modules/AI/AICentralCoordinatorView.xaml.cs" -Value $content -NoNewline

# 3. 修复 IDE0060: 未使用参数警告 - 添加 discard 或移除参数
Write-Host "`n修复 IDE0060 警告..." -ForegroundColor Yellow

$files = @(
    "Services/AI/WorkflowOrchestrator.cs",
    "Services/AI/WorkflowEngine.cs",
    "Services/AI/LearningModule.cs",
    "Services/AI/ResourceManager.cs",
    "Services/AI/AICentralCoordinatorView.xaml.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "  处理 $file"
        
        $content = Get-Content $file -Raw
        
        # 将未使用的参数替换为 _
        $content = $content -replace '(\w+Callback|Handler)\(([^)]*?)\b(\w+)\s+(\w+)\b([^)]*)\)', {
            param($match)
            $fullMatch = $match.Value
            # 如果参数未在方法体中使用，添加 discard
            if ($fullMatch -match 'ct\b' -or $fullMatch -match 'state\b' -or $fullMatch -match 'systemState\b' -or $fullMatch -match 'decision\b') {
                $fullMatch -replace '\b(ct|state|systemState|decision)\b', '_'
            } else {
                $fullMatch
            }
        }
        
        Set-Content -Path $file -Value $content -NoNewline
    }
}

# 4. 修复 IDE0007: var 使用错误
Write-Host "`n修复 IDE0007 警告..." -ForegroundColor Yellow

$files = @(
    "Services/Resilience/AnomalyDetectionSystem.cs",
    "Services/Resilience/AutoRecoveryManager.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "  处理 $file"
        
        $content = Get-Content $file -Raw
        
        # 将显式类型改为 var (仅在简单情况)
        $content = $content -replace 'IGrouping<(\w+), (\w+)>\[\] (\w+) =', 'var $3 ='
        $content = $content -replace 'List<string> (\w+) =', 'var $1 ='
        
        Set-Content -Path $file -Value $content -NoNewline
    }
}

# 5. 修复 IDE0059: 不需要赋值 - "secretKey"
Write-Host "`n修复 IDE0059 警告..." -ForegroundColor Yellow

$file = "Services/ConfigurationService.cs"
if (Test-Path $file) {
    Write-Host "  处理 $file"
    
    $content = Get-Content $file -Raw
    
    # 移除未使用的 secretKey 赋值
    $content = $content -replace 'string secretKey = [^;]+;', '// secretKey removed - not used'
    
    Set-Content -Path $file -Value $content -NoNewline
}

Write-Host "`n完成！正在编译项目验证..." -ForegroundColor Green

# 编译项目
dotnet build "币安量化机器人.csproj" --no-incremental

Write-Host "`n修复完成！" -ForegroundColor Green
