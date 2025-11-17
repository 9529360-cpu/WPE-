# Contributing to Binance Quantitative Trading Bot

Thank you for your interest in contributing! This document provides guidelines and instructions for contributing to this project.

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [How to Contribute](#how-to-contribute)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Commit Message Guidelines](#commit-message-guidelines)
- [Pull Request Process](#pull-request-process)
- [Project Structure](#project-structure)

## 🤝 Code of Conduct

### Our Pledge

We are committed to providing a welcoming and inspiring community for all. Please be respectful and constructive in your interactions.

### Our Standards

- Use welcoming and inclusive language
- Be respectful of differing viewpoints
- Accept constructive criticism gracefully
- Focus on what's best for the community
- Show empathy towards other community members

## 🚀 Getting Started

### Prerequisites

- Windows 10/11 (64-bit)
- Visual Studio 2022 or later
- .NET 8.0 SDK
- Git for version control
- Basic knowledge of C#, WPF, and quantitative trading

### Setting Up Development Environment

1. **Fork the Repository**
   ```bash
   # Click the "Fork" button on GitHub
   ```

2. **Clone Your Fork**
   ```bash
   git clone https://github.com/YOUR-USERNAME/WPE-.git
   cd WPE-
   ```

3. **Add Upstream Remote**
   ```bash
   git remote add upstream https://github.com/9529360-cpu/WPE-.git
   ```

4. **Install Dependencies**
   ```bash
   dotnet restore
   ```

5. **Build the Project**
   ```bash
   dotnet build
   ```

6. **Run Tests**
   ```bash
   dotnet test
   ```

## 🛠️ How to Contribute

### Types of Contributions

We welcome many types of contributions:

- **Bug Reports**: Found a bug? Report it!
- **Feature Requests**: Have an idea? Share it!
- **Code Contributions**: Fix bugs or add features
- **Documentation**: Improve or translate docs
- **Testing**: Write tests or test features
- **Design**: Improve UI/UX

### Reporting Bugs

Before creating a bug report, please check existing issues. When creating a bug report, include:

- **Clear title** describing the issue
- **Detailed description** of the problem
- **Steps to reproduce** the issue
- **Expected behavior** vs actual behavior
- **Screenshots** if applicable
- **Environment details** (OS version, .NET version, etc.)
- **Logs** if available

**Bug Report Template:**
```markdown
## Description
[Clear description of the bug]

## Steps to Reproduce
1. Go to '...'
2. Click on '...'
3. See error

## Expected Behavior
[What you expected to happen]

## Actual Behavior
[What actually happened]

## Environment
- OS: Windows 11
- .NET Version: 8.0.0
- Application Version: 1.0.0

## Additional Context
[Any other relevant information]
```

### Suggesting Features

Feature requests are welcome! Please provide:

- **Clear use case** for the feature
- **Expected behavior** and workflow
- **Benefits** to users
- **Potential implementation** approach (optional)
- **Mockups** or examples (if UI-related)

### Code Contributions

1. Check existing issues or create one
2. Comment on the issue to claim it
3. Fork and create a branch
4. Implement your changes
5. Write/update tests
6. Update documentation
7. Submit a pull request

## 🔄 Development Workflow

### Branch Naming Convention

Use descriptive branch names:

- `feature/your-feature-name` - New features
- `fix/issue-description` - Bug fixes
- `docs/what-you-document` - Documentation
- `refactor/what-you-refactor` - Code refactoring
- `test/what-you-test` - Test additions

Example:
```bash
git checkout -b feature/add-bollinger-bands-indicator
```

### Keeping Your Fork Updated

```bash
# Fetch upstream changes
git fetch upstream

# Merge upstream main into your branch
git checkout main
git merge upstream/main

# Push updates to your fork
git push origin main
```

## 📝 Coding Standards

### C# Style Guidelines

Follow these conventions:

1. **Naming Conventions**
   ```csharp
   // PascalCase for classes, methods, properties
   public class TradingStrategy { }
   public void ExecuteTrade() { }
   public string StrategyName { get; set; }
   
   // camelCase for local variables and parameters
   var tradingPair = "BTCUSDT";
   public void ProcessOrder(string orderType) { }
   
   // _camelCase for private fields
   private readonly ILogger _logger;
   ```

2. **Code Organization**
   - One class per file
   - Group related methods together
   - Keep methods short and focused (< 50 lines ideally)
   - Use regions sparingly

3. **Comments and Documentation**
   ```csharp
   /// <summary>
   /// Executes a trading strategy based on market conditions.
   /// </summary>
   /// <param name="observation">Current market observation</param>
   /// <returns>Trading decision with confidence score</returns>
   public async ValueTask<StrategyDecision> EvaluateAsync(
       MarketObservation observation)
   {
       // Implementation
   }
   ```

4. **Async/Await Best Practices**
   - Use `async`/`await` for I/O operations
   - Prefer `ValueTask` for hot paths
   - Always pass `CancellationToken`
   - Avoid `async void` except for event handlers

5. **Error Handling**
   ```csharp
   try
   {
       await PerformRiskyOperation();
   }
   catch (SpecificException ex)
   {
       _logger.LogError(ex, "Descriptive error message");
       throw; // or handle appropriately
   }
   ```

6. **LINQ and Collections**
   - Use LINQ for readable queries
   - Prefer `List<T>` over arrays for mutable collections
   - Use `IEnumerable<T>` for method parameters when possible
   - Consider `IAsyncEnumerable<T>` for streaming data

### XAML Style Guidelines

1. **Formatting**
   ```xaml
   <Button
       Content="Execute Trade"
       Command="{Binding ExecuteCommand}"
       IsEnabled="{Binding CanExecute}"
       Margin="10,5"
       Padding="15,8" />
   ```

2. **Resource Organization**
   - Define styles in resource dictionaries
   - Use meaningful `x:Key` names
   - Group related resources

3. **Data Binding**
   - Use `{Binding}` with proper paths
   - Implement `INotifyPropertyChanged` for ViewModels
   - Validate binding paths

## 🧪 Testing Guidelines

### Writing Tests

1. **Unit Tests**
   ```csharp
   [Fact]
   public async Task Strategy_ShouldGenerateBuySignal_WhenPriceIsBelowMean()
   {
       // Arrange
       var strategy = new MeanReversionStrategy(/* params */);
       var observation = CreateTestObservation(price: 95);
       
       // Act
       var decision = await strategy.EvaluateAsync(observation);
       
       // Assert
       Assert.Equal(TradeActionType.Buy, decision.Action.ActionType);
   }
   ```

2. **Test Naming**
   - Use descriptive names: `MethodName_Scenario_ExpectedResult`
   - Be specific about what you're testing
   - Make failures easy to understand

3. **Test Coverage**
   - Aim for >80% code coverage
   - Focus on critical business logic
   - Test edge cases and error paths

4. **Performance Tests**
   - Use BenchmarkDotNet for performance tests
   - Define `#if BENCHMARKS` sections
   - Document performance expectations

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test
dotnet test --filter "FullyQualifiedName~StrategyTests"

# Run benchmarks
dotnet run -c Release --define BENCHMARKS
```

## 📨 Commit Message Guidelines

### Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Maintenance tasks

### Examples

```
feat(strategy): add Bollinger Bands indicator

Implement Bollinger Bands calculation and integrate it
into the technical indicator engineer.

Closes #123
```

```
fix(risk): correct Kelly criterion calculation

Fixed division by zero error in Kelly allocator when
variance is zero.

Fixes #456
```

### Best Practices

- Use present tense: "add feature" not "added feature"
- Keep subject line under 50 characters
- Capitalize subject line
- Don't end subject with a period
- Use body to explain what and why, not how
- Reference issues and PRs in footer

## 🔀 Pull Request Process

### Before Submitting

1. ✅ Update your branch with latest upstream changes
2. ✅ Run all tests and ensure they pass
3. ✅ Update documentation if needed
4. ✅ Add/update tests for your changes
5. ✅ Follow coding standards
6. ✅ Write clear commit messages

### Submitting PR

1. **Create Pull Request** on GitHub
2. **Fill out PR template** completely
3. **Link related issues** (e.g., "Closes #123")
4. **Request reviewers**
5. **Wait for CI checks** to pass

### PR Template

```markdown
## Description
[Brief description of changes]

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Related Issues
Closes #[issue number]

## Testing
- [ ] Unit tests added/updated
- [ ] Manual testing completed
- [ ] All tests pass

## Checklist
- [ ] Code follows style guidelines
- [ ] Self-review completed
- [ ] Documentation updated
- [ ] No new warnings generated
```

### Review Process

1. **Code review** by maintainers
2. **Address feedback** promptly
3. **Update PR** based on comments
4. **Approval** from at least one maintainer
5. **Merge** by maintainer

### After Merge

- Delete your branch
- Close related issues
- Update your fork

## 📁 Project Structure

```
├── Core/                    # Domain layer
│   ├── Abstractions/       # Interfaces
│   ├── Models/             # Domain models
│   ├── Strategies/         # Trading strategies
│   ├── Risk/               # Risk management
│   └── Data/               # Data abstractions
├── Application/            # Application layer
│   ├── Backtesting/       # Backtest engine
│   └── Services/          # Application services
├── Infrastructure/         # Infrastructure layer
│   └── Data/              # Data implementations
├── Services/              # External services
│   ├── BinanceApiClient.cs
│   ├── ServiceLocator.cs
│   └── ...
├── Modules/               # UI modules (WPF)
│   ├── Market/
│   ├── Strategy/
│   ├── Trade/
│   └── ...
├── Tests/                 # Test projects
├── Data/                  # Data storage
└── Docs/                  # Documentation
```

### Adding New Modules

When adding a new UI module:

1. Create directory in `Modules/`
2. Add XAML view and code-behind
3. Register in `MainWindow._viewMap`
4. Update navigation buttons in `MainWindow.xaml`
5. Document the module

## 💡 Tips for Contributors

### For First-Time Contributors

- Start with "good first issue" labels
- Read existing code to understand patterns
- Don't hesitate to ask questions
- Small PRs are easier to review

### For Experienced Contributors

- Help review other PRs
- Mentor new contributors
- Improve documentation
- Optimize performance

### Communication

- Be clear and concise
- Provide context in discussions
- Be patient and respectful
- Share knowledge

## 📚 Resources

### Learning Resources

- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [WPF Tutorial](https://docs.microsoft.com/windows/apps/desktop/wpf/)
- [Binance API Docs](https://binance-docs.github.io/apidocs/)
- [Quantitative Trading Books](https://www.quantstart.com/reading-list/)

### Tools

- [Visual Studio](https://visualstudio.microsoft.com/)
- [ReSharper](https://www.jetbrains.com/resharper/) (optional)
- [GitHub Desktop](https://desktop.github.com/) (optional)

## ❓ Questions?

- Open a [GitHub Discussion](https://github.com/9529360-cpu/WPE-/discussions)
- Check [FAQ in README](README.md)
- Review existing issues

## 🎉 Recognition

Contributors will be recognized in:
- README.md contributors section
- Release notes
- Project documentation

Thank you for contributing! 🙏

---

**Note**: These guidelines may evolve. Check back periodically for updates.

Last updated: 2024-11-17
