using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mvc.Data;
using mvc.Models;
using mvc.Services;

var builder = WebApplication.CreateBuilder(args);

//1. Database connection string configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

//2. Add DbContext with the SQLite provider.
//   SQLite is a single-file embedded database, so there is no server to reach and
//   no EnableRetryOnFailure execution strategy (that is SQL Server specific).
//   Concurrency is handled by WAL mode + the busy timeout in the connection string.
builder.Services.AddDbContext<mvcContext>(options =>
    options.UseSqlite(connectionString));

// Repository registration
builder.Services.AddScoped<IBookRepository, BookRepository>();

// Email settings and sender
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddTransient<IEmailSender<IdentityUser>, EmailSender>();

// Audit log retention (Audit:RetentionDays, 0 = disabled)
builder.Services.AddHostedService<mvc.Services.AuditRetentionService>();

// Background writer that persists queued audit log / page view rows off the request path.
builder.Services.AddSingleton<mvc.Services.AuditWriteQueue>();
builder.Services.AddHostedService<mvc.Services.AuditWriteService>();

// Analytics / KPI aggregation
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

// In-memory cache for book list lookups.
builder.Services.AddMemoryCache();

//3. Add Identity services
builder.Services.AddDefaultIdentity<IdentityUser>(options => { options.Password.RequireDigit = true; options.Password.RequiredLength = 6; options.Password.RequireNonAlphanumeric = false; options.Password.RequireUppercase = true; })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<mvcContext>()
    .AddDefaultTokenProviders();

// Authorization policies: everything requires login unless [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("MemberOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(SeedData.AdminRole, SeedData.MemberRole));
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(SeedData.AdminRole));
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login"; // Set the login path for authentication
    options.LogoutPath = "/Account/Logout"; // Set the logout path
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Set session timeout
    options.Cookie.HttpOnly = true; // Set cookie to HttpOnly for security
    options.Cookie.IsEssential = true; // Make the session cookie essential
});

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<mvc.Filters.AuditActionFilter>();
    options.Filters.Add<mvc.Filters.PageViewFilter>();
});

// Enable gzip + brotli response compression for text-based content.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "image/svg+xml",
        "application/json",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);

var app = builder.Build();

// Create / migrate the SQLite file, then seed roles and the default admin account.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DatabaseInitializer.InitializeAsync(scope.ServiceProvider);
        await SeedData.InitializeAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex, "Startup database initialization failed. The site will still start.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Long-lived caching for static assets. Versioned/hashed files (e.g.
        // asp-append-version query strings or *.min.* bundles) are safe to cache.
        ctx.Context.Response.Headers.CacheControl = "public,max-age=604800,immutable";
    }
});
app.UseRouting();
app.UseSession(); // Enable session middleware
app.UseAuthentication(); // Enable authentication middleware
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();