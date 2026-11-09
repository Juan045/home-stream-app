using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;

public class AssetPlayabilityTests
{
    // Un id propio, para que la carpeta esperada no coincida con la de otro test.
    // Que el llamador pase las rutas del asset correcto ya no se prueba aca (StartStream y GetSession).
    const string AssetId = "asset-playability-42";

    static readonly CacheLayout Layout = new("/cache");

    static readonly SourceInfo Info = new(
        100, "hevc", 1920, 1080, [new AudioTrack(0, "spa", "aac")], [], ColorTransfer: "bt709");

    static Asset AssetWith(ArtifactState video)
    {
        var asset = new Asset(AssetId, "/media/pelicula.mkv", Info);
        asset.Video.State = video;
        return asset;
    }

    // Failed con 2 segmentos da true: es el comportamiento de hoy. La etapa 2 del plan
    // lo cambia a proposito (un video fallido no es reproducible); ahi se ajusta esta fila.
    [Theory]
    [InlineData(ArtifactState.Ready, 0, true)]
    [InlineData(ArtifactState.Building, 2, true)]
    [InlineData(ArtifactState.Building, 1, false)]
    [InlineData(ArtifactState.Pending, 0, false)]
    [InlineData(ArtifactState.Failed, 2, true)]
    public void Se_puede_mirar_con_el_video_listo_o_con_al_menos_dos_segmentos(
        ArtifactState video, int segments, bool expected)
    {
        var store = new FakeStore { Segments = segments };

        Assert.Equal(expected, AssetPlayability.IsPlayable(AssetWith(video), store, Layout.Paths(AssetId)));
    }

    // Los segmentos que importan son los del video: audio o la raiz no alcanzan para arrancar.
    [Fact]
    public void Sin_terminar_cuenta_en_la_carpeta_de_video_del_asset()
    {
        var store = new FakeStore { Segments = 2 };
        var asset = AssetWith(ArtifactState.Building);

        AssetPlayability.IsPlayable(asset, store, Layout.Paths(asset.Id));

        var directory = Assert.Single(store.Counted);
        Assert.Equal(Layout.Paths(asset.Id).Video, directory);
    }

    // GET /sessions/{id} lo consulta cada 2 s por espectador: con el video listo no hay que tocar disco.
    [Fact]
    public void Con_el_video_listo_no_cuenta_segmentos()
    {
        var store = new FakeStore { Segments = 0 };

        AssetPlayability.IsPlayable(AssetWith(ArtifactState.Ready), store, Layout.Paths(AssetId));

        Assert.Empty(store.Counted);
    }

    // Solo CountSegments importa; el resto devuelve valores neutros.
    sealed class FakeStore : IHlsOutput
    {
        public int Segments { get; init; }
        public List<string> Counted { get; } = [];

        public int CountSegments(string directory)
        {
            Counted.Add(directory);
            return Segments;
        }

        public void CreateDirectories(AssetPaths paths) { }

        public Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool IsPlaylistComplete(string directory) => false;
        public bool FileExists(string path) => false;
        public void PrepareBuild(string directory) { }
    }
}
