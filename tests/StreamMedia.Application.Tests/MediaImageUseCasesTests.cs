using StreamMedia.Application.Images;
using StreamMedia.Application.Images.Interfaces;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Domain.Entities;

// Guardar, leer y borrar el poster o el backdrop de una ficha, con fakes y reloj fijo.
public class MediaImageUseCasesTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    static readonly Media Ficha = Media.FromSource("peliculas/a.mkv", new SourceInfo(100, "h264", 1920, 1080, [], []));
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0];
    static readonly byte[] WebP = [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8, .. "VP8 "u8, 0, 0];
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    static byte[] JpegOfLength(int length)
    {
        var data = new byte[length];
        Jpeg.AsSpan(0, 3).CopyTo(data);
        return data;
    }

    static SaveMediaImageUseCase SaveOf(Media? ficha, FakeImages images)
        => new(new FakeMedia(ficha), images, new FixedTime(Now));

    [Fact]
    public void MaxBytes_es_un_MiB()
        => Assert.Equal(1_048_576, SaveMediaImageUseCase.MaxBytes);

    [Fact]
    public async Task Guardar_en_una_ficha_inexistente_lanza_media_not_found_y_no_guarda()
    {
        var images = new FakeImages();

        var ex = await Assert.ThrowsAsync<MediaPathException>(
            () => SaveOf(null, images).ExecuteAsync(Guid.NewGuid(), MediaImageRole.Poster, Jpeg));

        Assert.Equal("media_not_found", ex.Code);
        Assert.Empty(images.Saved);
    }

    // La ficha se valida primero: con datos invalidos igual gana media_not_found.
    [Fact]
    public async Task La_ficha_se_valida_antes_que_los_bytes()
    {
        var images = new FakeImages();

        var ex = await Assert.ThrowsAsync<MediaPathException>(
            () => SaveOf(null, images).ExecuteAsync(Guid.NewGuid(), MediaImageRole.Poster, []));

        Assert.Equal("media_not_found", ex.Code);
    }

    [Fact]
    public async Task Un_cuerpo_vacio_es_invalid_image()
    {
        var images = new FakeImages();

        var ex = await Assert.ThrowsAsync<ImageException>(
            () => SaveOf(Ficha, images).ExecuteAsync(Ficha.Id, MediaImageRole.Poster, []));

        Assert.Equal("invalid_image", ex.Code);
        Assert.Empty(images.Saved);
    }

    // Firma valida: lo que rechaza es el tamano, no el formato.
    [Fact]
    public async Task Un_byte_mas_que_MaxBytes_es_image_too_large()
    {
        var images = new FakeImages();

        var ex = await Assert.ThrowsAsync<ImageException>(() => SaveOf(Ficha, images)
            .ExecuteAsync(Ficha.Id, MediaImageRole.Poster, JpegOfLength(SaveMediaImageUseCase.MaxBytes + 1)));

        Assert.Equal("image_too_large", ex.Code);
        Assert.Empty(images.Saved);
    }

    [Fact]
    public async Task Exactamente_MaxBytes_se_acepta()
    {
        var images = new FakeImages();

        await SaveOf(Ficha, images).ExecuteAsync(
            Ficha.Id, MediaImageRole.Poster, JpegOfLength(SaveMediaImageUseCase.MaxBytes));

        Assert.Single(images.Saved);
    }

    [Fact]
    public async Task Un_png_es_invalid_image()
    {
        var images = new FakeImages();

        var ex = await Assert.ThrowsAsync<ImageException>(
            () => SaveOf(Ficha, images).ExecuteAsync(Ficha.Id, MediaImageRole.Poster, Png));

        Assert.Equal("invalid_image", ex.Code);
        Assert.Empty(images.Saved);
    }

    [Fact]
    public async Task Un_webp_se_guarda_con_su_tipo_los_mismos_bytes_y_la_hora_del_reloj()
    {
        var images = new FakeImages();

        var result = await SaveOf(Ficha, images).ExecuteAsync(Ficha.Id, MediaImageRole.Backdrop, WebP);

        var (id, role, saved) = Assert.Single(images.Saved);
        Assert.Equal(Ficha.Id, id);
        Assert.Equal(MediaImageRole.Backdrop, role);
        Assert.Equal("image/webp", saved.ContentType);
        Assert.True(WebP.SequenceEqual(saved.Data));
        Assert.Equal(Now, saved.UpdatedAt);
        Assert.Same(saved, result);
    }

    [Fact]
    public async Task Un_jpeg_se_guarda_como_image_jpeg()
    {
        var images = new FakeImages();

        await SaveOf(Ficha, images).ExecuteAsync(Ficha.Id, MediaImageRole.Poster, Jpeg);

        Assert.Equal("image/jpeg", Assert.Single(images.Saved).Image.ContentType);
    }

    [Fact]
    public async Task Leer_sin_imagen_lanza_image_not_found()
    {
        var ex = await Assert.ThrowsAsync<ImageException>(
            () => new GetMediaImageUseCase(new FakeImages()).ExecuteAsync(Guid.NewGuid(), MediaImageRole.Poster));

        Assert.Equal("image_not_found", ex.Code);
    }

    [Fact]
    public async Task Leer_devuelve_la_imagen_del_repositorio_sin_cambios()
    {
        var stored = new MediaImage("image/jpeg", Jpeg, Now);

        var result = await new GetMediaImageUseCase(new FakeImages { Stored = stored })
            .ExecuteAsync(Guid.NewGuid(), MediaImageRole.Poster);

        Assert.Same(stored, result);
    }

    [Fact]
    public async Task Borrar_en_una_ficha_inexistente_lanza_media_not_found_y_no_borra()
    {
        var images = new FakeImages();

        var ex = await Assert.ThrowsAsync<MediaPathException>(() => new DeleteMediaImageUseCase(new FakeMedia(null), images)
            .ExecuteAsync(Guid.NewGuid(), MediaImageRole.Poster));

        Assert.Equal("media_not_found", ex.Code);
        Assert.Empty(images.Deleted);
    }

    // Borrar es idempotente: sin imagen no hay error, se delega igual.
    [Fact]
    public async Task Borrar_sin_imagen_no_lanza_y_delega_en_el_repositorio()
    {
        var images = new FakeImages();

        await new DeleteMediaImageUseCase(new FakeMedia(Ficha), images).ExecuteAsync(Ficha.Id, MediaImageRole.Backdrop);

        Assert.Equal((Ficha.Id, MediaImageRole.Backdrop), Assert.Single(images.Deleted));
    }

    sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class FakeImages : IMediaImageRepository
    {
        public MediaImage? Stored { get; init; }
        public List<(Guid Id, MediaImageRole Role, MediaImage Image)> Saved { get; } = [];
        public List<(Guid Id, MediaImageRole Role)> Deleted { get; } = [];

        public Task<MediaImage?> GetAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default)
            => Task.FromResult(Stored);

        public Task SaveAsync(Guid idMedia, MediaImageRole role, MediaImage image, CancellationToken ct = default)
        {
            Saved.Add((idMedia, role, image));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default)
        {
            Deleted.Add((idMedia, role));
            return Task.CompletedTask;
        }
    }

    sealed class FakeMedia(Media? ficha) : IMediaRepository
    {
        public Task<Media?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(ficha);
        public Task<Media?> FindByPathAsync(string filePath, CancellationToken ct = default) => Task.FromResult(ficha);
        public Task AddAsync(Media media, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Media media, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Media>> ListAsync(MediaListQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Media>>([]);

        public Task<int> CountAsync(MediaListQuery query, CancellationToken ct = default) => Task.FromResult(0);
    }
}
