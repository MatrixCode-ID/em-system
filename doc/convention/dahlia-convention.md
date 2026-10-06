# Dahlia Convention

**Naming standard for database objects and C# models**

The Dahlia Convention is the naming standard for schemas built on the em-system engine and its derived
repositories (such as EmPorium House). It has two related parts:

1. [Database objects](#part-1--database-objects): tables, columns, views, functions and stored procedures.
2. [C# models](#part-2--c-models): ORM classes that map tables and views, and the classes derived from them
   in the API and UI.

New objects follow this standard, and derived repositories do not define their own rules. The engine's SQL
Server scripts are in `doc/sqlscript/mssql/`.

This document covers **naming only**. Existing databases and code may contain objects with different names
or layouts; those objects remain valid and do not have to be renamed. Constraint names (primary key,
unique, foreign key) and collations are out of scope.

## Purpose

The main purpose of these prefixes and patterns is **filtering IntelliSense in the IDE**. A few leading
characters are enough to narrow the autocomplete list to the kind of object or the table you want, both in
SQL (SSMS, Azure Data Studio, DataGrip/Rider) and in C#.

- **Object prefixes filter the list.** Typing `ta_` shows only tables, `vi_` only views, `sp_`/`fs_`/`ft_`
  only procedures and functions. A table and a view with the same name (`ta_Product`, `vi_Product`) appear
  next to each other and are easy to tell apart.
- **The table name in column names filters per table.** Typing `cProduct` shows only the columns of
  `ta_Product`, not `Id`, `Name` or `Note` from hundreds of other tables.
- **Child tables group with their parent.** Because a child inherits its parent's name, `ta_Product`,
  `ta_ProductTran` and `ta_ProductTranData` appear together, and so do their columns (`cProduct…`,
  `cProductTran…`).
- **Procedures and functions group per table.** The pattern `<prefix><Table>_<Task>` lists all
  `sp_Product_…` together, and the task after `_` explains what it does without opening the definition.
- **View columns are grouped by origin.** In a view, `c` is a table column, `cc` a computed value and `cv`
  a view-only column, so autocomplete shows which columns can be written back to the table.
- **It carries on into C#.** ORM classes use exactly the same table/view and column names. In derived
  classes, members starting with `c` always come from the database, while members without the prefix are
  application logic. See [Part 2](#part-2--c-models).

Additional benefits of names that are unique across the whole database:

- **Unambiguous joins.** Uniquely named columns never cause *ambiguous column name* errors and do not
  always need a table alias.
- **Easy to search.** A text search (`git grep cProductPrice`) finds every use of a column in SQL, C# and
  XAML, with no false hits from other tables.
- **The name tells where the data comes from.** `cProductTranEmpRef_cEmpId` names the source table, the
  meaning of the column and the referenced column.

---

## Part 1 — Database objects

### Prefix summary

| Object | Prefix | Example |
| --- | --- | --- |
| Table | `ta_` | `ta_Product` |
| View | `vi_` | `vi_Product` |
| Scalar function | `fs_` | `fs_Customer_GetAllInRegion` |
| Table-valued function | `ft_` | `ft_Product_ListActive` |
| Stored procedure | `sp_` | `sp_Product_Recalculate` |
| Column | `c` + table name | `cProductId` |
| Computed view column (optional) | `cc` + table name | `ccProductTotal` |
| View-only column (optional) | `cv` + table name | `cvProductColor` |

Object names contain no spaces and are PascalCase after the prefix.

### Tables

- Table names start with `ta_`, for example `ta_Product`.
- **Child tables inherit their parent's name.** `ta_ProductTran` is a child of `ta_Product`, and
  `ta_ProductTranData` is a child of `ta_ProductTran`.

### Columns

A column name is `c`, followed by the table name without `ta_`, then the column name. Example: `cProductId`
is the `Id` column of `ta_Product`.

#### Standard columns

When a table needs one of these columns, use this name, type and order. Columns that do not apply to the
table may be left out.

| Column | Type | Default | Meaning |
| --- | --- | --- | --- |
| `c<Table>Id` | `char(26)` | | Primary key holding a [ULID](https://github.com/ulid/spec) |
| `c<Table>Name` | `varchar(255)` | | Name of the record; empty when the record has no name |
| `c<Table>RefNumber` | `datetime` | | Basis of the number shown to users; the application formats it |
| `c<Table>Stage` or `c<Table>State` | `int` | | Document stage or record state; see [Stage and State](#stage-and-state) |
| `c<Table>Order` | `int` | `-1` | Sort order, set by the application when needed |
| `c<Table>Revision` | `int` | `1` | Revision number, set by the application when needed |
| `c<Table>Date` | `date` | | Date of the record; may be entered by users |
| `c<Table>Note` | `varchar(500)` | `NULL` | Note |
| `ustamp` | `datetime` | | Last update time (update stamp) |
| `datestamp` | `datetime` | | Creation time (create stamp) |
| `json_object` | `nvarchar(max)` | `NULL` | Additional data as JSON |

`ustamp`, `datestamp` and `json_object` are written as is, without the `c<Table>` prefix.

#### Stage and State

Pick one, depending on the kind of data:

- **Stage** for **document** tables (business objects that go through a workflow, such as orders, invoices
  or requests). Stage is the document's step in its workflow.
- **State** for **non-document** tables (such as users, robots or master data). Data like users do not go
  through draft, submit or approve steps, so they only need a state (for example active or inactive).
  State values are defined by the application per table.

Stage values:

| Value | Meaning |
| --- | --- |
| `< -1` | Other negative states, for example a deleted document |
| `-1` | Void |
| `0` | Draft |
| `> 0` | Can be processed; the default is `1` = published. Applications may use their own sequence, for example `1` submit, `2` approve, `3` close |

Stage values are governed by application business logic, so there is no master table for them.

#### Additional columns

Columns needed later, after a table is in use, are not added as new columns. Their data is serialized to
JSON and stored in `json_object`.

### Foreign keys

- A foreign key column uses the name of the parent's key column. Example: a column referencing `cEmpId` in
  another table is also named `cEmpId`.
- When a foreign key column has its own (non-standard) name, append the parent column after `_`:
  `c<Table><Name>_<parent column>`. Example: `cProductTranEmpRef_cEmpId` is the `EmpRef` column of
  `ta_ProductTran`, referencing `cEmpId`.

### Views

- View names start with `vi_` and **match their table's name**: table `ta_Product` has view `vi_Product`.
- Every table used by the UI must have a `vi_` view.
- Views must not contain heavy calculations; use `sp_` or `fs_` for those.
- A view should have a main table. Keep views that are not based on a table to a minimum.
- `json_object` content may be expanded into view columns.

#### `cc` and `cv` columns

Optional, used only when needed. Both mark view columns that do not come directly from a table column, so
readers know they cannot be written back to the `ta_` table. Columns taken from the table as is keep their
original `c` name.

| Prefix | Name | Meaning | Example |
| --- | --- | --- | --- |
| `cc` | Computed column | The result of a calculation or expression over other columns | `ccProductTotal` = `cProductQty * cProductPrice` |
| `cv` | Column view | A column that exists only in the view, for example a value expanded from `json_object` or a renamed column | `cvProductColor` from `json_object` |

They follow the normal column pattern: prefix, table name without `ta_`, then the column name.

```sql
CREATE VIEW [dbo].[vi_Product] AS
SELECT p.*,
       p.[cProductQty] * p.[cProductPrice]          AS [ccProductTotal],
       JSON_VALUE(p.[json_object], '$.color')       AS [cvProductColor]
FROM [dbo].[ta_Product] p;
```

### Functions and stored procedures

- Scalar functions start with `fs_`, table-valued functions with `ft_`, stored procedures with `sp_`.
- Try to tie each function or procedure to one table.
- The name contains the table name, then the task after `_`: `<prefix><Table>_<Task>`. Example:
  `fs_Customer_GetAllInRegion`.
- Each view, function, procedure and trigger lives in its own file named after the object (for example
  `doc/sqlscript/mssql/views/vi_Product.sql`).

### Table example

```sql
CREATE TABLE [dbo].[ta_Product]
(
   [cProductId]        char(26)      NOT NULL,
   [cProductName]      varchar(255)  NULL,
   [cProductRefNumber] datetime      NULL,
   [cProductStage]     int           NOT NULL,
   [cProductOrder]     int           NOT NULL DEFAULT -1,
   [cProductRevision]  int           NOT NULL DEFAULT 1,
   [cProductDate]      date          NULL,
   [cProductNote]      varchar(500)  NULL,
   [cProductQty]       int           NOT NULL,
   [cProductPrice]     decimal(18,2) NOT NULL,
   [ustamp]            datetime      NOT NULL,
   [datestamp]         datetime      NOT NULL,
   [json_object]       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_Product] PRIMARY KEY CLUSTERED ([cProductId])
);

CREATE TABLE [dbo].[ta_ProductTran]
(
   [cProductTranId]            char(26) NOT NULL,
   [cProductId]                char(26) NOT NULL,  -- standard FK: parent column name
   [cProductTranEmpRef_cEmpId] char(26) NULL,      -- non-standard FK: own name + parent column
   -- ... other standard columns ...
);
```

---

## Part 2 — C# models

C# code is split into two layers with different naming rules:

| Layer | Class name | Contents | Engine example |
| --- | --- | --- | --- |
| **ORM model class** | Same as the table/view: `ta_<Table>`, `vi_<Table>` | Column properties only | `ta_User`, `vi_User` |
| **Derived class** | No prefix: `<Table>` or a free name | Column mirror properties plus application properties and methods | `User` |

This split keeps IntelliSense organized: everything starting with `ta_`, `vi_` or `c` comes from the
database, and everything else is application logic.

### ORM model classes

An ORM model class mirrors one table or view. It is used by EF Core and Dapper, and as the DTO between API
and UI.

1. **The class name matches the table/view name exactly,** including its prefix: `ta_User` for table
   `ta_User`, `vi_User` for view `vi_User`. Apply `[Table("ta_User")]`, and `[Key]` on the primary key.
2. **Property names match column names exactly,** including the `c`/`cc`/`cv` prefixes and the standard
   columns `ustamp`, `datestamp` and `json_object`.
3. **No additional members.** Only column properties: no methods, computed properties, `[NotMapped]` or
   other logic. Everything else goes into a derived class.
4. **A view model inherits its table model.** `vi_User : ta_User` adds only the columns that exist in the
   view but not in the table (joined columns, `cc`, `cv`).
5. **`DbSet` properties use the plural class name:** `DbSet<ta_User> ta_Users`, `DbSet<vi_Contact> vi_Contacts`.

Location: `Em.Libs` (namespace `Em.Api.Core.Models`) for engine tables, and the module's `*.Models` project
for module tables (for example `Em.Test.Models`).

### Derived classes

Derived classes carry ORM model data into other layers (UI models, services, reports) and may contain
logic.

1. **Any derivation style.** A derived class may inherit the ORM model class directly or wrap it. Engine UI
   models wrap it through `UiModel<vi_X, TService>`, for example `User : UiModel<vi_User, ICredentialServices>`.
2. **No `ta_`/`vi_` prefix in the class name.** `User`, not `vi_User`. Use a free name when one table has
   several derived classes with different roles.
3. **Other properties and methods are allowed,** such as validation, save commands, data from other
   services or UI state flags.
4. **Members owned by the derived class have no `c` prefix** and use plain PascalCase: `AvailableClaims`,
   `GetClaims()`, `IsDirty`. Typing `c` in IntelliSense then shows only members that come from database
   columns.
5. **Properties that mirror a column keep the column name** (`cUserId`, `cContactFullName`), so the mapping
   to and from the ORM model class can be read line by line. Columns that must not be changed from this
   class, such as joined columns, `cc` and `cv`, get a `private set`.
6. **Conversion through explicit members.** Engine UI models use `static Build(app, vi_X)` to create a model
   from a view row, and `ReadFrom(vi_X)` and `WriteTo(vi_X)` to map columns in both directions.

Location of UI derived classes: `Em.Ui.Core` for the engine, and the module's `*.Models.Ui` project for
modules (for example `Em.Test.Models.Ui`).

### Model example

```csharp
// ORM model class: columns only, same name as the table.
[Table("ta_Product")]
public class ta_Product {
   [Key]
   public string cProductId { get; set; } = string.Empty;
   public string? cProductName { get; set; }
   public int cProductStage { get; set; }
   public int cProductQty { get; set; }
   public decimal cProductPrice { get; set; }
   public DateTime ustamp { get; set; }
   public DateTime datestamp { get; set; }
   public string? json_object { get; set; }
}

// ORM model class for the view: inherits the table, adds view columns only.
[Table("vi_Product")]
public class vi_Product : ta_Product {
   public decimal ccProductTotal { get; set; }
   public string? cvProductColor { get; set; }
}

// Derived UI class: no ta_/vi_ prefix.
public class Product : UiModel<vi_Product, IProductServices> {
   public static Product Build(IEmApp app, vi_Product data) => new(app, data);
   private Product(IEmApp app, vi_Product data) : base(app, data) { }

   // Column mirrors: the column name is kept.
   public int cProductQty { get; set => SetField(ref field, value); }
   public decimal ccProductTotal { get; private set => SetField(ref field, value); }

   // Owned by the derived class: no c prefix.
   public bool IsLowStock => cProductQty < 10;
   public Task SubmitAsync() => /* ... */;

   protected override void ReadFrom(vi_Product source) { /* source.cX -> cX */ }
   protected override void WriteTo(vi_Product target) { /* cX -> target.cX */ }
}
```
