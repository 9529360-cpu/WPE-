# Repository Cleanup Guide / 仓库清理指南

## ⚠️ Important Notice / 重要提示

**English:**
This is a manual cleanup guide. The Copilot agent **cannot** automatically delete branches or close pull requests due to security restrictions. You must perform these operations manually.

**中文:**
这是一个手动清理指南。由于安全限制，Copilot 代理**无法**自动删除分支或关闭拉取请求。您必须手动执行这些操作。

---

## Current Repository Status / 当前仓库状态

### Branches / 分支 (15 total)

#### ✅ Recommended to KEEP / 建议保留:
1. **master** - Main production branch / 主生产分支
2. **upgrade-to-NET10** - Active feature branch / 活跃的功能分支

#### ⚠️ Review Required / 需要审查:
3. **9529360-cpu-patch-1** - Patch branch, check if still needed / 补丁分支，检查是否仍需要

#### 🗑️ Candidate for Deletion / 删除候选 (copilot/* branches):
- copilot/delete-old-branches-and-prs (current working branch)
- copilot/check-compile-errors
- copilot/complete-project-for-launch
- copilot/remove-unused-branches
- copilot/sub-pr-2
- copilot/sub-pr-2-again
- copilot/sub-pr-2-another-one
- copilot/sub-pr-2-one-more-time
- copilot/sub-pr-2-yet-again

#### 🗑️ Candidate for Deletion / 删除候选 (codex/* branches):
- codex/analyze-and-refactor-trading-system-architecture
- codex/evaluate-ai-trading-terminal-features-and-progress
- codex/evaluate-ai-trading-terminal-features-and-progress-qmgz3v

---

### Pull Requests / 拉取请求 (15 total)

#### 🟢 Open PRs / 开放的PR (10):
1. **PR #15** - "[WIP] Remove outdated branches and approve pull requests" - DRAFT
2. **PR #14** - "Replace generic catch clause" - DRAFT
3. **PR #12** - "Fix restrictive git fetch configuration"
4. **PR #11** - "Production readiness: Complete infrastructure"
5. **PR #9** - "Codex/analyze and refactor trading system architecture"
6. **PR #8** - "No changes needed: null check for _secretBytes" - DRAFT
7. **PR #7** - "Verify readonly HttpClient field" - DRAFT
8. **PR #6** - "Fix HttpClient resource leak" - DRAFT
9. **PR #5** - "Remove blocking async call" - DRAFT
10. **PR #2** - "Add auto-trading engine and strategy control UI"

#### ✅ Closed/Merged PRs / 已关闭/合并的PR (5):
- PR #13 - Merged ✅
- PR #10 - Merged ✅
- PR #4 - Merged ✅
- PR #3 - Merged ✅
- PR #1 - Merged ✅

---

## Manual Cleanup Steps / 手动清理步骤

### Option 1: Using GitHub Web Interface / 使用GitHub网页界面

**English:**
1. Go to: https://github.com/9529360-cpu/WPE-/branches
2. For each branch you want to delete, click the trash icon (🗑️)
3. Go to: https://github.com/9529360-cpu/WPE-/pulls
4. For each PR you want to close, click "Close pull request"

**中文:**
1. 访问：https://github.com/9529360-cpu/WPE-/branches
2. 对于每个要删除的分支，点击垃圾桶图标 (🗑️)
3. 访问：https://github.com/9529360-cpu/WPE-/pulls
4. 对于每个要关闭的PR，点击"Close pull request"

### Option 2: Using Git Command Line / 使用Git命令行

**Delete Remote Branches / 删除远程分支:**
```bash
# Delete a single branch / 删除单个分支
git push origin --delete branch-name

# Example: Delete copilot branches / 示例：删除copilot分支
git push origin --delete copilot/check-compile-errors
git push origin --delete copilot/remove-unused-branches
# ... repeat for other branches
```

**Delete Local Branches / 删除本地分支:**
```bash
# Delete local branch / 删除本地分支
git branch -d branch-name

# Force delete / 强制删除
git branch -D branch-name
```

### Option 3: Batch Delete Script / 批量删除脚本

**Create a script to delete multiple branches / 创建脚本删除多个分支:**
```bash
#!/bin/bash
# save as delete-branches.sh

BRANCHES_TO_DELETE=(
  "copilot/check-compile-errors"
  "copilot/remove-unused-branches"
  "copilot/sub-pr-2"
  "copilot/sub-pr-2-again"
  "copilot/sub-pr-2-another-one"
  "copilot/sub-pr-2-one-more-time"
  "copilot/sub-pr-2-yet-again"
  "codex/analyze-and-refactor-trading-system-architecture"
  "codex/evaluate-ai-trading-terminal-features-and-progress"
  "codex/evaluate-ai-trading-terminal-features-and-progress-qmgz3v"
)

for branch in "${BRANCHES_TO_DELETE[@]}"; do
  echo "Deleting $branch..."
  git push origin --delete "$branch"
done
```

**Run the script / 运行脚本:**
```bash
chmod +x delete-branches.sh
./delete-branches.sh
```

---

## Recommendations / 建议

### Branches to Keep / 保留的分支:
- ✅ **master** - Always keep
- ✅ **upgrade-to-NET10** - Active development

### Branches to Delete / 删除的分支:
- 🗑️ All `copilot/*` branches (temporary automated branches)
- 🗑️ All `codex/*` branches (if work is already merged)
- ⚠️ **9529360-cpu-patch-1** - Review first, then delete if not needed

### Pull Requests to Close / 关闭的拉取请求:
- Close all DRAFT PRs (#5, #6, #7, #8, #14, #15) if not actively being worked on
- Review and merge or close PR #2, #9, #11, #12 based on their content

---

## Safety Tips / 安全提示

**English:**
- ⚠️ **ALWAYS** make sure branches are merged before deleting
- ⚠️ **CHECK** if any open PRs depend on a branch before deleting
- ⚠️ **BACKUP** important data before bulk deletions
- ✅ You can always restore deleted branches from GitHub if needed (within 90 days)

**中文:**
- ⚠️ **始终**确保分支在删除前已合并
- ⚠️ **检查**是否有开放的PR依赖于要删除的分支
- ⚠️ **备份**重要数据后再批量删除
- ✅ 如需要可以从GitHub恢复已删除的分支（90天内）

---

## GitHub CLI Alternative / GitHub CLI 替代方案

If you have `gh` CLI installed / 如果安装了 `gh` CLI:

```bash
# List all branches / 列出所有分支
gh api repos/9529360-cpu/WPE-/branches

# Delete a branch / 删除分支
gh api -X DELETE repos/9529360-cpu/WPE-/git/refs/heads/branch-name

# Close a pull request / 关闭拉取请求
gh pr close 15
```

---

## Questions? / 有问题？

If you need help with any specific branch or PR, please ask and provide:
1. The branch/PR name
2. Why you want to keep or delete it
3. Any dependencies or concerns

如果您需要关于特定分支或PR的帮助，请询问并提供：
1. 分支/PR名称
2. 为什么要保留或删除它
3. 任何依赖关系或顾虑
