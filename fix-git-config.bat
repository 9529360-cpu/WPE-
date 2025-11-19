@echo off
REM 修复 Git 仓库配置脚本 (Windows)
REM Fix Git Repository Configuration Script (Windows)

echo ================================
echo 修复 Git 仓库配置
echo Fixing Git Repository Configuration
echo ================================
echo.

REM 检查是否在 Git 仓库中
if not exist .git (
    echo 错误: 当前目录不是 Git 仓库
    echo Error: Current directory is not a Git repository
    pause
    exit /b 1
)

REM 备份当前配置
echo 正在备份当前 Git 配置...
echo Backing up current Git configuration...
copy .git\config .git\config.backup > nul
if %ERRORLEVEL% NEQ 0 (
    echo 错误: 无法备份配置文件
    echo Error: Failed to backup configuration file
    pause
    exit /b 1
)
echo 配置已备份到 .git\config.backup
echo Configuration backed up to .git\config.backup
echo.

REM 获取当前 fetch 配置
echo 当前 fetch 配置 / Current fetch configuration:
git config --get remote.origin.fetch
echo.

REM 检查远程 'origin' 是否存在 / Verify remote 'origin' exists
git config --get remote.origin.url > nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo 错误: 找不到名为 'origin' 的远程仓库
    echo Error: Remote 'origin' not found
    pause
    exit /b 1
)

REM 更新 fetch 配置
echo 更新 fetch 配置以获取所有分支...
echo Updating fetch configuration to retrieve all branches...
git config remote.origin.fetch "+refs/heads/*:refs/remotes/origin/*"
if %ERRORLEVEL% NEQ 0 (
    echo 错误: 无法更新 fetch 配置
    echo Error: Failed to update fetch configuration
    pause
    exit /b 1
)

REM 验证新配置
echo 新 fetch 配置 / New fetch configuration:
git config --get remote.origin.fetch
echo.

REM 获取所有分支
echo 正在从远程仓库获取所有分支...
echo Fetching all branches from remote repository...
git fetch --all --prune
if %ERRORLEVEL% EQU 0 (
    echo [OK] 成功获取所有分支
    echo [OK] Successfully fetched all branches
) else (
    echo [!] 获取分支时出错，可能需要配置认证
    echo [!] Error fetching branches, you may need to configure authentication
)
echo.

REM 显示所有远程分支
echo 所有远程分支 / All remote branches:
git branch -r
echo.

echo ================================
echo 配置修复完成!
echo Configuration fix completed!
echo ================================
echo.
echo 您现在可以使用以下命令查看和切换分支:
echo You can now use the following commands to view and switch branches:
echo.
echo   查看所有分支 / View all branches:
echo     git branch -a
echo.
echo   切换到某个分支 / Switch to a branch:
echo     git checkout ^<branch-name^>
echo.
echo   创建并切换到新分支 / Create and switch to a new branch:
echo     git checkout -b ^<new-branch-name^>
echo.

pause
