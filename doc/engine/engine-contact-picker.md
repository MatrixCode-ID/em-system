# Contact picker

The WPF `Em.Ui.Wpf.Dialogs.ContactPickerDialog` searches contacts through `IContactServices`.
The dialog and service have no tenant or employee dependency. The calling module provides filters
and may annotate unavailable choices with a reason.

## Service

Contact services are registered by the engine API and WPF/MAUI clients. No extra module setup or schema
migration is required. `core.contact` requires authentication and does not enforce a manager claim.
Caller filters narrow results; they are not authorization boundaries. A consuming module must still
validate its own save operation, permissions and uniqueness.

Call `PostGetMeta_ContactSearchPage(ContactSearchQuery)` with an optional name/username fragment,
a one-based page and a `ContactSearchFilter`. Each response contains up to 50 rows and `HasMore`.
Ordering is by name then identity. Search, filters and exclusions apply on the server before paging.
Rows include contact identity, full name, type, lifecycle status and linked usernames.

| Filter | Meaning |
| --- | --- |
| `PeopleOnly` | Exclude organizations |
| `Type` | Optional exact contact type |
| `State` | Optional exact contact lifecycle state |
| `HasUser` | Require a linked user (`true`), require no linked user (`false`), or allow both (`null`) |
| `ActiveUsersOnly` | Restrict username search, display and presence filtering to active users |
| `ExcludedContactIds` | Omit identities supplied by the caller |

Defaults allow all types and states, with or without users, and include inactive usernames. Filters
combine with AND. An invalid page returns 400. Pages are separate requests, so concurrent changes can
shift results between requests.

## WPF usage

```csharp
var dialog = new ContactPickerDialog(app, new ContactSearchFilter {
   PeopleOnly = true,
   State = ContactState.Active,
   ExcludedContactIds = alreadySelectedIds
}) { Owner = owner };
if (dialog.ShowDialog() == true && dialog.Vm.SelectedContact is { } contact) {
   // Use contact.ContactId in the caller's operation.
}
```

The constructor starts the first search. Search or Enter applies the entered text and returns to page
one; Previous/Next retain the applied text and caller filters. The table displays Contact, Username and
Contact Status. Choose contact or double-click accepts a selection; Cancel leaves the caller unchanged.
The view model ignores responses arriving after the dialog closes.

For a rule that should remain visible, pass `selectionBlockReason: contact => ...`. Return null to
allow selection, or an English explanation to display and prevent selection. This callback runs on the
current page and does not filter the server results. Use `ContactSearchFilter` to hide unwanted rows.

Employee supplies active-person filters and excludes all contacts already employed in the selected
tenant, including inactive employees. It also rechecks its current employee list before accepting a
selection; the employee save operation enforces uniqueness if another client changes the data.
MAUI exposes the same service proxy but has no picker screen.
