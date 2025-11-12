# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 10.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10.0 upgrade.
3. Upgrade 币安量化机器人.csproj
4. Update NuGet package versions across projects as listed in Settings below.

## Settings

### Excluded projects

| Project name                                   | Description                 |
|:-----------------------------------------------|:---------------------------:|


### Aggregate NuGet packages modifications across all projects

| Package Name                        | Current Version | New Version | Description                                   |
|:------------------------------------|:---------------:|:-----------:|:----------------------------------------------|
| Microsoft.Data.Sqlite               |   8.0.4         |  10.0.0     | Replace with 10.0.0 for .NET 10 compatibility  |
| Microsoft.Extensions.Configuration.Json | 9.0.10     |  10.0.0     | Replace with 10.0.0 for .NET 10 compatibility  |
| ScottPlot.WPF                       |   5.0.56        |  4.1.73     | Downgrade to 4.1.73 (compatible with target)  |
| System.Text.Json                    |   9.0.10        |  10.0.0     | Replace with 10.0.0 for .NET 10 compatibility  |

### Project upgrade details

#### 币安量化机器人.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0-windows` to `net10.0-windows`

NuGet packages changes:
  - `Microsoft.Data.Sqlite` update from `8.0.4` to `10.0.0` (*recommended for .NET 10*)
  - `Microsoft.Extensions.Configuration.Json` update from `9.0.10` to `10.0.0` (*recommended for .NET 10*)
  - `System.Text.Json` update from `9.0.10` to `10.0.0` (*recommended for .NET 10*)
  - `ScottPlot.WPF` change from `5.0.56` to `4.1.73` (*compatibility recommendation from analysis*)

Feature upgrades:
  - Ensure any Windows-specific APIs still supported under `net10.0-windows` and adjust if APIs moved or deprecated.

Other changes:
  - Rebuild solution and fix compile errors introduced by package and framework changes.
  - Run full test/build and verify runtime behavior.
