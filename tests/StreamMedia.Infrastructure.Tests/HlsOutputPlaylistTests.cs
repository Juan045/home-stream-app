using StreamMedia.Domain.DTO;
using StreamMedia.Infrastructure.Cache;

public class HlsOutputPlaylistTests : IDisposable
{
    // Con "\n" explicitos y no un literal multilinea: el contenido no depende del fin de linea de este archivo.
    const string Segments =
        "#EXTM3U\n" +
        "#EXT-X-VERSION:7\n" +
        "#EXT-X-TARGETDURATION:6\n" +
        "#EXT-X-MEDIA-SEQUENCE:0\n" +
        "#EXT-X-MAP:URI=\"init.mp4\"\n" +
        "#EXTINF:6.006000,\n" +
        "seg-00000.m4s\n" +
        "#EXTINF:6.006000,\n" +
        "seg-00001.m4s\n";

    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    // Carpeta de build con la playlist interna tal como la deja FFmpeg; sin contenido, la carpeta queda vacia.
    string BuildDirectory(string? playlist = null)
    {
        var directory = Path.Combine(_root, "video");
        Directory.CreateDirectory(directory);
        if (playlist is not null)
            File.WriteAllText(Path.Combine(directory, HlsLayout.InternalPlaylist), playlist);
        return directory;
    }

    string MissingDirectory() => Path.Combine(_root, "no-existe", "video");

    // FFmpeg crea la playlist recien con el primer segmento: antes de eso el build no termino.
    [Fact]
    public void Sin_playlist_no_esta_completa()
    {
        Assert.False(new HlsOutput().IsPlaylistComplete(BuildDirectory()));
    }

    // Un asset recolectado por el GC no tiene carpeta; preguntar no puede romper.
    [Fact]
    public void Sin_carpeta_no_esta_completa()
    {
        Assert.False(new HlsOutput().IsPlaylistComplete(MissingDirectory()));
    }

    // Solo el ENDLIST que FFmpeg escribe al terminar bien marca el fin; HLS define la etiqueta en mayusculas.
    [Theory]
    [InlineData("", false)]
    [InlineData("#EXT-X-ENDLIST\n", true)]
    [InlineData("#ext-x-endlist\n", false)]
    public void Esta_completa_solo_con_la_etiqueta_ENDLIST(string ending, bool expected)
    {
        var directory = BuildDirectory(Segments + ending);

        Assert.Equal(expected, new HlsOutput().IsPlaylistComplete(directory));
    }

    // null y no excepcion: es el estado normal antes del primer segmento.
    [Fact]
    public async Task Sin_playlist_lee_null()
    {
        Assert.Null(await new HlsOutput().ReadInternalPlaylistAsync(BuildDirectory()));
    }

    [Fact]
    public async Task Sin_carpeta_lee_null()
    {
        Assert.Null(await new HlsOutput().ReadInternalPlaylistAsync(MissingDirectory()));
    }

    // El texto lo interpreta PlaylistBuilder: leerlo no puede normalizar saltos de linea ni recortar nada.
    [Fact]
    public async Task Lee_la_playlist_sin_modificarla()
    {
        const string playlist = Segments + "#EXT-X-ENDLIST\n";
        var directory = BuildDirectory(playlist);

        Assert.Equal(playlist, await new HlsOutput().ReadInternalPlaylistAsync(directory));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
