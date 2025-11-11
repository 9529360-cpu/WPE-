<#
.SYNOPSIS
    自动修复所有 IDE0007 警告（使用 var 代替显式类型）

.DESCRIPTION
    批量将显式类型声明替换为 var

.EXAMPLE
    .\Fix-AllVarWarnings.ps1
#>

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  批量修复 var 警告" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

$RootPath = Split-Path -Parent $PSScriptRoot

# 需要修复的文件
$FilesToFix = @(
    "Services\AI\DecisionFactorLibrary.cs"
)

$TotalFixed = 0

foreach ($File in $FilesToFix) {
    $FilePath = Join-Path $RootPath $File
    
    if (-not (Test-Path $FilePath)) {
        Write-Host "⚠️  跳过: $File (文件不存在)" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "📝 处理: $File" -ForegroundColor Cyan
    
    $Content = Get-Content $FilePath -Raw -Encoding UTF8
    $OriginalContent = $Content
    $FixCount = 0
    
    # 修复模式列表
    $Patterns = @(
        # decimal xxx = ...
        @{
            Pattern = '(\s+)decimal\s+(\w+)\s*=\s*([^;]+;)'
            Replacement = '$1var $2 = $3'
        },
        # int xxx = ...
        @{
            Pattern = '(\s+)int\s+(\w+)\s*=\s*([^;]+;)'
            Replacement = '$1var $2 = $3'
        },
        # foreach (KeyValuePair<...> xxx in ...)
        @{
            Pattern = 'foreach\s*\(\s*KeyValuePair<([^>]+)>\s+(\w+)\s+in\s+'
            Replacement = 'foreach (var $2 in '
        },
        # foreach (string xxx in ...)
        @{
            Pattern = 'foreach\s*\(\s*string\s+(\w+)\s+in\s+'
            Replacement = 'foreach (var $1 in '
        },
        # for (int i = ...)
        @{
            Pattern = 'for\s*\(\s*int\s+(\w+)\s*=\s*'
            Replacement = 'for (var $1 = '
        },
        # TryGetValue(..., out Type variable)
        @{
            Pattern = 'TryGetValue\([^,]+,\s*out\s+(\w+)\s+(\w+)\)'
            Replacement = 'TryGetValue($1, out var $2)'
        }
    )
    
    foreach ($Pattern in $Patterns) {
        $Matches = [regex]::Matches($Content, $Pattern.Pattern)
        if ($Matches.Count -gt 0) {
            $Content = [regex]::Replace($Content, $Pattern.Pattern, $Pattern.Replacement)
            $FixCount += $Matches.Count
        }
    }
    
    if ($Content -ne $OriginalContent) {
        Set-Content $FilePath -Value $Content -Encoding UTF8 -NoNewline
        Write-Host "✅ 已修复: $FixCount 处" -ForegroundColor Green
        $TotalFixed += $FixCount
    }
    else {
        Write-Host "✅ 无需修复" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  修复完成！" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "总计修复: $TotalFixed 处" -ForegroundColor Green
Write-Host ""
