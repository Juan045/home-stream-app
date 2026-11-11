using StreamMedia.Domain.Entities;

namespace StreamMedia.Domain.Tests;

public class AssetTests
{
    static Asset AssetWith(ArtifactState video, params ArtifactState[] audio)
    {
        var asset = new Asset("asset", "/media/pelicula.mkv", new SourceInfo(100, "h264", 1920, 1080, [], []));
        asset.Video.State = video;
        for (var i = 0; i < audio.Length; i++)
            asset.Audio[i] = new Artifact { State = audio[i] };
        return asset;
    }

    // C6: un audio failed ya no va a cambiar. Si contara como pendiente, la sesion nunca terminaria.
    [Fact]
    public void Con_el_video_listo_y_un_audio_fallido_esta_listo()
    {
        Assert.Equal(AssetStatus.Ready, AssetWith(ArtifactState.Ready, ArtifactState.Ready, ArtifactState.Failed).Status);
    }

    [Fact]
    public void Con_un_audio_en_curso_sigue_procesando()
    {
        Assert.Equal(AssetStatus.Processing, AssetWith(ArtifactState.Ready, ArtifactState.Building).Status);
    }

    [Fact]
    public void Con_el_video_fallido_fallo_aunque_el_audio_este_listo()
    {
        Assert.Equal(AssetStatus.Failed, AssetWith(ArtifactState.Failed, ArtifactState.Ready).Status);
    }

    // Disponible = registrada y no fallida: lo que el master y la sesion anuncian (C7).
    [Theory]
    [InlineData(ArtifactState.Pending, true)]
    [InlineData(ArtifactState.Building, true)]
    [InlineData(ArtifactState.Ready, true)]
    [InlineData(ArtifactState.Failed, false)]
    public void Una_pista_registrada_esta_disponible_salvo_que_haya_fallado(ArtifactState state, bool expected)
    {
        Assert.Equal(expected, AssetWith(ArtifactState.Ready, state).IsAudioAvailable(0));
    }

    [Fact]
    public void Una_pista_no_registrada_no_esta_disponible()
    {
        Assert.False(AssetWith(ArtifactState.Ready).IsAudioAvailable(0));
    }
}
