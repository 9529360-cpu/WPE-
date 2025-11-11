# 批量修复警告脚本 - 第2轮
# 修复剩余的 CS8425, CS8618, CS8604, IDE0060 等警告

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  第2轮警告修复 - 针对性修复  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. 检查当前警告
Write-Host "📊 Step 1: 检查当前警告..." -ForegroundColor Yellow
$buildOutput = dotnet build 2>&1 | Out-String
$errorLines = $buildOutput | Select-String "error" 
$warningLines = $buildOutput | Select-String "warning"

$errorCount = ($errorLines | Measure-Object).Count
$warningCount = ($warningLines | Measure-Object).Count

Write-Host "   错误: $errorCount" -ForegroundColor $(if($errorCount -gt 0){"Red"}else{"Green"})
Write-Host "   警告: $warningCount" -ForegroundColor $(if($warningCount -gt 0){"Yellow"}else{"Green"})
Write-Host ""

# 2. 显示详细警告类型
Write-Host "📋 Step 2: 分析警告类型..." -ForegroundColor Yellow

$ide0008Count = ($warningLines | Select-String "IDE0008").Count
$cs8425Count = ($warningLines | Select-String "CS8425").Count
$cs8618Count = ($warningLines | Select-String "CS8618").Count
$cs8604Count = ($warningLines | Select-String "CS8604").Count
$ide0060Count = ($warningLines | Select-String "IDE0060").Count
$ide0051Count = ($warningLines | Select-String "IDE0051").Count

Write-Host "   IDE0008 (var): $ide0008Count" -ForegroundColor Cyan
Write-Host "   CS8425 (EnumeratorCancellation): $cs8425Count" -ForegroundColor Cyan
Write-Host "   CS8618 (null字段): $cs8618Count" -ForegroundColor Cyan
Write-Host "   CS8604 (null参数): $cs8604Count" -ForegroundColor Cyan
Write-Host "   IDE0060 (未使用参数): $ide0060Count" -ForegroundColor Cyan
Write-Host "   IDE0051 (未使用成员): $ide0051Count" -ForegroundColor Cyan
Write-Host ""

# 3. 运行格式化修复
Write-Host "🔧 Step 3: 运行格式化修复..." -ForegroundColor Yellow

# 修复 IDE0008
if ($ide0008Count -gt 0) {
    Write-Host "   修复 IDE0008..." -ForegroundColor Cyan
    dotnet format --diagnostics IDE0008 --verbosity quiet
}

# 修复 IDE0060
if ($ide0060Count -gt 0) {
    Write-Host "   修复 IDE0060..." -ForegroundColor Cyan
    dotnet format --diagnostics IDE0060 --verbosity quiet
}

# 修复 IDE0051
if ($ide0051Count -gt 0) {
    Write-Host "   修复 IDE0051..." -ForegroundColor Cyan
    dotnet format --diagnostics IDE0051 --verbosity quiet
}

Write-Host "   ✅ 自动修复完成" -ForegroundColor Green
Write-Host ""

# 4. 重新构建
Write-Host "🔨 Step 4: 重新构建..." -ForegroundColor Yellow
$buildResult = dotnet build 2>&1 | Out-String
$newErrorCount = ($buildResult | Select-String "error").Count
$newWarningCount = ($buildResult | Select-String "warning").Count

Write-Host "   新的错误数: $newErrorCount" -ForegroundColor $(if($newErrorCount -gt 0){"Red"}else{"Green"})
Write-Host "   新的警告数: $newWarningCount" -ForegroundColor $(if($newWarningCount -gt 0){"Yellow"}else{"Green"})
Write-Host ""

# 5. 显示剩余问题
if ($newWarningCount -gt 0 -or $newErrorCount -gt 0) {
    Write-Host "⚠️  Step 5: 剩余问题分析..." -ForegroundColor Yellow
    
    # CS8425 需要手动添加 [EnumeratorCancellation]
    $cs8425Remaining = ($buildResult | Select-String "CS8425").Count
    if ($cs8425Remaining -gt 0) {
        Write-Host "   CS8425 (异步迭代器): $cs8425Remaining 个 - 需要手动添加 [EnumeratorCancellation]" -ForegroundColor Yellow
    }
    
    # CS8618 需要初始化或标记为可空
    $cs8618Remaining = ($buildResult | Select-String "CS8618").Count
    if ($cs8618Remaining -gt 0) {
        Write-Host "   CS8618 (null字段): $cs8618Remaining 个 - 需要添加 = null! 或初始化" -ForegroundColor Yellow
    }
    
    # CS8604 需要null检查
    $cs8604Remaining = ($buildResult | Select-String "CS8604").Count
    if ($cs8604Remaining -gt 0) {
        Write-Host "   CS8604 (null参数): $cs8604Remaining 个 - 需要添加null检查或!" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "   建议手动修复文件列表:" -ForegroundColor White
    $problemFiles = $buildResult | Select-String "error|warning" | Select-Object -First 20
    foreach ($line in $problemFiles) {
        if ($line -match "([^\\]+\.cs)\((\d+),\d+\): (error|warning) (\w+)") {
            $file = $matches[1]
            $lineNum = $matches[2]
            $type = $matches[3]
            $code = $matches[4]
            Write-Host "   - $file ($lineNum) $code" -ForegroundColor Gray
        }
    }
}
else {
    Write-Host "🎉 所有问题已修复！" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  修复总结  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   修复前: 错误=$errorCount, 警告=$warningCount" -ForegroundColor White
Write-Host "   修复后: 错误=$newErrorCount, 警告=$newWarningCount" -ForegroundColor White
Write-Host "   修复数量: $($errorCount + $warningCount - $newErrorCount - $newWarningCount)" -ForegroundColor Green
Write-Host ""

if ($newErrorCount -eq 0 -and $newWarningCount -eq 0) {
    Write-Host "✅ 完美！项目完全干净！" -ForegroundColor Green
} elseif ($newErrorCount -eq 0 -and $newWarningCount -lt 30) {
    Write-Host "👍 很好！只剩少量警告需要手动修复" -ForegroundColor Yellow
} else {
    Write-Host "⚠️  还需要继续修复" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "按任意键退出..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
