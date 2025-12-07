# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 10.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10.0 upgrade.
3. Upgrade AIStoryBuilders\AIStoryBuilders.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

Table below contains projects that do belong to the dependency graph for selected projects and should not be included in the upgrade.

| Project name                                   | Description                 |
|:-----------------------------------------------|:---------------------------:|

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                          | Current Version | New Version | Description                                   |
|:--------------------------------------|:---------------:|:-----------:|:----------------------------------------------|
| Microsoft.EntityFrameworkCore.SqlServer | 8.0.8           | 10.0.0      | Recommended for .NET 10.0                     |
| Microsoft.Extensions.Logging.Debug    | 8.0.0           | 10.0.0      | Recommended for .NET 10.0                     |
| Newtonsoft.Json                       | 13.0.3          | 13.0.4      | Recommended for .NET 10.0                     |

### Project upgrade details
This section contains details about each project upgrade and modifications that need to be done in the project.

#### AIStoryBuilders\\AIStoryBuilders.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0-windows10.0.22621.0` to `net10.0-windows10.0.22621.0`

NuGet packages changes:
  - Microsoft.EntityFrameworkCore.SqlServer should be updated from `8.0.8` to `10.0.0` (recommended for .NET 10.0)
  - Microsoft.Extensions.Logging.Debug should be updated from `8.0.0` to `10.0.0` (recommended for .NET 10.0)
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (recommended for .NET 10.0)

Feature upgrades:
  - Review .NET MAUI compatibility for .NET 10 SDK versions of `Microsoft.Maui.*` packages and upgrade if required.

Other changes:
  - Validate platform-specific `SupportedOSPlatformVersion` and `TargetPlatformMinVersion` remain valid for net10.0.
