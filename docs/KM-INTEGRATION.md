# KM DayZ Suite integration

APH Havoc Server Manager is the free GPL v3 companion used by KM DayZ Suite for DayZ server,
Workshop, mod, economy, FTP/FTPS, RCon, Discord webhook, logging, tooling, setup-check and
automation functions.

The distributed KM application and APH companion are an aggregate of legally separate programs:

- `KMSuite.exe` is the proprietary KM host and is not included in this GPL source repository.
- APH Havoc runs in its own process. KM may embed that process window in its Server Manager area.
- KM does not reference, link, merge, load or obfuscate APH assemblies inside `KMSuite.exe`.
- APH's full GPL licence, corresponding source and modification notice ship with the companion.

The `Dzl.*` namespaces, solution/project names, selected filenames and configuration identifiers
inside the source are compatibility identifiers inherited from the original GPL project. The
public product name is APH Havoc Server Manager. Original copyright and provenance are recorded
in [`../NOTICE.txt`](../NOTICE.txt) and must remain with redistributed modified copies.
