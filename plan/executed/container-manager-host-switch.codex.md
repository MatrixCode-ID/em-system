# Container Manager host switch

- Date: 2026-10-20
- Status: implemented and verified through console checks.
- Scope: let API and WPF hosts opt out of Container Manager while preserving
  the default behavior of existing hosts.

## Implementation

Both builders expose `EnableContainerManager`, defaulting to `true`.
The API registers the container management service and its actions after the
host configuration callback, only when enabled. Registry protocol hosting
remains controlled separately through registry/storage configuration.

The WPF host applies the switch to the shared navigation access check before
administrator or debug bypass. The Tools menu and direct navigation therefore
both refuse Container Manager when disabled. Other tools retain their access
rules. MAUI has no Container Manager screen.

No new UI or mockup is required: this change disables an existing screen through
host configuration. No database schema or stored data is changed.

## Verification

- API registration tests: 2 passed; enabled/disabled service, status action and
  required action claim checked without connecting to a database.
- WPF DebugStateTests: 20 passed, including four new cases checking enabled and
  disabled Container Manager for administrator/debugger, Tools menu visibility,
  direct navigation access, and continued User Manager access.
- Consuming API and WPF host builds passed with zero warnings and errors.
- Diff whitespace checks passed with CRLF-aware whitespace settings.
- GUI interaction, live HTTP hosting, publishing and deployment were not run.

Guide: [Container registry](../../doc/engine/engine-registry.md).
