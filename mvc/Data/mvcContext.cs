using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using mvc.Models;

namespace mvc.Data
{
    public class mvcContext : IdentityDbContext<IdentityUser>
    //DbContext was the original class, but we changed it to IdentityDbContext to support authentication and authorization features in ASP.NET Core Identity.
    {
        public mvcContext(DbContextOptions<mvcContext> options) : base(options)
        {
        }

        // The book catalog. Mapped to the lowercase "books" table so the SQLite file
        // stays compatible with the original schema (id / title / author / price).
        public DbSet<BookViewModel> Books { get; set; } = default!;
        public DbSet<AuditLog> AuditLogs { get; set; } = default!;
        public DbSet<PageView> PageViews { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<BookViewModel>(entity =>
            {
                entity.ToTable("books");
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Id).HasColumnName("id");
                entity.Property(b => b.Title).HasColumnName("title").HasMaxLength(50).IsRequired();
                entity.Property(b => b.Author).HasColumnName("author").HasMaxLength(50).IsRequired();
                entity.Property(b => b.Price).HasColumnName("price");

                // Holds the search term posted by the Book views; never persisted.
                entity.Ignore(b => b.searchString);
            });
        }
    }
}
