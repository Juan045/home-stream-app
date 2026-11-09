using StreamMedia.Application.Cache;
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

    const string AssetId = "asset";
    const string MissingAssetId = "no-existe";

    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    CacheLayout Layout => new(_root);

    HlsOutput NewOutput() => new(Layout);

    // Carpeta de build con la playlist interna tal como la deja FFmpeg; sin contenido, la carpeta queda vacia.
    // La carpeta sale del mismo layout que usa HlsOutput.
    void BuildDirectory(string? playlist = null)
    {
        var directory = Layout.Dir(AssetId, OutputFolder.Video);
        Directory.CreateDirectory(directory);
        if (playlist is not null)
            File.WriteAllText(Path.Combine(directory, HlsLayout.InternalPlaylist), playlist);
    }

    // FFmpeg crea la playlist recien con el primer segmento: antes de eso el build no termino.
    [Fact]
    public void Sin_playlist_no_esta_completa()
    {
        BuildDirectory();

        Assert.False(NewOutput().IsPlaylistComplete(AssetId, OutputFolder.Video));
    }

    // Un asset recolectado por el GC no tiene carpeta; preguntar no puede romper.
    [Fact]
    public void Sin_carpeta_no_esta_completa()
    {
        Assert.False(NewOutput().IsPlaylistComplete(MissingAssetId, OutputFolder.Video));
    }

    // Solo el ENDLIST que FFmpeg escribe al terminar bien marca el fin; HLS define la etiqueta en mayusculas.
    [Theory]
    [InlineData("", false)]
    [InlineData("#EXT-X-ENDLIST\n", true)]
    [InlineData("#ext-x-endlist\n", false)]
    public void Esta_completa_solo_con_la_etiqueta_ENDLIST(string ending, bool expected)
    {
        BuildDirectory(Segments + ending);

        Assert.Equal(expected, NewOutput().IsPlaylistComplete(AssetId, OutputFolder.Video));
    }

    // null y no excepcion: es el estado normal antes del primer segmento.
    [Fact]
    public async Task Sin_playlist_lee_null()
    {
        BuildDirectory();

        Assert.Null(await NewOutput().ReadInternalPlaylistAsync(AssetId, OutputFolder.Video));
    }

    [Fact]
    public async Task Sin_carpeta_lee_null()
    {
        Assert.Null(await NewOutput().ReadInternalPlaylistAsync(MissingAssetId, OutputFolder.Video));
    }

    // El texto lo interpreta PlaylistBuilder: leerlo no puede normalizar saltos de linea ni recortar nada.
    [Fact]
    public async Task Lee_la_playlist_sin_modificarla()
    {
        const string playlist = Segments + "#EXT-X-ENDLIST\n";
        BuildDirectory(playlist);

        Assert.Equal(playlist, await NewOutput().ReadInternalPlaylistAsync(AssetId, OutputFolder.Video));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
