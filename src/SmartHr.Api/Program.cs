using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartHr.Api;
using SmartHr.Application;
using SmartHr.Domain;
using SmartHr.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod())
);

var connectionString =
    builder.Configuration.GetConnectionString("SqlServer")
    ?? throw new InvalidOperationException("Missing SqlServer connection string.");
builder.Services.AddDbContext<RagDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5))
);

// RAG
builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddScoped<RagService>();
builder.Services.AddSingleton<VectorCache>();
builder.Services.AddSingleton<StructureChunker>();
builder.Services.AddSingleton<ITextExtractor, TextExtractor>();

var ollama = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
builder.Services.AddSingleton(ollama);
builder.Services.AddHttpClient<OllamaClient>(client =>
{
    client.BaseAddress = new Uri(ollama.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan; // per-attempt timeouts are enforced in OllamaClient
});
builder.Services.AddTransient<IEmbeddingService>(sp => sp.GetRequiredService<OllamaClient>());
builder.Services.AddTransient<IChatService>(sp => sp.GetRequiredService<OllamaClient>());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapEmployeeEndpoints();
app.MapPolicyEndpoints();
app.MapRagEndpoints();

await DbSeeder.InitializeAsync(app);

app.Run();

public partial class Program { }
