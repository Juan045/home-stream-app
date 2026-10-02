using StreamMedia.Domain.DTO;

namespace StreamMedia.Domain.Entities;

// La "ficha" del catalogo
public class Media
{
    public Guid Id { get; private set; }

    public string FilePath { get; private set; } = default!; // relativa a MEDIA_ROOT

    public string Title { get; private set; } = default!;
    public int? Year { get; private set; }
    public string? Synopsis { get; private set; }
    public string? Kind { get; private set; }
    // Si esta en "Mi lista". Lo filtra el listado (?in_list=true).
    public bool? InList { get; private set; }
    public string? AssetId { get; private set; }

    // TODO: sacar el "?". FromSource y Rehydrate siempre lo completan, nunca
    // queda en null - el nullable de aca solo obliga a poner "!" en cada
    // lugar que lo usa (ej. MediaController), tapando con una promesa manual
    // lo que el compilador podria garantizar solo. Sacarlo es 0 riesgo hoy:
    // ningun caller construye un Media sin esto.
    public SourceInfo? SourceInfo { get; private set; } = default!;

    //Constructor
    private Media() { }

    public static Media FromSource(string filePath, SourceInfo sourceInfo, string? assetId = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return new Media
        {
            Id = Guid.NewGuid(),
            FilePath = filePath,
            SourceInfo = sourceInfo,
            Title = fileName, // Media.from_source en Python
            Kind = "film",
            InList = false,
            AssetId = assetId,
        };
    }

    // Reconstruye un Media ya existente a partir de lo que devuelve el repositorio.
    // A diferencia de FromSource, no deriva nada: usa los valores tal como estan guardados.
    public static Media Rehydrate(
        Guid id,
        string filePath,
        string title,
        int? year,
        string? synopsis,
        SourceInfo info,
        string? kind,
        bool? inList,
        string? assetId = null)
    {
        return new Media
        {
            Id = id,
            FilePath = filePath,
            Title = title,
            Year = year,
            Synopsis = synopsis,
            SourceInfo = info,
            Kind = kind,
            InList = inList,
            AssetId = assetId,
        };
    }

    public void Update(
        string? title = null,
        string? kind = null,
        string? synopsis = null,
        SourceInfo? info = null)
    {
        if (title is not null)
            Title = title;

        if (kind is not null)
            Kind = kind;

        if (synopsis is not null)
            Synopsis = synopsis;

        if (info is not null)
            SourceInfo = info;
    }
}
