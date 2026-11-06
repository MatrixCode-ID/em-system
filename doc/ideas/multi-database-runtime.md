# Multi-database runtime

- Date: 2026-11-06
- Status: executed for SQL Server and MySQL; see [the plan](../../plan/executed/multi-database-runtime.md)
- Scope decided by the user: the engine runs fully on the platform a host chooses. PostgreSQL stays a separate step.

## Decisions

- **One dialect per provider.** SQL the EF Core provider cannot produce lives behind `IEmSqlDialect`, chosen from the
  provider. Feature code states what it needs: a row lock, a schema check, the shape of a connection string.
- **Locks through one helper.** `DbSet<T>.LockRows(reach, keys...)` replaces every hand-written lock. SQL Server keeps the
  exact hints it had; MySQL uses `FOR UPDATE`.
- **Failures through one classifier.** `EmDbErrors` recognizes a unique conflict or a missing table for every provider,
  anywhere in the exception chain.
- **Data Approval follows the provider of the core database.** One transaction still means one connection, so the core and
  module databases must share one provider, one server and one login. Mixed providers are refused at startup. Atomic writes
  across providers would need a different design, such as an outbox with compensation.

## Differences that stay

These are platform behaviour, documented rather than hidden.

| Area | SQL Server | MySQL |
| --- | --- | --- |
| Default isolation | Read committed | Repeatable read: a plain read in a transaction sees the snapshot of its first plain read. Approval takes its lock first, so the lock wait comes before that snapshot. |
| `LIKE` under the default collation | Case-insensitive, accent-sensitive; `[` is a wildcard | Case- and accent-insensitive; `\` is an escape character, so a search for a literal backslash does not match |
| Aggregated text in views | `STRING_AGG`, no practical limit | `GROUP_CONCAT` is cut at `group_concat_max_len` (1024 by default) without an error. It only feeds display text: approval step names and the user accounts of an employee. |
| Filtered unique indexes | `WHERE` clause | Generated columns in the scripts; the model filters apply to SQL Server only |

## Follow-ups

- Container registry and deploy on MySQL have schema scripts but no runtime test.
- NuPak on MySQL is proven at startup only; publishing and feeds are not exercised.
- `LogStartupCheck` and `CtnStartupChecks` still name `doc/sqlscript/mssql` in their messages.
- `group_concat_max_len` could be raised per session if longer aggregated text ever matters.
- PostgreSQL needs its full schema (only the log tables exist), a dialect, an answer to object name casing, and another
  way to reach a second database, because a connection cannot change database there.
