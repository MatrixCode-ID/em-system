# Port Em SQL scripts to MySQL

Status: in progress. The existing MySQL log scripts are retained. All 21 view definitions have been translated. A RunBay smoke test created them against generated fixture tables on local MySQL and selected from them successfully; it did not validate the real schema or compare data semantics.

- Convert the ULID implementation and all application table scripts, preserving constraints, indexes, defaults and installation order. MySQL restrictions on stored-function defaults and filtered indexes require explicit equivalents.
- Review the JSON and GROUP_CONCAT behavior of the views, including MySQL's group_concat_max_len limit, against SQL Server.
- Evaluate and translate each historical upgrade script separately; do not execute upgrades for a fresh installation.
- Exercise a fresh installation and reruns against an isolated local MySQL database, compare schema and view behavior with SQL Server, and document limitations and installation instructions.
- Verify application compatibility separately; translating DDL alone does not establish runtime provider support.

View smoke: RunBay `em-system` action `test-mysql-views`, run `20261106-094550-test-mysql-views-9101`. The fixture database was removed after the test. Harness: `../.artefacts/em-system/scripts/mysql-view-smoke/`.

## Local progress (2026-11-06)

A persistent local MySQL database `em_system_mysql` was created at the user's request. It contains the existing 070/071 log tables and three ULID generation/conversion functions; it is not a complete application schema. RunBay `install-mysql-local-partial` run `20261106-095003-install-mysql-local-partial-0b92` succeeded. Do not overwrite the database when continuing; inspect its state and apply only the missing, verified scripts.

`sets/000-ulid.sql` currently ports NewUlid, NewUlidString and NewUlidStringFromBytes only. Cysharp `Ulid` 1.4.1 validated 1,002 binary-to-text vectors plus 1,000 generated values, uniqueness and timestamp. RunBay `test-mysql-ulid-cysharp` run `20261106-094834-test-mysql-ulid-cysharp-b24a` printed PASS and removed its fixture database, but remained running afterward and was stopped; its final RunBay status is stopped. Harness: `../.artefacts/em-system/scripts/mysql-ulid-smoke/`. Complete the other ULID functions and installation tests.

## Further progress (2026-11-06)

`tables/050-smtp.sql` and `views/vi_Smtp.sql` were installed in `em_system_mysql` after disposable-database testing of rerun, multiple non-defaults, the unique default and port validation. RunBay test `20261106-095210-test-mysql-smtp-schema-13dd` and installation `20261106-095221-install-mysql-smtp-local-d9e2` passed; the SMTP table is empty.

The ULID file now includes binary/string random and date extraction helpers as well as decoding. Cysharp test run `20261106-095405-test-mysql-ulid-cysharp-6ac7` reported successful binary/string comparisons, random-part checks, invalid inputs and timestamps; the fixture database was removed but the lingering run was stopped. The functions were installed locally using `20261106-095500-install-mysql-ulid-local-5fe8`. MySQL dates are UTC DATETIME(3), without SQL Server's offset type. Remaining work still includes all other application tables, upgrade scripts, real-schema view behavior, and full installation/runtime tests.

## Table-only scope and object-name casing (2026-11-06)

The user clarified that this work is limited to creating the MySQL tables; application/EF Core testing
will be performed by the user while working. Runtime code changes made during investigation were reverted.
The user's separate MySQL instance uses port 3307 and lower_case_table_names=2; its `em_system_mysql`
database contains all 70 tables with names matching SQL Server exactly. The former instance on 3306 and
its database were not replaced. RunBay `install-mysql-preserved-local` run `20261106-102519-install-mysql-preserved-local-5642`
created the tables; `test-mysql-name-case` run `20261106-102548-test-mysql-name-case-10af` compared all 70
names and proved the installer rejects a lowercase-only server before database creation. Initializer test
`20261106-102642-test-mysql-instance-init-a37d` confirmed new directory creation and refusal to reuse an
existing directory. Scripts and usage: `scripts/mysql-server-init/`, `scripts/mysql-schema-install/`,
`doc/sqlscript/mysql/README.md`. The old runtime smoke database and artifact directories are retained as
fixtures; no claim of full runtime support is made.

## Runtime support (2026-11-06)

Runtime support was done separately at the user's request:
[plan/executed/multi-database-runtime.md](../executed/multi-database-runtime.md).
