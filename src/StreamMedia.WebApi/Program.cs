using Microsoft.Extensions.Options;
using StreamMedia.Infrastructure.Database;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Infrastructure.Media;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Database
builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateOnStart();

// Singleton para que sobreviva entre requests mientras corre el proceso (se pierde al reiniciar)
builder.Services.AddSingleton<SqliteConnectionFactory>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<DatabaseOptions>>()
        .Value;

    return new SqliteConnectionFactory(options);
});

builder.Services.AddSingleton<DatabaseInitializer>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<DatabaseOptions>>()
        .Value;

    var connectionFactory = serviceProvider
        .GetRequiredService<SqliteConnectionFactory>();

    return new DatabaseInitializer(
        connectionFactory,
        options.SchemaFile);
});

builder.Services.AddScoped<IMediaRepository, SqliteMediaRepository>();

// Scoped para que cada request tenga su propia instancia de RegisterMediaUseCase y de FakeMediaAnalyzer
// No se tiene que compartir entre requests, porque cada request puede tener su propio estado de ejecución.
builder.Services.AddScoped<IMediaAnalyzer, FakeMediaAnalyzer>(); //Temporal hasta tener la implementación real
builder.Services.AddScoped<RegisterMediaUseCase>();

var app = builder.Build();

// Inicializar base de datos
var databaseInitializer =
    app.Services.GetRequiredService<DatabaseInitializer>();

await databaseInitializer.InitializeAsync();

// Configure the HTTP request pipeline.
/* if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapControllers();
} */
app.MapOpenApi();
app.MapControllers();

app.UseHttpsRedirection();

app.Run();
