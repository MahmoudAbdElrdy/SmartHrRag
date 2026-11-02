using Microsoft.EntityFrameworkCore;
using SmartHr.Api;
using SmartHr.Application;
using SmartHr.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()
    );
});

var connectionString =
    builder.Configuration.GetConnectionString("SqlServer")
    ?? throw new InvalidOperationException("Missing SqlServer connection string.");

builder.Services.AddDbContext<RagDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddScoped<RagService>();
builder.Services.AddSingleton<StructureChunker>();
builder.Services.AddSingleton<ITextExtractor, TextExtractor>();

var ollama = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();

builder.Services.AddSingleton(ollama);
builder.Services.AddHttpClient<OllamaClient>(client =>
{
    client.BaseAddress = new Uri(ollama.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddTransient<IEmbeddingService>(services =>
    services.GetRequiredService<OllamaClient>()
);
builder.Services.AddTransient<IChatService>(services =>
    services.GetRequiredService<OllamaClient>()
);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPolicyEndpoints();
app.MapRagEndpoints();

app.Run();

public partial class Program { }
