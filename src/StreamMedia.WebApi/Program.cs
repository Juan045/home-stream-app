using Microsoft.Extensions.Options;
using StreamMedia.Infrastructure.Database;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Infrastructure.Media;
using StreamMedia.Infrastructure.Ffmpeg;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers()
    .AddJsonOptions(
        options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy  = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        });

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


// FFMPEG
builder.Services
    .AddOptions<FfmpegOptions>()
    .Bind(builder.Configuration.GetSection(FfmpegOptions.SectionName))
    .Validate(o => o.MaxConcurrent > 0, "Ffmpeg:MaxConcurrent debe ser mayor que 0")
    .ValidateOnStart();

builder.Services.AddSingleton<FfprobeRunner>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<FfmpegOptions>>()
        .Value;

    return new FfprobeRunner(options);
});

builder.Services.AddSingleton<IFfmpegRunner>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<FfmpegOptions>>()
        .Value;

    return new FfmpegRunner(options);
});

// Media root: sin él ninguna ruta se puede resolver, así que se falla al arrancar.
var mediaRoot = builder.Configuration["Media:Root"];
if (string.IsNullOrWhiteSpace(mediaRoot) || !Directory.Exists(mediaRoot))
    throw new InvalidOperationException($"Media:Root no está configurado o no existe: '{mediaRoot}'");

builder.Services.AddSingleton(new MediaPathResolver(mediaRoot));
builder.Services.AddSingleton<SessionManager>();

builder.Services.AddScoped<IMediaAnalyzer, FfprobeMediaAnalyzer>();
builder.Services.AddScoped<IMediaRepository, SqliteMediaRepository>();

// Scoped para que cada request tenga su propia instancia de RegisterMediaUseCase y de FakeMediaAnalyzer
// No se tiene que compartir entre requests, porque cada request puede tener su propio estado de ejecución.
//builder.Services.AddScoped<IMediaAnalyzer, FakeMediaAnalyzer>(); //Temporal hasta tener la implementación real
builder.Services.AddScoped<RegisterMediaUseCase>();
builder.Services.AddScoped<GetMediaByIdUseCase>();
builder.Services.AddScoped<ListMediaUseCase>();
builder.Services.AddScoped<UpdateMediaUseCase>();
builder.Services.AddScoped<StartStreamUseCase>();
builder.Services.AddScoped<HeartbeatUseCase>();

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
