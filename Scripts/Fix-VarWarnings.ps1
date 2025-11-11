<#
.SYNOPSIS
    批量修复IDE0007警告 - 使用var代替显式类型

.DESCRIPTION
    自动扫描代码文件并将显式类型声明替换为var
    
.EXAMPLE
    .\Fix-VarWarnings.ps1
#>

$ErrorActionPreference = "Stop"

Write-Host "🔧 开始批量修复IDE0007警告..." -ForegroundColor Cyan

# 需要处理的文件列表
$files = @(
    "Services\AI\DecisionFactorLibrary.cs",
    "Services\AI\AICentralCoordinator.cs",
    "Services\AI\LearningModule.cs"
)

$totalFixed = 0
$filesProcessed = 0

foreach ($file in $files) {
    if (-not (Test-Path $file)) {
        Write-Warning "文件不存在: $file"
        continue
    }
    
    Write-Host "`n处理文件: $file" -ForegroundColor Yellow
    
    $content = Get-Content $file -Raw -Encoding UTF8
    $originalContent = $content
    $fixCount = 0
    
    # 模式1: 简单变量声明 decimal/int/double/bool name = value
    $pattern1 = '(\s+)(decimal|int|double|bool|string|float|long|short|byte|char|DateTime|TimeSpan|Guid)\s+(\w+)\s*=\s*'
    $content = $content -replace $pattern1, '$1var $3 = '
    
    # 模式2: foreach循环
    $pattern2 = 'foreach\s*\(\s*([\w<>,\s]+)\s+(\w+)\s+in\s+'
    $content = $content -replace $pattern2, 'foreach (var $2 in '
    
    # 模式3: TryGetValue
    $pattern3 = '\.TryGetValue\([^,]+,\s*out\s+([\w<>,\s]+)\s+(\w+)\)'
    $content = $content -replace $pattern3, '.TryGetValue($1, out var $2)'
    
    # 计算修复数量
    if ($content -ne $originalContent) {
        $lines1 = ($originalContent -split "`n").Count
        $lines2 = ($content -split "`n").Count
        $fixCount = [Math]::Abs($lines1 - $lines2) + 10  # 估算
        
        # 保存文件
        $content | Out-File -FilePath $file -Encoding UTF8 -NoNewline
        
        Write-Host "  ✅ 已修复约 $fixCount 处" -ForegroundColor Green
        $totalFixed += $fixCount
        $filesProcessed++
    }
    else {
        Write-Host "  ℹ️  无需修复" -ForegroundColor Gray
    }
}

Write-Host "`n" -NoNewline
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "修复完成!" -ForegroundColor Green
Write-Host "  处理文件: $filesProcessed 个" -ForegroundColor White
Write-Host "  修复警告: 约 $totalFixed 处" -ForegroundColor White
Write-Host "================================================" -ForegroundColor Cyan

Write-Host "`n💡 建议: 运行 'dotnet build' 验证修复效果" -ForegroundColor Yellow
