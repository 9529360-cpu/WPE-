@echo off
chcp 65001 >nul
title 币安量化机器人 - 一键启动脚本

echo.
echo ════════════════════════════════════════════════════════
echo            币安量化机器人 - 一键启动
echo ════════════════════════════════════════════════════════
echo.

REM 检查.NET 8运行时
echo [1/5] 检查.NET 8运行时...
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ❌ 未检测到 .NET 8 运行时
    echo.
    echo 请先安装 .NET 8 Runtime:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)
echo ✅ .NET 运行时已就绪

REM 检查项目文件
echo.
echo [2/5] 检查项目文件...
if not exist "币安量化机器人.csproj" (
    echo ❌ 未找到项目文件
    echo.
    echo 请确保在项目根目录运行此脚本
    pause
    exit /b 1
)
echo ✅ 项目文件已找到

REM 还原NuGet包
echo.
echo [3/5] 还原NuGet包...
dotnet restore
if %errorlevel% neq 0 (
    echo ❌ 包还原失败
    pause
    exit /b 1
)
echo ✅ 包还原成功

REM 构建项目
echo.
echo [4/5] 构建项目...
dotnet build --configuration Release
if %errorlevel% neq 0 (
    echo ❌ 构建失败
    pause
    exit /b 1
)
echo ✅ 构建成功

REM 启动应用
echo.
echo [5/5] 启动应用程序...
echo.
echo ════════════════════════════════════════════════════════
echo            应用程序正在启动...
echo ════════════════════════════════════════════════════════
echo.
echo 💡 提示:
echo   - 首次使用请配置API Key (设置 → API管理)
echo   - 推荐先使用模拟账户测试
echo   - 查看文档: Docs\Quick_Start_Guide.md
echo.
echo ════════════════════════════════════════════════════════
echo.

REM 运行应用
dotnet run --configuration Release

echo.
echo ════════════════════════════════════════════════════════
echo            应用程序已退出
echo ════════════════════════════════════════════════════════
echo.
pause
