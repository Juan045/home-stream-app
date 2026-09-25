using System;
using Microsoft.VisualBasic;

namespace StreamMedia.Domain.Entities;

// La "ficha" del catalogo
public class Media
{
    public Guid Id { get; private set; }

    public string FilePath { get; private set; } = default!; // relativa a MEDIA_ROOT

    public string Title { get; private set; } = default!;
    public int? Year { get; private set; }
    public string? Synopsis { get; private set; }
    public SourceInfo? SourceInfo { get; private set; } = default!;

    //Constructor
    private Media() { }

    public static Media FromSource(string filePath, SourceInfo sourceInfo)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return new Media
        {
            Id = Guid.NewGuid(),
            FilePath = filePath,
            SourceInfo = sourceInfo,
            Title = fileName, // Media.from_source en Python
        };
    }

    // Reconstruye un Media ya existente a partir de lo que devuelve el repositorio.
    // A diferencia de FromSource, no deriva nada: usa los valores tal como estan guardados.
    public static Media Rehydrate(Guid id, string filePath, string title, int? year, string? synopsis, SourceInfo info)
    {
        return new Media
        {
            Id = id,
            FilePath = filePath,
            Title = title,
            Year = year,
            Synopsis = synopsis,
            SourceInfo = info,
        };
    }
}
