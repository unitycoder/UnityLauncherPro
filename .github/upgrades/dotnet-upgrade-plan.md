# .NET 8.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that a .NET 8.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 8.0 upgrade.
3. Upgrade UnityLauncherPro\UnityLauncherPro.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

No projects are excluded from the upgrade.

### Project upgrade details

This section contains details about each project upgrade and modifications that need to be done in the project.

#### UnityLauncherPro\UnityLauncherPro.csproj modifications

Project format changes:
  - Project file needs to be converted to SDK-style format

Project properties changes:
  - Target framework should be changed from `net48` to `net8.0-windows`

Other changes:
  - WPF project properties need to be configured for .NET 8.0 (UseWPF=true)
  - Assembly information may need to be migrated from AssemblyInfo.cs to project properties
  - Project references and file includes will be automatically inferred by SDK-style format