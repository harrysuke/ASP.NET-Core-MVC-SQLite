# 03 — Database

## Engine & file

- Engine: **SQLite** (embedded, single file — no server, no service, no login).
- File: `booksDb.sqlite`, created in the app's working directory (the project folder when
  you run `dotnet run --project mvc`, the publish folder under IIS).
- Connection string (`appsettings.json`):

  ```
  Data Source=booksDb.sqlite;Default Timeout=30
  ```

  `Default Timeout` is the busy timeout in seconds: it makes a writer wait for a lock
  instead of failing immediately with "database is locked".
- The file, plus its `-wal` / `-shm` side files, is ignored by `.gitignore`.

## Connection-level pragmas

`Data/DatabaseInitializer` applies these once per process at startup:

| Pragma | Why |
| --- | --- |
| `journal_mode=WAL` | lets reads run while `AuditWriteService` commits audit / page-view rows |
| `synchronous=NORMAL` | safe with WAL and much cheaper than `FULL` for every write |

## Tables

| Table | Purpose | Managed by |
| --- | --- | --- |
| `books` (`id`, `title`, `author`, `price`) | Book catalog | `BookRepository` (EF Core) |
| `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` | ASP.NET Identity tables | EF Core migrations |
| `AuditLogs` | Audit trail rows (see `Models/AuditLog`) | `AuditActionFilter` + `AuditRetentionService` |
| `PageViews` | Page view hits (see `Models/PageView`) | `PageViewFilter` |

`BookViewModel` is mapped in `mvcContext.OnModelCreating` to the lowercase `books` table so
the SQLite schema stays compatible with the original catalog layout. The `searchString`
property on the model is a UI-only field and is `Ignore`d.

## Migrations (EF Core, code-first)

| Migration | Adds |
| --- | --- |
| `20261026004959_InitialSqlite` | All tables: Identity, `books`, `AuditLogs`, `PageViews` |

Current model is captured in `mvcContextModelSnapshot.cs`. The migrations were regenerated
for SQLite — the old SQL Server ones (`nvarchar`/`IDENTITY`/`datetime2`) cannot be applied
to a SQLite file.

Migrations run automatically at startup (`DatabaseInitializer`), so a fresh clone only
needs `dotnet run`. To do it by hand:

```bash
cd mvc
dotnet tool restore                 # restores dotnet-ef 10.0.9 from .config/dotnet-tools.json
dotnet ef database update           # creates booksDb.sqlite and applies migrations
dotnet ef migrations add <Name>     # after changing the model
```

## Data access

SQLite has no stored procedures, so the original `BookViewAll` / `BookViewById` /
`BookAdd` / `BookEdit` / `BookAddOrEdit` / `BookDeleteById` / `BookSearchByTitle` procs are
gone. `BookRepository` keeps the same interface and issues plain EF Core queries:

| Repository method | Emitted SQL shape |
| --- | --- |
| `GetAll()` | `SELECT … FROM books ORDER BY id` (cached 5 min in `IMemoryCache`) |
| `GetById(id)` | `SELECT … FROM books WHERE id = @id LIMIT 1` |
| `Add(book)` | `INSERT INTO books (title, author, price) VALUES (…) RETURNING id` |
| `Update(id, book)` | `UPDATE books SET title=…, author=…, price=… WHERE id=@id` |
| `Delete(id)` | `DELETE FROM books WHERE id=@id` |
| `Search(term)` | `WHERE instr(lower(title), @t) > 0 OR instr(lower(author), @t) > 0` |

`Search` uses `lower()` + `instr()` rather than `LIKE` so the match is case-insensitive for
non-ASCII text and `%` / `_` in the search term are treated as literal characters.

## Additional files

- `SQL/books.sql` — standalone SQLite DDL for `books` plus the six demo rows. The app
  creates the same table through migrations; the script is a reference / manual-rebuild
  helper:

  ```bash
  sqlite3 booksDb.sqlite < mvc/SQL/books.sql
  ```

## Seeding at startup (`Data/DatabaseInitializer.cs` + `Data/SeedData.cs`)

Runs every startup inside `Program.cs`:

- `DatabaseInitializer`: applies migrations, sets WAL, then inserts six demo books **only
  when `books` is empty**.
- `SeedData`: creates roles if missing — **Admin**, **Member** (`SeedData.AdminRole`,
  `SeedData.MemberRole`).
- If `Admin:Email` + `Admin:Password` are configured (see `appsettings.json` /
  IIS env vars), creates that user, sets `EmailConfirmed = true`, assigns `Admin`.
- Failure paths are logged and non-fatal: if the file is locked or unreadable the site
  still starts.
- Existing users are never re-created; upgrades to role assignment are skipped if the
  user already exists.

## Backups

A SQLite database is a single file, so a backup is a file copy. Use the SQLite backup API
or copy the file while the app is stopped (with WAL, also copy `-wal` and `-shm`, or run
`VACUUM INTO 'backup.sqlite'` from `sqlite3` to get a consistent snapshot).
