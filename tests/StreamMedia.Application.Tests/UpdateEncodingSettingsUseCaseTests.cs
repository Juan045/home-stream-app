using StreamMedia.Application.Encoder;
using StreamMedia.Application.Encoder.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Domain.Entities;

public class UpdateEncodingSettingsUseCaseTests
{
    // Cada campo por separado: si uno solo es invalido, tampoco se guarda el otro.
    [Theory]
    [InlineData("vp9", "av1")]
    [InlineData("h264", "vp9")]
    [InlineData(null, "av1")]
    [InlineData("h264", null)]
    [InlineData(null, null)]
    [InlineData("", "av1")]
    [InlineData("H264", "av1")] // el codec es una clave exacta: asi llega a VideoEncoders.Get
    public async Task Un_codec_invalido_lanza_y_no_guarda(string? liveCodec, string? archiveCodec)
    {
        var repository = new FakeRepository();
        var useCase = new UpdateEncodingSettingsUseCase(repository);

        await Assert.ThrowsAsync<InvalidRequestException>(() => useCase.ExecuteAsync(liveCodec, archiveCodec));

        Assert.Empty(repository.Saved);
    }

    [Theory]
    [InlineData("h264", "h264")]
    [InlineData("h264", "av1")]
    [InlineData("av1", "h264")]
    [InlineData("av1", "av1")]
    public async Task Un_par_valido_se_guarda_y_se_devuelve(string liveCodec, string archiveCodec)
    {
        var repository = new FakeRepository();
        var expected = new EncodingSettings(liveCodec, archiveCodec);

        var result = await new UpdateEncodingSettingsUseCase(repository).ExecuteAsync(liveCodec, archiveCodec);

        Assert.Equal(expected, result);
        Assert.Equal(expected, Assert.Single(repository.Saved));
    }

    sealed class FakeRepository : IEncodingSettingsRepository
    {
        public List<EncodingSettings> Saved { get; } = [];

        public Task<EncodingSettings> GetAsync(CancellationToken ct = default)
            => Task.FromResult(EncodingSettings.Default);

        public Task SaveAsync(EncodingSettings settings, CancellationToken ct = default)
        {
            Saved.Add(settings);
            return Task.CompletedTask;
        }
    }
}
