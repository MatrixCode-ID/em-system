# MySQL schema installation

The SQL scripts retain the object names in the SQL Server scripts, for example `ta_Meta`. MySQL's
`lower_case_table_names` is an **instance-wide initialization setting**, not a database setting. It
cannot be changed for an existing data directory. The Em System standard requires value `2` to preserve names while comparing them without case.
This setting is not supported by every platform; do not silently substitute a different value.
Do not change the setting on an existing instance or data directory.

On Windows, with PowerShell 7, initialize a **new, non-existent** data directory (the script refuses to
change an existing directory). Replace the paths with the MySQL installation and data directory:

```powershell
.\scripts\mysql-server-init\mysql-server-init.ps1 -MySqlBaseDir '<mysql-base-dir>' -DataDir '<new-data-dir>'
```

The initializer creates an empty local root password. Start that MySQL instance in a separate terminal,
using the same directories and case setting, with an available port. For example:

```powershell
& '<mysql-base-dir>\bin\mysqld.exe' --no-defaults --console '--basedir=<mysql-base-dir>' '--datadir=<new-data-dir>' --lower-case-table-names=2 --bind-address=127.0.0.1 --port=3307
```

Configure access appropriately before exposing it beyond localhost. The instance must run before the next step.

Create a new database and install all numbered table scripts with:

```powershell
.\scripts\mysql-schema-install\mysql-schema-install.ps1 -MySql '<mysql-client-path>' -Server 127.0.0.1 -Port 3307 -User root -Database em_system_mysql
```

For a password-protected account, use a MySQL-supported credential mechanism such as a login path or
`MYSQL_PWD` set outside the command. The installer verifies the server setting before creating anything,
refuses an existing database, runs `sets/` and `tables/` in order, then verifies the exact name `ta_Meta`.
A failed installation retains its database for inspection. `900-emtest.sql` creates sample-module tables.
The `.cmd` wrappers under `scripts/` are also available for interactive Windows use.

Run `views/` separately after the tables only if needed. `updates/` is for an older MySQL installation,
not a new database; review the applicable migration and take a backup before using it. MySQL DDL implicitly
commits, so a failed migration can require manual repair.

The conversion creates tables and their constraints; it does not by itself establish that every Em System
runtime feature works with MySQL. Application testing is a separate step. Aggregating views use
`GROUP_CONCAT`, which is subject to the session's `group_concat_max_len` and may need configuration.
