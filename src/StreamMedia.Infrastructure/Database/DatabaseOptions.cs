namespace StreamMedia.Infrastructure.Database;

/// <summary>
/// Configuracion de la base de datos SQLite.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Ruta del archivo SQLite. Puede ser absoluta o relativa al proceso.
    /// </summary>
    public string Path { get; init; } = "data/media.sqlite";

    /// <summary>
    /// Ruta del schema SQL dentro de los archivos publicados de la aplicacion.
    /// </summary>
    public string SchemaFile { get; init; } = "Database/Schema/schema.sql";
}
