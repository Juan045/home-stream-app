using StreamMedia.Application.Cache;
using StreamMedia.Domain.DTO;
using StreamMedia.Infrastructure.Cache;

// FindFile resuelve el archivo que pide el navegador: el nombre llega de la URL, asi que es entrada no confiable.
public class HlsOutputFindFileTests : IDisposable
{
    const string AssetId = "asset";
    const string SubsFile = "sub_2_spa.vtt";

    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    CacheLayout Layout => new(_root);

    HlsOutput NewOutput() => new(Layout);

    // OutputFolder no entra en InlineData; la carpeta viaja como texto.
    static OutputFolder Folder(string name) => name switch
    {
        "video" => OutputFolder.Video,
        "audio1" => OutputFolder.Audio(1),
        "subs" => OutputFolder.Subs,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    // Crea el archivo dentro de la carpeta que el mismo layout le asigna a ese asset.
    string CreateFile(OutputFolder folder, string fileName)
    {
        var path = Path.Combine(Layout.Dir(AssetId, folder), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Theory]
    [InlineData("video", HlsLayout.InitName)]
    [InlineData("audio1", "seg-00000.m4s")]
    [InlineData("subs", SubsFile)]
    public void Si_existe_devuelve_la_ruta_absoluta(string folderName, string fileName)
    {
        var folder = Folder(folderName);
        CreateFile(folder, fileName);

        var found = NewOutput().FindFile(AssetId, folder, fileName);

        Assert.Equal(Path.GetFullPath(Path.Combine(Layout.Dir(AssetId, folder), fileName)), found);
    }

    // Un segmento que FFmpeg todavia no escribio es el caso normal mientras corre el build.
    [Fact]
    public void Si_no_existe_devuelve_null()
    {
        Directory.CreateDirectory(Layout.Dir(AssetId, OutputFolder.Video));

        Assert.Null(NewOutput().FindFile(AssetId, OutputFolder.Video, HlsLayout.InitName));
    }

    // Un asset recolectado por el GC no tiene carpeta; preguntar no puede romper.
    [Fact]
    public void Sin_carpeta_devuelve_null()
    {
        Assert.Null(NewOutput().FindFile("no-existe", OutputFolder.Audio(3), HlsLayout.InitName));
    }

    [Fact]
    public void Busca_solo_en_la_carpeta_pedida()
    {
        CreateFile(OutputFolder.Subs, SubsFile);
        Directory.CreateDirectory(Layout.Dir(AssetId, OutputFolder.Video));

        Assert.Null(NewOutput().FindFile(AssetId, OutputFolder.Video, SubsFile));
    }

    // Path traversal: el destino existe a proposito, asi el null no se explica por un archivo faltante.
    [Fact]
    public void Nombre_que_sube_de_carpeta_devuelve_null_aunque_el_destino_exista()
    {
        Directory.CreateDirectory(Layout.Dir(AssetId, OutputFolder.Video));
        File.WriteAllText(Layout.Paths(AssetId).Manifest, "{}");

        Assert.Null(NewOutput().FindFile(AssetId, OutputFolder.Video, "../manifest.json"));
    }

    // Solo nombres simples: un subdirectorio tampoco vale, aunque quede dentro de la carpeta.
    [Fact]
    public void Nombre_con_subcarpeta_devuelve_null_aunque_el_destino_exista()
    {
        CreateFile(OutputFolder.Subs, Path.Combine("a", "b.vtt"));

        Assert.Null(NewOutput().FindFile(AssetId, OutputFolder.Subs, "a/b.vtt"));
    }

    // "." y ".." apuntan a carpetas que existen; "" a la carpeta misma. Ninguno es un archivo servible.
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    public void Nombre_que_no_es_de_archivo_devuelve_null(string fileName)
    {
        CreateFile(OutputFolder.Video, HlsLayout.InitName);

        Assert.Null(NewOutput().FindFile(AssetId, OutputFolder.Video, fileName));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
