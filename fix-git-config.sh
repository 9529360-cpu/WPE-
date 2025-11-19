#!/bin/bash
# 修复 Git 仓库配置脚本
# Fix Git Repository Configuration Script

echo "================================"
echo "修复 Git 仓库配置"
echo "Fixing Git Repository Configuration"
echo "================================"
echo ""

# 检查是否在 Git 仓库中
if [ ! -d .git ]; then
    echo "错误: 当前目录不是 Git 仓库"
    echo "Error: Current directory is not a Git repository"
    exit 1
fi

# 备份当前配置
echo "正在备份当前 Git 配置..."
echo "Backing up current Git configuration..."
if ! cp .git/config .git/config.backup; then
    echo "错误: 无法备份配置文件"
    echo "Error: Failed to backup configuration file"
    exit 1
fi
echo "配置已备份到 .git/config.backup"
echo "Configuration backed up to .git/config.backup"
echo ""

# 获取当前 fetch 配置
current_fetch=$(git config --get remote.origin.fetch)
echo "当前 fetch 配置 / Current fetch configuration:"
echo "$current_fetch"
echo ""

# 检查远程 'origin' 是否存在
if ! git config --get remote.origin.url > /dev/null 2>&1; then
    echo "错误: 找不到名为 'origin' 的远程仓库"
    echo "Error: Remote 'origin' not found"
    exit 1
fi

# 更新 fetch 配置
echo "更新 fetch 配置以获取所有分支..."
echo "Updating fetch configuration to retrieve all branches..."
git config remote.origin.fetch "+refs/heads/*:refs/remotes/origin/*"
if [ $? -ne 0 ]; then
    echo "错误: 无法更新 fetch 配置"
    echo "Error: Failed to update fetch configuration"
    exit 1
fi

# 验证新配置
new_fetch=$(git config --get remote.origin.fetch)
echo "新 fetch 配置 / New fetch configuration:"
echo "$new_fetch"
echo ""

# 获取所有分支
echo "正在从远程仓库获取所有分支..."
echo "Fetching all branches from remote repository..."
if git fetch --all --prune; then
    echo "✓ 成功获取所有分支"
    echo "✓ Successfully fetched all branches"
else
    echo "✗ 获取分支时出错，可能需要配置认证"
    echo "✗ Error fetching branches, you may need to configure authentication"
fi
echo ""

# 显示所有远程分支
echo "所有远程分支 / All remote branches:"
git branch -r
echo ""

echo "================================"
echo "配置修复完成!"
echo "Configuration fix completed!"
echo "================================"
echo ""
echo "您现在可以使用以下命令查看和切换分支:"
echo "You can now use the following commands to view and switch branches:"
echo ""
echo "  查看所有分支 / View all branches:"
echo "    git branch -a"
echo ""
echo "  切换到某个分支 / Switch to a branch:"
echo "    git checkout <branch-name>"
echo ""
echo "  创建并切换到新分支 / Create and switch to a new branch:"
echo "    git checkout -b <new-branch-name>"
echo ""
