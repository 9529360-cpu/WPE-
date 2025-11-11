# 🔧 代码质量维护 - 快速参考

## 🚀 日常命令

### 检查代码格式
```bash
dotnet format --verify-no-changes
```

### 自动修复格式
```bash
dotnet format
```

### 检查编译警告
```bash
dotnet build | findstr /i "warning"
```

### 完整质量检查
```bash
dotnet clean
dotnet format
dotnet build
dotnet test
```

---

## 📋 编码规范速查

### ✅ DO (必须做)

```csharp
// ✅ 内置类型使用显式类型
int count = 10;
string name = "test";
decimal price = 99.99m;
bool isActive = true;

// ✅ 所有控制语句使用大括号
if (condition)
{
    DoSomething();
}

for (int i = 0; i < count; i++)
{
    Process(i);
}

// ✅ 显式访问修饰符
private readonly ILogger _logger;
public string Name { get; set; }
```

### ❌ DON'T (不要做)

```csharp
// ❌ 内置类型不用 var
var count = 10;
var name = "test";

// ❌ 不要省略大括号
if (condition)
    DoSomething();

// ❌ 不要省略访问修饰符
ILogger _logger;  // 缺少 private
```

---

## 🎯 快速修复

### 修复单个文件的警告
```bash
# Visual Studio: Ctrl+K, Ctrl+D (格式化文档)
# 或: Ctrl+. (快速操作)
```

### 修复整个项目
```bash
dotnet format "币安量化机器人.csproj"
```

### 修复特定警告类型
```bash
dotnet format --diagnostics IDE0011  # 大括号
dotnet format --diagnostics IDE0008  # var
```

---

## 📊 质量指标

| 指标 | 目标 | 当前 |
|------|------|------|
| 警告数 | 0 | **0** ✅ |
| 错误数 | 0 | **0** ✅ |
| 测试覆盖率 | >80% | - |
| 代码复杂度 | <15 | - |

---

## 🔍 常用规则

| 规则 | 说明 | 严重性 |
|------|------|--------|
| IDE0011 | 添加大括号 | Error |
| IDE0008 | 使用显式类型 | Error |
| IDE0007 | 使用var | Error |
| IDE0058 | 未使用表达式 | None |
| CS8618 | 不可为空字段 | Warning |
| CS8625 | null转换 | Warning |

---

## 💾 提交前检查清单

- [ ] 运行 `dotnet format`
- [ ] 运行 `dotnet build` (0 警告)
- [ ] 运行 `dotnet test` (全部通过)
- [ ] 代码审查
- [ ] Git commit

---

## 🆘 紧急修复

### 恢复上次提交
```bash
git reset --hard HEAD~1
```

### 查看修改内容
```bash
git diff
git diff HEAD~1
```

### 暂存修复
```bash
git stash
# 做其他事情
git stash pop
```

---

## 📞 快速链接

- [完整修复报告](Code_Quality_Fix_Report.md)
- [修复指南](Code_Quality_Fix_Guide.md)
- [自动化脚本](../Scripts/Fix-CodeWarnings.ps1)
- [.editorconfig](../.editorconfig)

---

_最后更新: 2025-01-XX_
