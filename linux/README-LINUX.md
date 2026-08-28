# APH Havoc Server Manager Linux CLI (x64)

Release version: `0.1.35-km.11` (the package also contains a plain-text `VERSION` file).

This archive is the native, headless Linux build of the legally separate GPL Server Manager companion bundled with KM DayZ Suite. It is intended for DayZ server owners who want configuration, status, log, Central Economy and validation commands on a Linux host.

Run:

```bash
chmod +x aph-havoc km-suite-server.sh
./km-suite-server.sh --help
./km-suite-server.sh --version
./km-suite-server.sh environment
./km-suite-server.sh environment --json
```

The launcher detects the operating system and CPU architecture before starting. This build supports Linux x86-64 (`x86_64`/`amd64`).
The `environment` command reports the detected Linux platform and architecture, installed .NET SDKs
and runtimes, and whether .NET 11 is available. The executable is self-contained, so a system .NET
installation is reported for diagnostics but is not required to run this package.

The KM desktop application, embedded Workshop window, DayZ game/client/server launch controls, DayZ Tools, P: WorkDrive, PBO builders and Windows DPAPI credential store are Windows-only and are not presented as Linux features. Linux-hosted server files can still be managed from the Windows KM app through its FTP/FTPS and BattlEye RCon workspace.

Licensing: `aph-havoc` is the APH Havoc Server Manager GPL v3 public edition. Its GPL licence, APH Havoc modification notice, original upstream attribution and full corresponding source are included in this archive. It is not covered by the proprietary KM DayZ Suite licence. Public source: https://github.com/Stryka2022/Km-Dayz-Suite
