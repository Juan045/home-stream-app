using StreamMedia.Application.Media;

public class MediaPathResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly MediaPathResolver _resolver;

    public MediaPathResolverTests()
    {
        File.WriteAllText(Path.Combine(_root, "peli.mkv"), "");
        _resolver = new MediaPathResolver(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Resuelve_una_ruta_valida_a_absoluta() =>
        Assert.Equal(Path.Combine(_root, "peli.mkv"), _resolver.Resolve("peli.mkv"));

    [Theory]
    [InlineData("/etc/passwd", "invalid_path")]
    [InlineData("../fuera.mkv", "outside_media_root")]
    [InlineData("peli.avi", "unsupported_extension")]
    [InlineData("no-existe.mkv", "file_not_found")]
    public void Rechaza_rutas_invalidas(string ruta, string codigo)
    {
        var ex = Assert.Throws<MediaPathException>(() => _resolver.Resolve(ruta));
        Assert.Equal(codigo, ex.Code);
    }
}