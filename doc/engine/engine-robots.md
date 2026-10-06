# Robot identities

Robots are non-human identities for automated logins (`docker login`, NuGet push/restore, module
protocols). They are managed in the WPF **User Manager**, which has two tabs: **Users** for people and
**Robots** for robot identities. Feature managers (Container Manager, NuGet Manager, …) manage their own
resources; robot registration, tokens and grants live in User Manager.

## Services and claims

`IRobotServices` in module `Administrative Tools` requires the **User Manager Access** claim.
**Container Manager Access** does not allow managing robot identities, tokens or grants. Robots never get a
human login session or administrator rights.

| Action | Purpose |
| --- | --- |
| `GetMeta_Robots` | List robots |
| `GetMeta_RobotManagers` | Grant providers with their resources and access options |
| `GetMeta_RobotOwners` | Selectable owner accounts |
| `PostGetMeta_RobotCreate(name, description?, tokenExpiry?, ownerUserId?)` | Create a robot; returns the token |
| `PostGetMeta_RobotRegenerate` | Issue a new token; the old one stops working immediately |
| `PostMeta_RobotUpdate`, `PostMeta_RobotDelete` | Edit or delete |
| `PostMeta_RobotAccessSet(robotId, managerId, resourceId, access)` | Set a grant; an empty `access` revokes it (idempotent) |

The full token is returned only by create and regenerate. Tokens keep the `emc_` prefix. Only a SHA-256
hash is stored.

## Database

- New installations: `ta_Robot` is created by `doc/sqlscript/mssql/tables/010-core.sql`, with or without the
  container registry. Container grants per root live in `ta_CtnRootRobot` (`030-registry.sql`).
- Existing databases created before robots moved to User Manager: run
  `doc/sqlscript/mssql/updates/20261003-RobotUserManager.sql` before starting the new server. Do not run the
  new-installation DDL first on such a database, because it would create a separate identity table.
- Existing databases without the owner column: run `doc/sqlscript/mssql/updates/20261003-RobotOwner.sql`
  before starting the new server. It is idempotent and leaves existing robots without an owner.

## Owner account

A robot can optionally be linked to a user account as its owner.

- **New Robot** offers active user accounts (including regular administrators, but not the system accounts
  Admin and Debugger) and **No owner**, the default. The backend validates the selection.
- The **Owner account** dropdown has a **Search user account** box that matches part of the account name,
  case-insensitively. **No owner** is always available; typing does not change the owner until a result is
  selected.
- `RobotInfo.OwnerUserId` and `OwnerAccount` are returned by create, list and regenerate. Robot details show
  the owner; edit and regenerate keep the existing link.
- The link is stored in `ta_Robot.cRobotOwner_cUserId` with a foreign key to `ta_User.cUserId`. Deleting the
  user clears the link; the robot and its grants remain.

The link is ownership **metadata only**. It gives the owner no session, no claim inheritance and no
management rights, and the robot inherits nothing from the owner. A robot always uses its own token and
grants.

## Adding a grant provider

A feature that wants robot access implements `IRobotAccessManager` in the backend and registers it:

```csharp
builder.AddRobotAccessManager<MyRobotAccessManager>();   // scoped
```

| Member | Responsibility |
| --- | --- |
| `Id` | Unique and stable provider ID (for example `Container`) |
| `DescribeAsync` | Manager name and description, resources and access options (code + label) |
| `ReadAsync` | Existing grants |
| `SetAsync(robotId, resourceId, access)` | Validate the resource and code, then write or revoke the grant |
| `PrepareDeleteAsync(identities, robotId)` | Remove grants and references inside the supplied `RobotContext` transaction, without committing |
| `AfterDeleteAsync` | File cleanup after commit |

- The empty code is reserved for **No access**. A provider may offer access levels other than Read/Write.
- One robot can hold grants from several providers and on several resources at once; a robot has no single
  manager column.
- A provider that is not registered does not appear in the UI. Foreign keys that still reference the robot
  reject the delete, so data is never deleted partially. Keep the provider registered while its grant
  table is in use.
- User Manager renders the options from the backend, so a new provider needs no XAML changes.

At runtime the feature authenticates with `RobotAuth.AuthenticateAsync` (Basic credentials or a bare
token; see [Module protocol endpoints](engine-public-endpoints.md)) and then **must check its own grants**
on every operation. A valid identity alone grants nothing.

Built-in providers:

| Provider | Resource | Access |
| --- | --- | --- |
| `Container` | Registry root | `R` pull, `W` push + pull |
| `NuGet` | Feed / prefix | `R` restore, `W` restore + push + delete to recycle bin |

Robot registration remains available when the registry or NuGet server is not enabled.

## Maintainer notes

Local smoke test with owner migration (harness kept outside the repo):
`dotnet run --project ..\.artefacts\em-system\scripts\robot-smoke -- --migrate-owner`.
