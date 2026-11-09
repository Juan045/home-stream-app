using StreamMedia.Application.Cache;
using StreamMedia.Infrastructure.Cache;

public class HlsOutputPrepareBuildTests : IDisposable
{
    const string AssetId = "asset";

    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    CacheLayout Layout => new(_root);

    HlsOutput NewStore() => new(Layout);

    // Los archivos van donde HlsOutput los busca: la carpeta sale del mismo layout.
    string DirectoryWith(OutputFolder folder, params string[] files)
    {
        var directory = Layout.Dir(AssetId, folder);
        Directory.CreateDirectory(directory);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(directory, file), "");
        return directory;
    }

    // Un build relanzado con otro codec no puede dejar segmentos del anterior.
    [Fact]
    public void Borra_los_segmentos_y_la_playlist_del_build_anterior()
    {
        var video = DirectoryWith(OutputFolder.Video, "seg-00000.m4s", "seg-00001.m4s", "internal.m3u8");
        var store = NewStore();

        store.PrepareBuild(AssetId, OutputFolder.Video);

        Assert.False(File.Exists(Path.Combine(video, "seg-00000.m4s")));
        Assert.False(File.Exists(Path.Combine(video, "seg-00001.m4s")));
        Assert.False(File.Exists(Path.Combine(video, "internal.m3u8")));
        Assert.Equal(0, store.CountSegments(AssetId, OutputFolder.Video));
    }

    // Los subtitulos comparten carpeta y tambien pasan por PrepareBuild.
    [Fact]
    public void Conserva_los_subtitulos()
    {
        var subs = DirectoryWith(OutputFolder.Subs, "sub_0_spa.vtt", "sub_1_eng.vtt");

        NewStore().PrepareBuild(AssetId, OutputFolder.Subs);

        Assert.True(File.Exists(Path.Combine(subs, "sub_0_spa.vtt")));
        Assert.True(File.Exists(Path.Combine(subs, "sub_1_eng.vtt")));
    }

    // Relanzar el video no puede llevarse el audio, que ya se termino y no se rehace.
    [Fact]
    public void No_toca_los_segmentos_de_otra_carpeta()
    {
        DirectoryWith(OutputFolder.Video, "seg-00000.m4s");
        var audio = DirectoryWith(OutputFolder.Audio(0), "seg-00000.m4s");

        NewStore().PrepareBuild(AssetId, OutputFolder.Video);

        Assert.True(File.Exists(Path.Combine(audio, "seg-00000.m4s")));
    }

    [Fact]
    public void Crea_la_carpeta_si_no_existe()
    {
        var missing = Layout.Dir("nueva", OutputFolder.Video);

        NewStore().PrepareBuild("nueva", OutputFolder.Video);

        Assert.True(Directory.Exists(missing));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
