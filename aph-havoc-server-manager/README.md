# APH Havoc Server Manager

APH Havoc Server Manager is the free public DayZ server-management companion supplied with
KM DayZ Suite. It provides one workspace for server and client lifecycle, multiple server
instances, ordered mod loadouts, Steam Workshop search/sign-in/downloads, per-instance mod
deployment and keys, FTP/explicit-FTPS remote files, BattlEye RCon, Discord activity and update
webhooks, logs, Central Economy editors, DayZ Tools, build/signing workflows, setup checks, CLI
automation and MCP integration.

Public source and licence:

- <https://github.com/Stryka2022/Km-Dayz-Suite>
- <https://github.com/Stryka2022/Km-Dayz-Suite/blob/main/LICENSE>

## Public licence

APH Havoc Server Manager is free software distributed under the GNU General Public License
version 3. You may use, study, modify and redistribute it under the terms of that licence.
Modified distributions must preserve the GPL, provide corresponding source, mark their changes,
and retain copyright notices.

APH Havoc public edition and KM integration © 2026 APH Havoc Survival.

## Original upstream attribution

This is a modified public edition of DayZ Labs. Original work copyright © 2026 Borcioo
(DayZ Labs), originally published at <https://github.com/Borcioo/dayz-labs> under GNU GPL v3.
That attribution does not imply endorsement of APH Havoc or KM DayZ Suite. The complete change
notice is in `NOTICE.txt` in distributed packages and `ThirdParty/DayZLabs/NOTICE.txt` in the KM
source workspace.

The GPL companion remains a separate process from the proprietary `KMSuite.exe` host. KM embeds
the companion window for a single-window experience without linking or loading companion
assemblies into the proprietary process.

## KM integration

When hosted by KM DayZ Suite, the companion uses the KM left navigation and keeps its dashboard,
server editor and setup wizard inside the same application window. The server workspace includes
dedicated destinations for server instances, Steam Workshop, notifications and scheduled update
checks, mods, economy, FTP/FTPS and BattlEye RCon, logs, bases, tools, setup, MCP, settings and legal
information.

Instances support a friendly display name independently of their filesystem-safe unique folder,
an editable collision-free random port, and an optional isolated DayZ Dedicated Server install.
Workshop selections, Discord webhook profiles and schedules are stored per server instance.

## Build

The Windows desktop app and CLI use the .NET SDK. From this source directory:

```powershell
dotnet build Dzl.sln -c Release
dotnet test tests/Dzl.Core.Tests/Dzl.Core.Tests.csproj -c Release
dotnet test tests/Dzl.Tray.Tests/Dzl.Tray.Tests.csproj -c Release
```

Internal `Dzl.*` namespaces, project names, configuration keys and compatibility filenames are
retained so existing profiles, automation and upgrades continue to work. They are implementation
identifiers, not the public product name.

## Platform support

- Windows x64: full KM desktop integration and APH Havoc Server Manager workspace.
- Linux x64: native headless `aph-havoc` CLI package for server-side management and validation;
  use `aph-havoc --version` for the build version and `aph-havoc environment` for OS, architecture,
  installed SDK/runtime and .NET 11 detection (`--json` is supported).
- Linux remote servers: manageable from Windows through the integrated FTP/FTPS and RCon pages.

## Disclaimer

APH Havoc Server Manager is an unofficial community tool. It is not affiliated with or
authorized by Bohemia Interactive a.s. Bohemia Interactive, ARMA, DAYZ, ENFUSION and associated
logos and designs are trademarks or registered trademarks of Bohemia Interactive a.s.
