# Multi-database runtime for SQL Server and MySQL

Design and decisions: [doc/ideas/multi-database-runtime.md](../../doc/ideas/multi-database-runtime.md). Requested directly by
the user on 2026-11-06.

## Done

1. **Startup.** `Api/Shared/EmSqlDialect.cs` adds `IEmSqlDialect`, `EmSqlDialects` and `LockRows`. The schema checks of
   `NuPakStartup` and `ObjectStorageBackground` go through `VerifySchemaAsync`. Every `WITH (UPDLOCK...)` goes through
   `LockRows`, including the SMTP metadata lock. `ObjectStorageMaintenance` no longer names a schema.
2. **Correct features.** `Api/Shared/EmDbErrors.cs` replaces the SQL Server-only checks in approval, tenants, departments,
   NuPak, object storage and SMTP. `ApprovalTransaction`, `ApprovalDatabaseSwitch` and `ApprovalStartupChecks` now follow the
   dialect, and `UseEmProvider` attaches the database switch for MySQL. Column collations and index filters in the models apply
   to SQL Server only.
3. **Proof.** New tests: `EmSqlDialectTests` (no database) and, against a real server through `EM_TEST_MYSQL`,
   `MySqlRuntimeTests` and `MySqlApprovalTests`.

## Verification

| Check | Result |
| --- | --- |
| `test-multi-database-regression`, run `20261106-131504-test-multi-database-regression-14c8` | `Em.Libs.Tests` 44/44, `Em.Api.Core.Tests` 286/286, `Em.Api.Core.IntegrationTests` 129 passed and 4 skipped of 133 (the skips need `EM_TEST_PG`) |
| `test-multi-database-mysql`, run `20261106-131027-test-multi-database-mysql-2f71` | Dialects 16/16, MySQL 17/17 |
| Lock on MySQL | Two connections: an update waits while `LockRows` is held, under both shapes EF Core produces. Other rows stay free, and a range lock holds a key that does not exist yet. Six concurrent uploads of one key give versions 1 to 6. |
| `ustamp` stale check on MySQL | A stale update answers 409 and the fresh one saves |
| `LIKE` on MySQL | Ignores case and accents, as recorded in the idea note |
| Data Approval on MySQL | Two databases on one connection: commit and rollback reach both, and a request applies when approved |
| `test-mysql-approval-http`, run `20261106-131726-test-mysql-approval-http-2aa1` | 12 checks, 0 failures, including rollback of a failed approval. The run itself was stopped by hand afterwards because the action stayed open. |
| Host start | `Em.Api` on MySQL. Two product hosts: one on MySQL and one on SQL Server, both through their RunBay actions. |

`SuppressedScopeWritesNothingAndTheLoggerNeverThrows` depended on test order, because the first log provider of a process takes
the static bootstrap buffer. The test helper now empties that buffer first; engine code did not change for it.

## Not verified

- Desktop clients were not run against MySQL.
- Container registry and deploy on MySQL; NuPak beyond its startup check.
- PostgreSQL is out of scope. A provider without a dialect fails where the engine needs one, and Data Approval refuses it at startup.

No NuGet release note was added: a new version note on `main` starts a release.
