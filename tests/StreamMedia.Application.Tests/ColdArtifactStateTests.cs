using StreamMedia.Application.Artifacts;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;

public class ColdArtifactStateTests
{
    const int Index = 2;
    const string VttName = "sub_2_spa.vtt";

    static AssetManifest ManifestOf(bool listed = false, bool failed = false) => new()
    {
        Info = new SourceInfo(100, "h264", 1920, 1080, [], []),
        Subtitles = listed ? new() { [Index] = VttName } : [],
        SubtitlesFailed = failed ? [Index] : [],
    };

    [Theory]
    [InlineData(ArtifactState.Building, true, "ready")]
    [InlineData(ArtifactState.Building, false, "interrupted")]
    [InlineData(ArtifactState.Pending, true, "ready")]
    [InlineData(ArtifactState.Pending, false, "pending")]
    [InlineData(null, true, "ready")]
    [InlineData(null, false, "pending")]
    public void Sin_estado_final_decide_la_playlist_en_disco(ArtifactState? inManifest, bool complete, string expected)
    {
        Assert.Equal(expected, ColdArtifactState.Resolve(inManifest, () => complete));
    }

    // El Func lanza: si alguno de estos estados leyera el disco, el test falla.
    [Theory]
    [InlineData(ArtifactState.Ready, "ready")]
    [InlineData(ArtifactState.Failed, "failed")]
    public void Con_estado_final_no_se_lee_el_disco(ArtifactState inManifest, string expected)
    {
        Func<bool> mustNotRun = () => throw new InvalidOperationException("no debia leer el disco");

        Assert.Equal(expected, ColdArtifactState.Resolve(inManifest, mustNotRun));
    }

    [Fact]
    public void Subtitulo_listado_con_su_archivo_esta_listo()
    {
        var checkedName = "";

        var state = ColdArtifactState.ResolveSubtitle(ManifestOf(listed: true), Index, name =>
        {
            checkedName = name;
            return true;
        });

        Assert.Equal("ready", state);
        Assert.Equal(VttName, checkedName); // el archivo que dice el manifest, no uno armado de nuevo
    }

    [Fact]
    public void Subtitulo_listado_cuyo_archivo_ya_no_esta_queda_pendiente()
    {
        Assert.Equal("pending", ColdArtifactState.ResolveSubtitle(ManifestOf(listed: true), Index, _ => false));
    }

    [Fact]
    public void Subtitulo_fallido_figura_fallido()
    {
        Assert.Equal("failed", ColdArtifactState.ResolveSubtitle(ManifestOf(failed: true), Index, _ => false));
    }

    [Fact]
    public void Subtitulo_sin_registrar_queda_pendiente()
    {
        Assert.Equal("pending", ColdArtifactState.ResolveSubtitle(ManifestOf(), Index, _ => true));
    }

    // Mismo orden que AssetBuilder.SubtitleState: lo mostrado coincide con lo que haria reabrirlo.
    [Theory]
    [InlineData(true, "ready")]
    [InlineData(false, "failed")]
    public void Listado_y_fallido_a_la_vez_manda_el_archivo(bool vttExists, string expected)
    {
        var manifest = ManifestOf(listed: true, failed: true);

        Assert.Equal(expected, ColdArtifactState.ResolveSubtitle(manifest, Index, _ => vttExists));
    }
}
