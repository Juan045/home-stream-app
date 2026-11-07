using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Infrastructure.Cache;

public class AssetStorePrepareBuildTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    AssetStore NewStore()
        => new(new CacheOptions { Dir = _root }, TimeProvider.System, NullLogger<AssetStore>.Instance);

    string DirectoryWith(string name, params string[] files)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(directory, file), "");
        return directory;
    }

    // Un build relanzado con otro codec no puede dejar segmentos del anterior.
    [Fact]
    public void Borra_los_segmentos_y_la_playlist_del_build_anterior()
    {
        var video = DirectoryWith("video", "seg-00000.m4s", "seg-00001.m4s", "internal.m3u8");
        var store = NewStore();

        store.PrepareBuild(video);

        Assert.False(File.Exists(Path.Combine(video, "seg-00000.m4s")));
        Assert.False(File.Exists(Path.Combine(video, "seg-00001.m4s")));
        Assert.False(File.Exists(Path.Combine(video, "internal.m3u8")));
        Assert.Equal(0, store.CountSegments(video));
    }

    // Los subtitulos comparten carpeta y tambien pasan por PrepareBuild.
    [Fact]
    public void Conserva_los_subtitulos()
    {
        var subs = DirectoryWith("subs", "sub_0_spa.vtt", "sub_1_eng.vtt");

        NewStore().PrepareBuild(subs);

        Assert.True(File.Exists(Path.Combine(subs, "sub_0_spa.vtt")));
        Assert.True(File.Exists(Path.Combine(subs, "sub_1_eng.vtt")));
    }

    // Relanzar el video no puede llevarse el audio, que ya se termino y no se rehace.
    [Fact]
    public void No_toca_los_segmentos_de_otra_carpeta()
    {
        var video = DirectoryWith("video", "seg-00000.m4s");
        var audio = DirectoryWith(Path.Combine("audio", "0"), "seg-00000.m4s");

        NewStore().PrepareBuild(video);

        Assert.True(File.Exists(Path.Combine(audio, "seg-00000.m4s")));
    }

    [Fact]
    public void Crea_la_carpeta_si_no_existe()
    {
        var missing = Path.Combine(_root, "nueva", "video");

        NewStore().PrepareBuild(missing);

        Assert.True(Directory.Exists(missing));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
