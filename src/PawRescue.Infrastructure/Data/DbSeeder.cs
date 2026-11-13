using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PawRescue.Application.Interfaces;
using PawRescue.Domain.Entities;

namespace PawRescue.Infrastructure.Data;

public static class DbSeeder
{
    /// <summary>Applies migrations and creates the first administrator account.</summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // If the team has generated EF migrations, use them; otherwise create the schema directly
        // so the API runs on first launch. (Run `dotnet ef migrations add InitialCreate` before production.)
        if (db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        if (string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            logger.LogWarning("Seed:AdminPassword is empty — admin account not created.");
        }
        else
        {
            var email = seed.AdminEmail.Trim().ToLowerInvariant();
            if (!await db.Users.AnyAsync(u => u.Email == email))
            {
                db.Users.Add(new User("Paw Rescue Admin", email, hasher.Hash(seed.AdminPassword), RoleIds.Administrator));
                await db.SaveChangesAsync();
                logger.LogInformation("Seeded administrator {Email}", email);
            }
        }

        // Demo reports around the Philippines (only when the database has no reports yet)
        if (seed.SampleData)
        {
            var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
            var storage = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            await SampleDataSeeder.SeedAsync(db, hasher, webRoot, storage.PublicBaseUrl, logger);
        }
    }
}
