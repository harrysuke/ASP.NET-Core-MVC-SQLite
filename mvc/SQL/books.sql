-- ---------------------------------------------------------------------------
-- booksDb.sqlite — book catalog schema (SQLite)
--
-- The application creates this table itself through EF Core migrations
-- (Migrations/20261026004959_InitialSqlite.cs). This script is the standalone
-- equivalent, useful for inspecting or rebuilding the catalog by hand:
--
--   sqlite3 booksDb.sqlite < mvc/SQL/books.sql
--
-- SQLite has no stored procedures, so the old T-SQL procs (BookViewAll,
-- BookViewById, BookAdd, BookEdit, BookAddOrEdit, BookDeleteById,
-- BookSearchByTitle) are gone; the same operations are plain queries issued by
-- mvc/Data/BookRepository.cs.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS "books" (
    "id"     INTEGER NOT NULL CONSTRAINT "PK_books" PRIMARY KEY AUTOINCREMENT,
    "title"  TEXT    NOT NULL,
    "author" TEXT    NOT NULL,
    "price"  INTEGER NOT NULL
);

-- Demo rows, inserted only when the catalog is still empty.
INSERT INTO "books" ("title", "author", "price")
SELECT 'The Pragmatic Programmer', 'Andrew Hunt', 42
WHERE NOT EXISTS (SELECT 1 FROM "books");

INSERT INTO "books" ("title", "author", "price")
SELECT 'Clean Code', 'Robert C. Martin', 38
WHERE (SELECT COUNT(*) FROM "books") = 1;

INSERT INTO "books" ("title", "author", "price")
SELECT 'Design Patterns', 'Erich Gamma', 55
WHERE (SELECT COUNT(*) FROM "books") = 2;

INSERT INTO "books" ("title", "author", "price")
SELECT 'Refactoring', 'Martin Fowler', 47
WHERE (SELECT COUNT(*) FROM "books") = 3;

INSERT INTO "books" ("title", "author", "price")
SELECT 'Domain-Driven Design', 'Eric Evans', 52
WHERE (SELECT COUNT(*) FROM "books") = 4;

INSERT INTO "books" ("title", "author", "price")
SELECT 'The Mythical Man-Month', 'Frederick Brooks', 29
WHERE (SELECT COUNT(*) FROM "books") = 5;
