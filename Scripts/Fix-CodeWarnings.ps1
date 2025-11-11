# 批量修复编码警告脚本
# 用途: 自动修复 IDE0011 (大括号) 和 IDE0008 (var) 警告

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  币安量化机器人 - 代码质量修复工具  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. 检查当前状态
Write-Host "📊 Step 1: 检查当前警告数量..." -ForegroundColor Yellow
$buildOutput = dotnet build 2>&1 | Out-String
$warningCount = ([regex]::Matches($buildOutput, "warning")).Count

Write-Host "   当前警告数: $warningCount" -ForegroundColor $(if($warningCount -gt 0){"Red"}else{"Green"})
Write-Host ""

# 2. 备份代码
Write-Host "💾 Step 2: 备份当前代码..." -ForegroundColor Yellow
git add .
git commit -m "backup: 修复警告前的代码备份 ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))" -q
Write-Host "   ✅ 代码已备份到 Git" -ForegroundColor Green
Write-Host ""

# 3. 清理项目
Write-Host "🧹 Step 3: 清理项目..." -ForegroundColor Yellow
dotnet clean --verbosity quiet
Write-Host "   ✅ 清理完成" -ForegroundColor Green
Write-Host ""

# 4. 运行自动格式化
Write-Host "🔧 Step 4: 运行代码格式化..." -ForegroundColor Yellow

# 检查 dotnet-format 是否安装
$formatInstalled = dotnet tool list -g | Select-String "dotnet-format"
if (-not $formatInstalled) {
    Write-Host "   ⚠️  dotnet-format 未安装，正在安装..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-format
}

# 运行格式化
Write-Host "   正在修复 IDE0011 (大括号)..." -ForegroundColor Cyan
dotnet format --diagnostics IDE0011 --verbosity quiet

Write-Host "   正在修复 IDE0008 (var)..." -ForegroundColor Cyan
dotnet format --diagnostics IDE0008 --verbosity quiet

Write-Host "   正在修复所有警告..." -ForegroundColor Cyan
dotnet format --severity warn --verbosity quiet

Write-Host "   ✅ 格式化完成" -ForegroundColor Green
Write-Host ""

# 5. 重新构建
Write-Host "🔨 Step 5: 重新构建项目..." -ForegroundColor Yellow
$buildResult = dotnet build 2>&1 | Out-String
$newWarningCount = ([regex]::Matches($buildResult, "warning")).Count
$errorCount = ([regex]::Matches($buildResult, "error")).Count

Write-Host "   构建结果:" -ForegroundColor Cyan
Write-Host "   - 错误: $errorCount" -ForegroundColor $(if($errorCount -gt 0){"Red"}else{"Green"})
Write-Host "   - 警告: $newWarningCount" -ForegroundColor $(if($newWarningCount -gt 0){"Yellow"}else{"Green"})
Write-Host "   - 修复数量: $($warningCount - $newWarningCount)" -ForegroundColor Green
Write-Host ""

# 6. 显示剩余警告
if ($newWarningCount -gt 0) {
    Write-Host "⚠️  剩余警告详情:" -ForegroundColor Yellow
    $warnings = $buildResult | Select-String "warning" | Select-Object -First 10
    foreach ($warning in $warnings) {
        Write-Host "   $warning" -ForegroundColor Gray
    }
    
    if ($newWarningCount -gt 10) {
        Write-Host "   ... 还有 $($newWarningCount - 10) 个警告" -ForegroundColor Gray
    }
    Write-Host ""
}

# 7. 运行测试
Write-Host "🧪 Step 6: 运行测试..." -ForegroundColor Yellow
$testResult = dotnet test --no-build --verbosity quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ 所有测试通过" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  部分测试失败，请检查" -ForegroundColor Yellow
}
Write-Host ""

# 8. 提交修复
Write-Host "📝 Step 7: 提交修复..." -ForegroundColor Yellow
git add .

$commitMessage = @"
fix: 批量修复编码规范警告

- 修复 IDE0011: 添加缺失的大括号
- 修复 IDE0008: 使用显式类型代替 var
- 修复其他编码规范警告

修复前警告数: $warningCount
修复后警告数: $newWarningCount
修复数量: $($warningCount - $newWarningCount)
"@

git commit -m $commitMessage
Write-Host "   ✅ 修复已提交" -ForegroundColor Green
Write-Host ""

# 9. 总结
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  修复完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "📊 修复总结:" -ForegroundColor Cyan
Write-Host "   原始警告数: $warningCount" -ForegroundColor White
Write-Host "   当前警告数: $newWarningCount" -ForegroundColor $(if($newWarningCount -gt 0){"Yellow"}else{"Green"})
Write-Host "   修复数量: $($warningCount - $newWarningCount)" -ForegroundColor Green
Write-Host "   修复率: $([math]::Round((($warningCount - $newWarningCount) / $warningCount) * 100, 2))%" -ForegroundColor Green
Write-Host ""

if ($newWarningCount -eq 0) {
    Write-Host "🎉 恭喜！所有警告已修复！" -ForegroundColor Green
} elseif ($newWarningCount -lt 50) {
    Write-Host "👍 很好！大部分警告已修复，剩余警告较少" -ForegroundColor Yellow
    Write-Host "   建议: 手动检查并修复剩余的 $newWarningCount 个警告" -ForegroundColor White
} else {
    Write-Host "⚠️  还有较多警告需要修复" -ForegroundColor Yellow
    Write-Host "   建议: 运行 'dotnet build > build.log' 查看详细警告列表" -ForegroundColor White
}
Write-Host ""

# 10. 下一步建议
Write-Host "📌 下一步建议:" -ForegroundColor Cyan
Write-Host "   1. 检查 Git 提交: git log -1" -ForegroundColor White
Write-Host "   2. 查看更改内容: git diff HEAD~1" -ForegroundColor White
Write-Host "   3. 如需回滚: git reset --hard HEAD~1" -ForegroundColor White
Write-Host "   4. 推送到远程: git push origin master" -ForegroundColor White
Write-Host ""

Write-Host "按任意键退出..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
