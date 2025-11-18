# Deployment Guide

This guide covers deployment options for the Binance Quantitative Trading Bot in various environments.

## 📋 Table of Contents

- [System Requirements](#system-requirements)
- [Pre-deployment Checklist](#pre-deployment-checklist)
- [Deployment Methods](#deployment-methods)
- [Configuration](#configuration)
- [Database Setup](#database-setup)
- [Security Hardening](#security-hardening)
- [Monitoring and Logging](#monitoring-and-logging)
- [Backup and Recovery](#backup-and-recovery)
- [Troubleshooting](#troubleshooting)

## 💻 System Requirements

### Minimum Requirements

- **OS**: Windows 10 (64-bit) version 1809 or later
- **CPU**: Intel Core i3 or equivalent (2 cores)
- **RAM**: 4 GB
- **Storage**: 10 GB available space
- **Network**: Stable internet connection (1 Mbps+)
- **.NET Runtime**: .NET 8.0 Desktop Runtime

### Recommended Requirements

- **OS**: Windows 11 (64-bit)
- **CPU**: Intel Core i5 or equivalent (4+ cores)
- **RAM**: 8 GB or more
- **Storage**: 50 GB SSD
- **Network**: High-speed internet (10 Mbps+)
- **.NET Runtime**: .NET 8.0 Desktop Runtime

### Software Dependencies

- .NET 8.0 Desktop Runtime (Windows)
- Visual C++ Redistributable (latest)
- Windows Terminal (recommended for logs)

## ✅ Pre-deployment Checklist

Before deploying, ensure you have:

- [ ] Valid Binance account with API credentials
- [ ] Tested strategies in paper trading mode
- [ ] Configured risk management parameters
- [ ] Reviewed and understood all security implications
- [ ] Prepared backup and disaster recovery plan
- [ ] Set up monitoring and alerting
- [ ] Read all documentation thoroughly

## 🚀 Deployment Methods

### Method 1: Standalone Executable

Best for single-machine deployment.

#### Step 1: Build the Application

```bash
# Clone the repository
git clone https://github.com/9529360-cpu/WPE-.git
cd WPE-

# Restore dependencies
dotnet restore

# Build in Release mode
dotnet build -c Release

# Publish self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

#### Step 2: Deploy Files

1. Navigate to `bin/Release/net8.0-windows/win-x64/publish/`
2. Copy the entire folder to your deployment location
3. The main executable is `币安量化机器人.exe`

#### Step 3: Initial Setup

```powershell
# Navigate to deployment directory
cd C:\Trading\BinanceBot

# Run the application
.\币安量化机器人.exe
```

### Method 2: Development Environment

Best for development and testing.

#### Step 1: Clone Repository

```bash
git clone https://github.com/9529360-cpu/WPE-.git
cd WPE-
```

#### Step 2: Open in Visual Studio

1. Open `币安量化机器人.slnx` in Visual Studio 2022
2. Restore NuGet packages
3. Build solution (Ctrl+Shift+B)
4. Run (F5) or Run without debugging (Ctrl+F5)

### Method 3: ClickOnce Deployment

For easy updates and distribution.

#### Step 1: Configure ClickOnce

In Visual Studio:
1. Right-click project → Properties
2. Go to Publish section
3. Configure publish settings:
   - Publishing Folder: Network share or web server
   - Installation Folder URL
   - Prerequisites: .NET 8.0 Desktop Runtime

#### Step 2: Publish

```powershell
# Command-line publish
msbuild /t:Publish /p:Configuration=Release /p:PublishUrl="\\server\share\"
```

#### Step 3: Distribute

Share the setup.exe with users. They can install and auto-update.

## ⚙️ Configuration

### Application Configuration

#### Create Configuration File

On first run, the app creates `Data/appsettings.json`. Configure it:

```json
{
  "AutoReconnect": true,
  "EnableNotifications": true,
  "Environment": "Production",
  "RefreshIntervalSeconds": 5,
  "LogLevel": "Info",
  "TelegramBotToken": "YOUR_TELEGRAM_BOT_TOKEN",
  "TelegramChatId": "YOUR_CHAT_ID",
  "DingTalkWebhook": "YOUR_DINGTALK_WEBHOOK"
}
```

#### Environment-Specific Configuration

For different environments, create separate config files:

- `appsettings.Development.json`
- `appsettings.Staging.json`
- `appsettings.Production.json`

Set the `Environment` property to load the correct file.

### API Configuration

#### Binance API Setup

1. Log in to [Binance](https://www.binance.com/)
2. Go to API Management
3. Create new API key
4. Enable required permissions:
   - ✅ Enable Reading
   - ✅ Enable Spot & Margin Trading
   - ❌ Enable Withdrawals (NOT recommended)
5. (Recommended) Enable IP whitelist
6. Save API Key and Secret Key securely

#### Configure API in Application

1. Launch the application
2. Navigate to "API" module
3. Enter API Key and Secret
4. Test connection
5. Keys are stored encrypted locally

### Strategy Configuration

1. Navigate to "策略配置" (Strategy Settings)
2. Select strategy type (Mean Reversion, Momentum, etc.)
3. Configure parameters:
   - Entry/Exit conditions
   - Position sizing
   - Risk limits
4. Save configuration

## 🗄️ Database Setup

### SQLite Database

The application uses SQLite for local storage.

#### Automatic Initialization

On first run, the database is created at `Data/trading.sqlite`.

#### Manual Database Creation

If needed, create manually:

```sql
-- Create tables for historical data
CREATE TABLE IF NOT EXISTS klines (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    interval TEXT NOT NULL,
    open_time INTEGER NOT NULL,
    open REAL NOT NULL,
    high REAL NOT NULL,
    low REAL NOT NULL,
    close REAL NOT NULL,
    volume REAL NOT NULL,
    close_time INTEGER NOT NULL,
    UNIQUE(symbol, interval, open_time)
);

CREATE INDEX idx_klines_symbol_interval ON klines(symbol, interval);
CREATE INDEX idx_klines_time ON klines(open_time);

-- Create table for trades
CREATE TABLE IF NOT EXISTS trades (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    side TEXT NOT NULL,
    price REAL NOT NULL,
    quantity REAL NOT NULL,
    commission REAL NOT NULL,
    timestamp INTEGER NOT NULL
);

CREATE INDEX idx_trades_symbol ON trades(symbol);
CREATE INDEX idx_trades_timestamp ON trades(timestamp);

-- Create table for positions
CREATE TABLE IF NOT EXISTS positions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL UNIQUE,
    quantity REAL NOT NULL,
    entry_price REAL NOT NULL,
    current_price REAL,
    unrealized_pnl REAL,
    updated_at INTEGER NOT NULL
);
```

#### Database Maintenance

```powershell
# Backup database
Copy-Item "Data\trading.sqlite" "Data\trading.backup.sqlite"

# Vacuum database (compact)
sqlite3 Data\trading.sqlite "VACUUM;"

# Check integrity
sqlite3 Data\trading.sqlite "PRAGMA integrity_check;"
```

### External Database (Optional)

For production, consider using external database:

1. Update `DatabaseDataSource` connection string
2. Migrate schema to PostgreSQL or SQL Server
3. Update `ServiceLocator.CreatePipeline()` method

## 🔒 Security Hardening

### API Key Security

1. **Never commit API keys** to version control
2. **Use environment variables** for sensitive data
3. **Enable IP whitelist** on Binance API settings
4. **Limit API permissions** (no withdrawal access)
5. **Rotate keys regularly** (every 90 days)
6. **Use separate keys** for testing and production

### Application Security

#### Windows Security

```powershell
# Run with least privilege (non-admin user)
# Set file permissions
icacls "Data" /grant:r "Users:(OI)(CI)M"
icacls "Data\trading.sqlite" /grant:r "Users:M"
```

#### Firewall Configuration

```powershell
# Allow only Binance API endpoints
New-NetFirewallRule -DisplayName "Binance API" `
    -Direction Outbound `
    -Action Allow `
    -RemoteAddress "13.*.*.*, 54.*.*.*" `
    -Protocol TCP `
    -RemotePort 443
```

#### Encryption

Consider encrypting sensitive files:

```powershell
# Encrypt Data directory
cipher /E /S:Data
```

### Network Security

1. Use VPN when trading remotely
2. Monitor unusual network activity
3. Keep Windows and .NET updated
4. Enable Windows Defender or antivirus

## 📊 Monitoring and Logging

### Application Logging

Logs are written to `Data/logs/` directory.

#### Log Levels

- **Debug**: Detailed diagnostic information
- **Info**: General informational messages
- **Warning**: Unexpected but handled events
- **Error**: Error events that might still allow the application to continue
- **Critical**: Critical failures causing shutdown

#### Configure Logging

In `appsettings.json`:

```json
{
  "LogLevel": "Info"
}
```

### System Monitoring

#### Performance Counters

Monitor these metrics:

- CPU usage (should be <50% average)
- Memory usage (should be <2 GB)
- Disk I/O (check for bottlenecks)
- Network latency (should be <100ms to Binance)

#### Windows Task Manager

Create a scheduled task to restart the app if it crashes:

```powershell
# Create scheduled task
$action = New-ScheduledTaskAction -Execute "C:\Trading\BinanceBot\币安量化机器人.exe"
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName "BinanceBot" -Action $action -Trigger $trigger -Settings $settings
```

### Alert Configuration

#### Telegram Notifications

1. Create Telegram bot via [@BotFather](https://t.me/botfather)
2. Get bot token
3. Get your chat ID from [@userinfobot](https://t.me/userinfobot)
4. Configure in `appsettings.json`

#### DingTalk Notifications

1. Create custom robot in DingTalk group
2. Copy webhook URL
3. Configure in `appsettings.json`

## 💾 Backup and Recovery

### Backup Strategy

#### What to Backup

- `Data/trading.sqlite` - Trading history
- `Data/appsettings.json` - Configuration
- `Data/ai/` - AI models
- Strategy configurations
- Log files (optional)

#### Automated Backup Script

```powershell
# backup.ps1
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = "C:\Trading\Backups\$timestamp"
New-Item -ItemType Directory -Path $backupDir

Copy-Item "Data\trading.sqlite" "$backupDir\trading.sqlite"
Copy-Item "Data\appsettings.json" "$backupDir\appsettings.json"
Copy-Item "Data\ai" "$backupDir\ai" -Recurse

# Compress backup
Compress-Archive -Path $backupDir -DestinationPath "$backupDir.zip"
Remove-Item $backupDir -Recurse

# Retention: Keep last 30 days
Get-ChildItem "C:\Trading\Backups" -Filter "*.zip" | 
    Where-Object { $_.CreationTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item
```

#### Schedule Backup

```powershell
# Schedule daily backup at 2 AM
$action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File C:\Trading\backup.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At 2am
Register-ScheduledTask -TaskName "TradingBotBackup" -Action $action -Trigger $trigger
```

### Recovery Procedures

#### Database Recovery

```powershell
# Stop the application first

# Restore from backup
$backupDate = "20241117_020000"
Copy-Item "C:\Trading\Backups\$backupDate\trading.sqlite" "Data\trading.sqlite"

# Restart application
```

#### Configuration Recovery

```powershell
# Restore configuration
Copy-Item "C:\Trading\Backups\$backupDate\appsettings.json" "Data\appsettings.json"
```

#### Disaster Recovery

If complete system failure:

1. Install .NET 8.0 Runtime on new machine
2. Copy application files from backup
3. Restore `Data` directory from backup
4. Reconfigure API keys (if not backed up)
5. Verify all configurations
6. Test in paper trading mode first

## 🔧 Troubleshooting

### Common Issues

#### Application Won't Start

**Symptoms**: Application crashes on startup

**Solutions**:
1. Check .NET 8.0 Runtime is installed
2. Verify all files are present
3. Check Windows Event Viewer for errors
4. Run as Administrator (temporarily)
5. Check antivirus isn't blocking

```powershell
# Check .NET runtime
dotnet --list-runtimes | Select-String "Microsoft.WindowsDesktop.App 8.0"
```

#### Database Errors

**Symptoms**: "Database is locked" or corruption errors

**Solutions**:
1. Close all instances of the application
2. Check file permissions
3. Run integrity check
4. Restore from backup if corrupted

```powershell
# Check database
sqlite3 Data\trading.sqlite "PRAGMA integrity_check;"

# If corrupted, restore backup
Copy-Item "Data\trading.backup.sqlite" "Data\trading.sqlite"
```

#### API Connection Issues

**Symptoms**: "Failed to connect to Binance API"

**Solutions**:
1. Check internet connection
2. Verify API keys are correct
3. Check IP whitelist settings on Binance
4. Verify API permissions
5. Check system time is synchronized
6. Test API endpoint manually

```powershell
# Test API connectivity
Invoke-WebRequest -Uri "https://api.binance.com/api/v3/ping"

# Check system time
w32tm /query /status
```

#### High CPU/Memory Usage

**Symptoms**: Application consuming excessive resources

**Solutions**:
1. Reduce refresh interval in settings
2. Limit number of active strategies
3. Reduce data retention period
4. Check for memory leaks (contact support)
5. Restart application

#### WebSocket Disconnections

**Symptoms**: Frequent reconnections

**Solutions**:
1. Check network stability
2. Enable AutoReconnect in settings
3. Increase timeout values
4. Use wired connection instead of WiFi

### Debug Mode

Enable detailed logging:

```json
{
  "LogLevel": "Debug"
}
```

Then check `Data/logs/` for detailed information.

### Getting Help

If issues persist:

1. Check [GitHub Issues](https://github.com/9529360-cpu/WPE-/issues)
2. Review application logs
3. Create new issue with:
   - Detailed problem description
   - Steps to reproduce
   - Log files (remove sensitive info)
   - System information

## 📈 Performance Optimization

### Database Optimization

```sql
-- Create indexes for better performance
CREATE INDEX IF NOT EXISTS idx_klines_lookup 
    ON klines(symbol, interval, open_time DESC);

-- Analyze database
ANALYZE;
```

### Application Optimization

1. **Limit historical data**: Keep only necessary timeframes
2. **Batch operations**: Process multiple symbols together
3. **Caching**: Enable data caching in settings
4. **Connection pooling**: Reuse WebSocket connections

## 🔄 Updates and Maintenance

### Updating the Application

1. Backup current installation
2. Download latest release
3. Stop current application
4. Replace executable files
5. Keep `Data` directory intact
6. Restart application
7. Verify functionality

### Maintenance Schedule

- **Daily**: Check logs for errors
- **Weekly**: Review trading performance, backup database
- **Monthly**: Update API keys (if policy requires), review and optimize strategies
- **Quarterly**: Update application to latest version, security audit

## 📞 Support

For deployment assistance:

- **Documentation**: [README.md](README.md)
- **Issues**: [GitHub Issues](https://github.com/9529360-cpu/WPE-/issues)
- **Discussions**: [GitHub Discussions](https://github.com/9529360-cpu/WPE-/discussions)

---

**Last Updated**: 2024-11-17

**Version**: 1.0.0
