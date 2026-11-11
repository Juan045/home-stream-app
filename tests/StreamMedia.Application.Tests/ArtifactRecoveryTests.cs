using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;

// La tabla de .claude/Feature-Artifact-State.md §1: manifest x disco -> estado.
public class ArtifactRecoveryTests
{
    static Artifact Recovered(ArtifactState? inManifest, bool complete, string? error = null)
    {
        var artifact = new Artifact();
        ArtifactRecovery.Recover(artifact, inManifest, error, () => complete);
        return artifact;
    }

    // Decision del 2026-11-10: con error, los segmentos pueden estar mal formados aunque la playlist este cerrada.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_queda_failed_con_su_error_este_como_este_el_disco(bool complete)
    {
        var artifact = Recovered(ArtifactState.Failed, complete, "stderr");

        Assert.Equal(ArtifactState.Failed, artifact.State);
        Assert.Equal("stderr", artifact.Error);
        Assert.False(artifact.Interrupted);
    }

    // El Func lanza: si un failed leyera el disco, el test falla.
    [Fact]
    public void Failed_no_lee_el_disco()
    {
        var artifact = new Artifact();

        ArtifactRecovery.Recover(artifact, ArtifactState.Failed, null,
            () => throw new InvalidOperationException("no debia leer el disco"));

        Assert.Equal(ArtifactState.Failed, artifact.State);
    }

    [Theory]
    [InlineData(ArtifactState.Ready)]
    [InlineData(ArtifactState.Building)]
    [InlineData(ArtifactState.Pending)]
    [InlineData(null)]
    public void Completo_en_disco_esta_listo(ArtifactState? inManifest)
    {
        var artifact = Recovered(inManifest, complete: true, error: "viejo");

        Assert.Equal(ArtifactState.Ready, artifact.State);
        Assert.False(artifact.Interrupted);
        Assert.Null(artifact.Error);
    }

    [Fact]
    public void Building_incompleto_esta_pendiente_e_interrumpido()
    {
        var artifact = Recovered(ArtifactState.Building, complete: false);

        Assert.Equal(ArtifactState.Pending, artifact.State);
        Assert.True(artifact.Interrupted);
    }

    // Un ready cuyos archivos ya no estan tambien se rehace: manda el disco.
    [Theory]
    [InlineData(ArtifactState.Ready)]
    [InlineData(ArtifactState.Pending)]
    [InlineData(null)]
    public void Incompleto_sin_building_esta_pendiente_sin_interrumpir(ArtifactState? inManifest)
    {
        var artifact = Recovered(inManifest, complete: false, error: "viejo");

        Assert.Equal(ArtifactState.Pending, artifact.State);
        Assert.False(artifact.Interrupted);
        Assert.Null(artifact.Error); // el motivo solo acompana a un failed
    }
}
