using Microsoft.EntityFrameworkCore;
using mvc.Models;

namespace mvc.Data
{
    /// <summary>
    /// Creates / upgrades the SQLite database file at startup.
    /// SQLite has no server and no DBA, so the app is responsible for applying its
    /// own migrations and for tuning the few connection-level pragmas it relies on.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync(IServiceProvider services)
        {
            mvcContext context = services.GetRequiredService<mvcContext>();
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

            // Creates booksDb.sqlite on first run, then applies any pending migrations.
            await context.Database.MigrateAsync();
            logger.LogInformation("SQLite database ready at '{DataSource}'.", context.Database.GetDbConnection().DataSource);

            // WAL lets readers run while the audit/page-view writer is committing, which
            // matters because AuditWriteService writes off the request path. The busy
            // timeout in the connection string covers the remaining lock contention.
            try
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
            }
            catch (Exception ex)
            {
                // Not fatal: the database still works in the default rollback-journal mode.
                logger.LogWarning(ex, "Could not switch the SQLite database to WAL mode.");
            }

            await SeedBooksAsync(context, logger);
        }

        private static async Task SeedBooksAsync(mvcContext context, ILogger logger)
        {
            if (await context.Books.AnyAsync())
            {
                return;
            }

            context.Books.AddRange(
                new BookViewModel { Title = "The Pragmatic Programmer", Author = "Andrew Hunt", Price = 42 },
                new BookViewModel { Title = "Clean Code", Author = "Robert C. Martin", Price = 38 },
                new BookViewModel { Title = "Design Patterns", Author = "Erich Gamma", Price = 55 },
                new BookViewModel { Title = "Refactoring", Author = "Martin Fowler", Price = 47 },
                new BookViewModel { Title = "Domain-Driven Design", Author = "Eric Evans", Price = 52 },
                new BookViewModel { Title = "The Mythical Man-Month", Author = "Frederick Brooks", Price = 29 });

            await context.SaveChangesAsync();
            logger.LogInformation("Seeded the empty books table with demo rows.");
        }
    }
}
