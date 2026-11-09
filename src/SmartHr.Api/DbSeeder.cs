using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartHr.Application;
using SmartHr.Domain;
using SmartHr.Infrastructure;

namespace SmartHr.Api;

/// <summary>Applies migrations and seeds demo employees, sample policies, and evaluation cases.</summary>
public static class DbSeeder
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");
        var config = services.GetRequiredService<IConfiguration>();
        var db = services.GetRequiredService<RagDbContext>();

        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrated.");
        await SeedEmployeesAsync(db);
        await Reembedder.ReembedAsync(services, force: false, CancellationToken.None);

        if (!config.GetValue("Seed:Enabled", true))
        {
            return;
        }

        var samples = FindSamplesDirectory(app.Environment.ContentRootPath, config["Seed:SamplesPath"]);
        if (samples is null)
        {
            logger.LogWarning("Samples folder not found; skipping policy seeding.");
            return;
        }

        await SeedEvaluationCasesAsync(db, samples, logger);

        if (await db.Documents.AnyAsync())
        {
            return;
        }

        var rag = services.GetRequiredService<RagService>();
        foreach (var file in Directory.GetFiles(samples, "*.md").OrderBy(f => f))
        {
            try
            {
                var text = await File.ReadAllTextAsync(file);
                var result = await rag.IngestAsync(TitleOf(text, file), Path.GetFileName(file), text, CancellationToken.None);
                logger.LogInformation("Seeded {File}: {Chunks} chunks", Path.GetFileName(file), result.Chunks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed {File} (is Ollama running?)", file);
            }
        }
    }

    public static string TitleOf(string text, string file)
    {
        var match = Regex.Match(text, @"^#\s+(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(file);
    }

    private static async Task SeedEmployeesAsync(RagDbContext db)
    {
        if (await db.Employees.AnyAsync())
        {
            return;
        }

        db.Employees.AddRange(
            new Employee { EmployeeNumber = "E1001", FullName = "Ahmed Hassan / أحمد حسن", Email = "ahmed@example.com", Department = "HR", JobTitle = "HR Manager", HireDate = new DateTime(2016, 3, 1) },
            new Employee { EmployeeNumber = "E1002", FullName = "Sara Ali / سارة علي", Email = "sara@example.com", Department = "Finance", JobTitle = "Accountant", HireDate = new DateTime(2021, 9, 15) },
            new Employee { EmployeeNumber = "E1003", FullName = "Omar Khaled / عمر خالد", Email = "omar@example.com", Department = "IT", JobTitle = "Software Engineer", HireDate = new DateTime(2023, 1, 10) },
            new Employee { EmployeeNumber = "E1004", FullName = "Mona Youssef / منى يوسف", Email = "mona@example.com", Department = "Sales", JobTitle = "Sales Specialist", HireDate = new DateTime(2025, 6, 1) },
            new Employee { EmployeeNumber = "E1005", FullName = "Khaled Nasser / خالد ناصر", Email = "khaled@example.com", Department = "Operations", JobTitle = "Supervisor", HireDate = new DateTime(2012, 11, 20) }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedEvaluationCasesAsync(RagDbContext db, string samples, ILogger logger)
    {
        var path = Path.Combine(samples, "evaluation.json");
        if (!File.Exists(path) || await db.RetrievalCases.AnyAsync())
        {
            return;
        }

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        foreach (var item in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            db.RetrievalCases.Add(
                new RetrievalCase
                {
                    Question = item.GetProperty("question").GetString() ?? "",
                    ExpectedDocument = item.TryGetProperty("expectedDocument", out var d) ? d.GetString() ?? "" : "",
                    ExpectedSection = item.TryGetProperty("expectedSection", out var s) ? s.GetString() : null,
                }
            );
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded evaluation cases.");
    }

    private static string? FindSamplesDirectory(string contentRoot, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured, contentRoot);
            return Directory.Exists(full) ? full : null;
        }

        for (var dir = new DirectoryInfo(contentRoot); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
