using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace mvc.Data
{
    public class SeedData
    {
        public const string AdminRole = "Admin";
        public const string MemberRole = "Member";

        public static async Task InitializeAsync(IServiceProvider services)
        {
            RoleManager<IdentityRole> roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            UserManager<IdentityUser> userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            IConfiguration configuration = services.GetRequiredService<IConfiguration>();
            ILogger<SeedData> logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<SeedData>();

            var roles = new[] { AdminRole, MemberRole };

            foreach (string role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(role));

                    if (result.Succeeded)
                    {
                        logger.LogInformation("Created the {Role} role.", role);
                    }
                    else
                    {
                        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                        logger.LogError("Failed to create the {Role} role. Errors: {Errors}", role, errors);
                    }
                }
            }

            string? adminEmail = configuration["Admin:Email"];
            string? adminPassword = configuration["Admin:Password"];
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogWarning("Admin:Email / Admin:Password not configured. No default admin created.");
                return;
            }

            IdentityUser? adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                IdentityResult result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, AdminRole);
                    logger.LogInformation("Created default admin account for {Email}.", adminEmail);
                }
                else
                {
                    logger.LogError("Failed to create default admin account: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}