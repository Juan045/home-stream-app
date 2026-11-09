using StreamMedia.Infrastructure.Cache;

public class SourceIdentityTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    // Fechas enteras y separadas por horas: no dependen de la resolucion del mtime del disco.
    static readonly DateTime Monday = new(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Tuesday = new(2024, 1, 2, 10, 0, 0, DateTimeKind.Utc);

    string FileWith(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, Monday);
        return path;
    }

    // Si el id no fuera estable, cada reinicio tiraria todo el cache del asset.
    [Fact]
    public void El_mismo_archivo_sin_tocar_da_siempre_el_mismo_id()
    {
        var source = FileWith("video.mkv", "contenido");
        var identity = new SourceIdentity();

        var first = identity.AssetIdFor(source);

        Assert.Equal(first, identity.AssetIdFor(source));
        Assert.Equal(first, new SourceIdentity().AssetIdFor(source));
    }

    // Un archivo reemplazado con otro mtime es otro video: no puede heredar el cache.
    [Fact]
    public void Cambiar_el_mtime_cambia_el_id()
    {
        var source = FileWith("video.mkv", "contenido");
        var identity = new SourceIdentity();
        var before = identity.AssetIdFor(source);

        File.SetLastWriteTimeUtc(source, Tuesday);

        Assert.NotEqual(before, identity.AssetIdFor(source));
    }

    // Se restaura el mtime para que el unico cambio sea el tamano.
    [Fact]
    public void Cambiar_el_tamano_cambia_el_id()
    {
        var source = FileWith("video.mkv", "abc");
        var identity = new SourceIdentity();
        var before = identity.AssetIdFor(source);

        File.WriteAllText(source, "abcdef");
        File.SetLastWriteTimeUtc(source, Monday);

        Assert.Equal(Monday, File.GetLastWriteTimeUtc(source));
        Assert.NotEqual(before, identity.AssetIdFor(source));
    }

    // Dos copias del mismo video en carpetas distintas no comparten cache.
    [Fact]
    public void Dos_archivos_iguales_en_rutas_distintas_dan_ids_distintos()
    {
        var a = FileWith("a.mkv", "contenido");
        var b = FileWith("b.mkv", "contenido");
        var identity = new SourceIdentity();

        Assert.NotEqual(identity.AssetIdFor(a), identity.AssetIdFor(b));
    }

    // En Windows la ruta no distingue mayusculas: el mismo archivo no puede generar dos caches.
    [Fact]
    public void En_Windows_la_ruta_con_otras_mayusculas_da_el_mismo_id()
    {
        // xUnit 2.9 no tiene skip dinamico; fuera de Windows las rutas si distinguen mayusculas.
        if (!OperatingSystem.IsWindows())
            return;

        var source = FileWith("video.mkv", "contenido");
        var identity = new SourceIdentity();

        Assert.Equal(identity.AssetIdFor(source), identity.AssetIdFor(source.ToUpperInvariant()));
    }

    // El id es el nombre de la carpeta del asset: formato fijo, sin caracteres raros.
    [Fact]
    public void El_id_son_16_caracteres_hexadecimales_en_minuscula()
    {
        var source = FileWith("video.mkv", "contenido");

        Assert.Matches("^[0-9a-f]{16}$", new SourceIdentity().AssetIdFor(source));
    }

    [Fact]
    public void Lanza_FileNotFoundException_si_el_archivo_no_existe()
    {
        var missing = Path.Combine(_root, "no-existe.mkv");

        Assert.Throws<FileNotFoundException>(() => new SourceIdentity().AssetIdFor(missing));
    }

    // Sin la carpeta, File.* tiraria DirectoryNotFoundException; el contrato pide la misma excepcion.
    [Fact]
    public void Lanza_FileNotFoundException_si_tampoco_existe_la_carpeta()
    {
        var missing = Path.Combine(_root, "no-existe", "video.mkv");

        Assert.Throws<FileNotFoundException>(() => new SourceIdentity().AssetIdFor(missing));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
