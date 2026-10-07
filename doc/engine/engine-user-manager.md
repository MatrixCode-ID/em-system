# User Manager: roles of a user

The user editor of User Manager (`Navigations/UserEditor.xaml`, WPF) has three tabs: **Profile**,
**Roles** and **Activity**. This guide covers the Roles tab. Roles themselves (their names and the claims
they carry) are managed in Role Manager.

## Roles tab

The tab shows one card for every role on the server, sorted by name. Each card has:

- the role name, its description and the number of permissions it carries;
- a switch that gives the role to the open account;
- **Start** and **Expiry** dates, both optional, enabled only while the switch is on;
- chips: `INACTIVE` for a disabled role, `SCHEDULED` when the assignment starts later, `EXPIRED` when it
  has already ended.

A disabled role can still be given, but it grants nothing until it is active again: the server only counts
active roles when it reads a user's permissions.

The refresh button in the top right corner reloads the roles and the account's assignments; it asks first
when role changes would be lost.

The search box under the header narrows the cards to the roles whose name or description contains the
typed text, ignoring case, and shows how many roles match. Search only hides cards: a hidden card keeps
its switch and dates, and **Save** still sends its changes. The search text stays when another account is
opened or the roles are reloaded.

### Saving

Role changes are saved with the editor's **Save Changes** button, together with the rest of the record:

- switching a card or moving one of its dates marks the record changed, shows the *Unsaved changes* chip,
  and makes leaving the screen ask for confirmation;
- **Discard** puts the cards back as well;
- a card whose expiry falls before its start shows *Expiry is before start.* and holds Save back.

On Save the editor writes the user row only when it really changed (a new user is inserted first, so it has
an id), then sends the role difference in the same order Role Manager uses: removed assignments
(`PostTa_UserRole_DeleteBatch`), new ones (`PostTa_UserRole_NewBatch`), then one
`PostTa_UserRole_Update` per assignment whose period changed. Every call tolerates being repeated: when the
roles fail after the user row was stored, the role changes stay marked and the next Save sends them again.

A new user can have roles switched on before it is saved; one Save creates the user and its assignments.

### Dates

The pickers choose a day. A start is stored at 00:00 of that day and an expiry at 23:59:59, in local time
like the other `datetime` columns. An empty date means no limit. A stored period whose time is not one of
those two keeps its exact value as long as its day is not changed, so opening and saving a user changes
nothing. A rescheduled assignment keeps its original creation stamp.

### System accounts

The debugger account and the built-in administrator hold every permission and have no user row a role could
point to. When the editor opens one of them, the cards are shown locked with a note saying so.

### Permissions

Changing roles needs an administrator on the server (`RequireAdmin`); a refusal is shown like any other
save error. Rules are read when a user signs in, so a change takes effect at the account's next sign in.

## Maintainer notes

- Render harness (light/dark, changed, period error, new user, system account, busy, loading, error, narrow):
  `dotnet run --project ..\.artefacts\em-system\scripts\user-roles-render`.
- Live smoke against a local `Em.Api` with the development debug key:
  `dotnet run --project ..\.artefacts\em-system\scripts\user-roles-smoke`; remove its rows afterwards with
  `cleanup.sql` in the same folder (see its header).
