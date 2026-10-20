using Microsoft.Extensions.Options;
using StreamMedia.Infrastructure.Database;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Infrastructure.Media;
using StreamMedia.Infrastructure.Ffmpeg;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Infrastructure.Cache;
using StreamMedia.Domain.Entities;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;


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

// Excepciones
builder.Services.AddExceptionHandler<StreamMedia.WebApi.Errors.ApiExceptionHandler>();
builder.Services.AddProblemDetails();

// FFMPEG
var ffmpeg = builder.Configuration.GetSection(FfmpegOptions.SectionName).Get<FfmpegOptions>() ?? new();

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

// Cache
var cache = builder.Configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new();

builder.Services
    .AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Dir), "Cache:Dir no puede estar vacio")
    .Validate(o => o.MaxBytes > 0, "Cache:MaxBytes debe ser mayor que 0")   // nuevo
    .ValidateOnStart();

// Singleton: no guarda estado en memoria, el estado es el disco.
builder.Services.AddSingleton<IAssetStore>(serviceProvider => new AssetStore(
    serviceProvider.GetRequiredService<IOptions<CacheOptions>>().Value,
    serviceProvider.GetRequiredService<TimeProvider>(),
    serviceProvider.GetRequiredService<ILogger<AssetStore>>()));

builder.Services.AddSingleton(new CapacityLimits(cache.MaxBytes, ffmpeg.MaxConcurrent));
builder.Services.AddSingleton<CapacityGuard>();

builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<AssetBuilder>(
    sp, ffmpeg.MaxConcurrent));

// Transcode: con que se codifica un asset nuevo. Sin seccion, los defaults del record.
builder.Services.AddSingleton(new TranscodeOptions());

builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<IMediaAnalyzer, FfprobeMediaAnalyzer>();

builder.Services.AddScoped<IMediaRepository, SqliteMediaRepository>();

// Scoped para que cada request tenga su propia instancia de RegisterMediaUseCase y de FakeMediaAnalyzer
// No se tiene que compartir entre requests, porque cada request puede tener su propio estado de ejecución.
builder.Services.AddScoped<RegisterMediaUseCase>();
builder.Services.AddScoped<GetMediaByIdUseCase>();
builder.Services.AddScoped<ListMediaUseCase>();
builder.Services.AddScoped<UpdateMediaUseCase>();
builder.Services.AddScoped<StartStreamUseCase>();
builder.Services.AddScoped<EncodeMediaUseCase>();
builder.Services.AddScoped<GetEncodeStatusUseCase>();
builder.Services.AddScoped<HeartbeatUseCase>();
builder.Services.AddScoped<GetSessionUseCase>();
builder.Services.AddScoped<GetPlaylistUseCase>();
builder.Services.AddScoped<GetHlsFileUseCase>();

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

// Cache de artefactos: PhysicalFileProvider falla si el directorio no existe.
var cacheDir = Path.GetFullPath(cache.Dir);
Directory.CreateDirectory(cacheDir);

var contentTypes = new FileExtensionContentTypeProvider();
    contentTypes.Mappings[".m4s"]  = "video/iso.segment";
    contentTypes.Mappings[".vtt"]  = "text/vtt";
    contentTypes.Mappings[".m3u8"] = "application/vnd.apple.mpegurl";

// El manejador de excepciones va primero para que cubra todo lo que viene despues.
app.UseExceptionHandler();

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    if (path.StartsWithSegments("/hls"))
    {
        if (path.Value!.EndsWith("/internal.m3u8", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (path.Value.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            ctx.Response.Headers.CacheControl = "no-store";
    }
    await next();
});

// Segmentos, init.mp4 y subtitulos. Las playlists calculadas (master.m3u8,
// playlist.m3u8) no existen en disco: este middleware pasa de largo y las
// resuelve HlsController.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(cacheDir),
    RequestPath = "/hls",
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        if (!ctx.File.Name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl =
                ctx.File.Name.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)
                    ? "no-store"
                    : "public, max-age=31536000, immutable";
    },
});

// Frontend compilado: wwwroot. UseDefaultFiles va ANTES de UseStaticFiles y es
// lo que hace que "/new/" sirva new/index.html.
app.UseDefaultFiles();
// Inmutable solo /assets: Vite le pone el hash al nombre. Lo de public/ (icons.svg,
// fonts, favicon) conserva el nombre entre builds; con immutable el navegador se
// quedaba con el sprite viejo y no veia los simbolos nuevos.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Path.StartsWithSegments("/assets")
                ? "public, max-age=31536000, immutable"
                : "no-cache",
});

app.MapOpenApi();
app.MapControllers();

// Cualquier /api que ningun controller matcheo, con el formato de error del proyecto.
// El catch-all tiene la menor precedencia: los controllers le ganan.
app.Map("/api/{**rest}", (string rest) => Results.Json(
        new { error = "not_found", detail = $"No existe el endpoint /api/{rest}" },
        statusCode: StatusCodes.Status404NotFound))
   .ExcludeFromDescription();

// No necesario para una LAN.
// app.UseHttpsRedirection();


app.Run();
