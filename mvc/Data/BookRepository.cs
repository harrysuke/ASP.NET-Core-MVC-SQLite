using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using mvc.Models;

namespace mvc.Data
{
    public interface IBookRepository
    {
        List<BookViewModel> GetAll();
        BookViewModel? GetById(int? id);
        void Add(BookViewModel book);
        void Update(int id, BookViewModel book);
        void Delete(int id);
        List<BookViewModel> Search(string searchString);
    }

    // SQLite has no stored procedures, so the book catalog is accessed through
    // EF Core against the mapped "books" table. The public surface is unchanged,
    // so controllers do not care which provider sits underneath.
    public class BookRepository : IBookRepository
    {
        private const string GetAllCacheKey = "books:all";

        // Cached so repeated reads (home page, list, dashboard) don't hit SQLite each time.
        private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5)
        };

        private readonly mvcContext _context;
        private readonly IMemoryCache _cache;

        public BookRepository(mvcContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public List<BookViewModel> GetAll()
        {
            if (_cache.TryGetValue(GetAllCacheKey, out List<BookViewModel>? cached) && cached != null)
            {
                return cached;
            }

            List<BookViewModel> books = _context.Books
                .AsNoTracking()
                .OrderBy(b => b.Id)
                .ToList();

            _cache.Set(GetAllCacheKey, books, CacheOptions);
            return books;
        }

        public BookViewModel? GetById(int? id)
        {
            if (id == null)
            {
                return null;
            }

            BookViewModel? cached = GetAll().FirstOrDefault(b => b.Id == id);
            if (cached != null)
            {
                return cached;
            }

            return _context.Books.AsNoTracking().FirstOrDefault(b => b.Id == id);
        }

        public void Add(BookViewModel book)
        {
            _context.Books.Add(new BookViewModel
            {
                Title = book.Title,
                Author = book.Author,
                Price = book.Price
            });
            _context.SaveChanges();
            _cache.Remove(GetAllCacheKey);
        }

        public void Update(int id, BookViewModel book)
        {
            BookViewModel? existing = _context.Books.FirstOrDefault(b => b.Id == id);
            if (existing == null)
            {
                return;
            }

            existing.Title = book.Title;
            existing.Author = book.Author;
            existing.Price = book.Price;
            _context.SaveChanges();
            _cache.Remove(GetAllCacheKey);
        }

        public void Delete(int id)
        {
            BookViewModel? existing = _context.Books.FirstOrDefault(b => b.Id == id);
            if (existing == null)
            {
                return;
            }

            _context.Books.Remove(existing);
            _context.SaveChanges();
            _cache.Remove(GetAllCacheKey);
        }

        public List<BookViewModel> Search(string searchString)
        {
            if (string.IsNullOrWhiteSpace(searchString))
            {
                return GetAll();
            }

            // lower() + instr() keeps the match case-insensitive (SQLite's LIKE is only
            // case-insensitive for ASCII) and treats % / _ in the term as plain text,
            // which is what the old LIKE-based stored procedure effectively did.
            string term = searchString.Trim().ToLower();
            return _context.Books
                .AsNoTracking()
                .Where(b => b.Title.ToLower().Contains(term)
                    || b.Author.ToLower().Contains(term))
                .OrderBy(b => b.Id)
                .ToList();
        }
    }
}
