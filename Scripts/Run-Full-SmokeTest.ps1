# Full smoke test script for AI trading system (simulation)
# Usage: Run in repository root with PowerShell 7+

param(
    [int]$RunSeconds = 60
)

Write-Host "Starting full smoke test for $RunSeconds seconds..."

# 1. Build
Write-Host "Building solution..."
dotnet build -c Release

# 2. Start app (headless) - use a small console runner if available
# For WPF use a background test harness: call a test runner that can instantiate services

Write-Host "Initializing services via dotnet script..."

$script = @'
using System;
using System.Threading.Tasks;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

class Runner
{
    public static async Task<int> Main()
    {
        try
        {
            var coord = ServiceLocator.GetAICentralCoordinator();
            await coord.StartAsync();

            await ServiceLocator.AutoTrader.StartAsync(new[] { "BTCUSDT", "ETHUSDT" }, AccountType.Simulated);

            await Task.Delay(TimeSpan.FromSeconds(60));

            await ServiceLocator.AutoTrader.StopAsync();
            await coord.StopAsync();

            Console.WriteLine("Smoke test completed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex}");
            return 1;
        }
    }
}
'@

# Write temporary runner
$runnerFile = Join-Path $PWD "SmokeRunner.cs"
$script | Out-File -FilePath $runnerFile -Encoding UTF8

# Compile and run using dotnet script (if dotnet-script installed) or csc/dotnet run project approach.
# Try dotnet run in a tiny console project not available; instead use dotnet script if present.

if (Get-Command dotnet-script -ErrorAction SilentlyContinue) {
    dotnet-script $runnerFile --no-restore
} else {
    Write-Host "dotnet-script not found; please run smoke runner manually or install dotnet-script.";
}

Write-Host "Smoke test script finished." 
