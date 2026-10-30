# User Manager: user list and roles of a user

User Manager (`Navigations/UserManager.xaml`, WPF) lists the user accounts; its editor
(`Navigations/UserEditor.xaml`) has two tabs: **Profile** and **Roles**. This guide covers the list and
the Roles tab. An **Activity** tab (the account's history) is planned and stays hidden until it is built.
Roles themselves (their names and the claims they carry) are managed in Role Manager.

The screen is also the reference layout for WPF manager screens: toolbar, filter strip, list card, pager
and filter sheet follow the rules in `CLAUDE.md` ("Standar kerapian UI WPF").

## User list

Searching, filtering, sorting and paging all happen on the server in one call,
`PostGetMeta_UserListPage(UserListQuery)` on `ICredentialServices`, so they apply across every page, not only
the rows on screen. The answer (`UserListPage`) carries the page of users in the usual user shape, the roles
each of them holds in effect now, and the number of users matching across all pages. Any signed-in user may
call it (`RequireUserId`), like the other user and role reads. On the client,
`User.GetUserListPageAsync(app, query)` wraps the call and fills `User.RoleNames`.

Every change of search, filter or sort reads the list again from page 1. A change made while a read is
running is not lost: the list is read once more when the current read ends. The loading veil covers only the
rows, so the search box keeps its focus while typing.

### Search

The box in the toolbar matches the account, the full name or the email (the default communication value),
as a contains match. The list follows 350 ms after typing stops; the cross in the box empties it at once.

### Status

Two controls edit the same condition:

- the segments beside the list (**All**, **Active**, **Pending**, **Suspended**, **Inactive**) pick one state
  or none;
- the **STATUS** chips in the filter sheet may pick several.

One picked state lights its segment. Several picked states leave every segment off and show a
`Status: ...` chip instead. With no state picked, every state except **Deleted** is listed.

### Filter sheet

The **Filter** button opens a side sheet with:

- **STATUS** - see above;
- **ROLE** - the active roles on the server, by name. A user matches when it holds at least one picked role
  *in effect now*: the role is active and today falls inside the assignment's Start/Expiry period. This is
  the same rule the server uses to grant permissions, and the same rule as the role chips in the list;
- **Administrators only** - accounts with administrator privileges;
- **Include deleted accounts** - adds deleted accounts to whatever states are picked.

The sheet edits a draft filled from the conditions in force when it opens. **Apply** puts the draft in force;
**Reset** empties it and keeps the sheet open; the close button, a click on the scrim or `Escape` close it
without changing anything.

Conditions in force that the segments cannot show appear as chips above the list, one per condition, each
with its own remove button; **Clear all** removes them all (the search stays). The badge on the Filter button
counts the chips.

### Sorting

Clicking a column header sorts on it: ascending, then descending, then back to the default order (by user
id). Another column always starts ascending. The arrow beside the caption shows the direction; an unsorted
column shows a faint hint under the pointer. Sortable columns: account, full name, email, address, zip and
status. Roles and note have no single value to order by. Ties are ordered by user id so a row never appears
on two pages.

### Role badges

The roles column shows, in order:

- an accented `ADMIN` chip for accounts with administrator privileges;
- the names of the roles in effect now, as neutral chips;
- a `+N` chip for the roles that did not fit.

At most two chips are shown by name, `ADMIN` included, so an administrator shows one role by name. Hovering
the cell lists every role. The column takes a share of the leftover width: when it is narrow the chips wrap
to a second line one by one and a long role name ends in an ellipsis, so rows keep one height and the
action buttons stay in view.

Role names are read with the list; a role changed in the editor shows in the list after the next read. The
`ADMIN` chip follows the account's flag directly.

### Width

The columns fit a window from about 1280 px wide. Narrower than that, the last columns (note and the row
actions) are cut off, as before this change; the account, name and email columns are capped and end in an
ellipsis with the full value in a tooltip.

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
its switch and dates. The search text stays when another account is
opened or the roles are reloaded.

### Saving

For a stored user a role change is stored as soon as it is made; there is no separate Save for roles:

- switching a card, or moving one of its dates, sends that card's change straight away (removed:
  `PostTa_UserRole_DeleteBatch`, new: `PostTa_UserRole_NewBatch`, period changed: `PostTa_UserRole_Update`);
- the card shows a spinner and is locked while its change is on its way, so the stored state is exactly
  what was sent;
- a card whose expiry falls before its start shows *Expiry is before start.* and is not sent until the
  period is fixed; it stays marked with the accent dot meanwhile.

When a send fails, the error is shown and the card stays marked. Switching it again, or pressing
**Save Changes**, sends it once more; Save sends every card still marked after writing the user row.
Every call tolerates being repeated.

A new user has no id until its row is stored, so its cards are not sent on their own: they can be
switched and dated as usual, stay marked, and **Save Changes** creates the user and then sends its
assignments. From then on the cards save themselves.

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
- Unit tests of the list view model (query sent, status sync, sheet draft, chips, sort cycle, search, role
  badges) and a XAML load check: `UserManagerTests` in `tests/Em.Ui.Wpf.Core.Tests`.
- Render harness for the list (light/dark: list, sorted + searched + status segment, chips, open sheet,
  empty, loading, 1280 and 1100 wide):
  `dotnet run --project ..\.artefacts\em-system\scripts\user-manager-render`.
