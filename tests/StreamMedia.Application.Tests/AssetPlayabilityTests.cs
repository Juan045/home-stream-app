using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;

public class AssetPlayabilityTests
{
    // Un id propio: los casos afirman que se cuenta en la carpeta de video de este asset.
    const string AssetId = "asset-playability-42";

    static readonly SourceInfo Info = new(
        100, "hevc", 1920, 1080, [new AudioTrack(0, "spa", "aac")], [], ColorTransfer: "bt709");

    static Asset AssetWith(ArtifactState video)
    {
        var asset = new Asset(AssetId, "/media/pelicula.mkv", Info);
        asset.Video.State = video;
        return asset;
    }

    // Failed con segmentos no se mira: pueden estar cortados o mal formados (C9).
    [Theory]
    [InlineData(ArtifactState.Ready, 0, true)]
    [InlineData(ArtifactState.Building, 2, true)]
    [InlineData(ArtifactState.Building, 1, false)]
    [InlineData(ArtifactState.Pending, 0, false)]
    [InlineData(ArtifactState.Failed, 2, false)]
    public void Se_puede_mirar_con_el_video_listo_o_con_al_menos_dos_segmentos(
        ArtifactState video, int segments, bool expected)
    {
        var store = new FakeStore { Segments = segments };

        Assert.Equal(expected, AssetPlayability.IsPlayable(AssetWith(video), store));
    }

    // Los segmentos que importan son los del video: audio o la raiz no alcanzan para arrancar.
    [Fact]
    public void Sin_terminar_cuenta_en_la_carpeta_de_video_del_asset()
    {
        var store = new FakeStore { Segments = 2 };
        var asset = AssetWith(ArtifactState.Building);

        AssetPlayability.IsPlayable(asset, store);

        var counted = Assert.Single(store.Counted);
        Assert.Equal((asset.Id, OutputFolder.Video), counted);
    }

    // GET /sessions/{id} lo consulta cada 2 s por espectador: con el video listo no hay que tocar disco.
    [Fact]
    public void Con_el_video_listo_no_cuenta_segmentos()
    {
        var store = new FakeStore { Segments = 0 };

        AssetPlayability.IsPlayable(AssetWith(ArtifactState.Ready), store);

        Assert.Empty(store.Counted);
    }

    // Con el video fallido la respuesta no depende del disco: tampoco hay que tocarlo.
    [Fact]
    public void Con_el_video_fallido_no_cuenta_segmentos()
    {
        var store = new FakeStore { Segments = 10 };

        Assert.False(AssetPlayability.IsPlayable(AssetWith(ArtifactState.Failed), store));
        Assert.Empty(store.Counted);
    }

    // Solo CountSegments importa; el resto devuelve valores neutros.
    sealed class FakeStore : IHlsOutput
    {
        public int Segments { get; init; }
        public List<(string AssetId, OutputFolder Folder)> Counted { get; } = [];

        public int CountSegments(string assetId, OutputFolder folder)
        {
            Counted.Add((assetId, folder));
            return Segments;
        }

        public void CreateDirectories(string assetId) { }

        public Task<string?> ReadInternalPlaylistAsync(string assetId, OutputFolder folder, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool IsPlaylistComplete(string assetId, OutputFolder folder) => false;
        public string? FindFile(string assetId, OutputFolder folder, string fileName) => null;
        public void PrepareBuild(string assetId, OutputFolder folder) { }
    }
}
