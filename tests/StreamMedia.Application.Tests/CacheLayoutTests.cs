using StreamMedia.Application.Cache;

// Dir es el unico lugar que traduce (asset, carpeta) a ruta; AssetPaths es lo que van a ver los argumentos de FFmpeg.
// Si divergen, FFmpeg escribe en una carpeta y el resto de la app lee de otra.
public class CacheLayoutTests
{
    // Id con forma de hash, como los reales: uno trivial podria coincidir por casualidad con un nombre de carpeta.
    const string AssetId = "3f9a1c7e5b2d4f80";

    static readonly CacheLayout Layout = new("/cache");

    [Fact]
    public void Dir_de_video_coincide_con_AssetPaths()
    {
        Assert.Equal(Layout.Paths(AssetId).Video, Layout.Dir(AssetId, OutputFolder.Video));
    }

    // Cada pista de audio va en su propia rendition: el numero tiene que llegar a la ruta.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Dir_de_audio_coincide_con_AssetPaths(int track)
    {
        Assert.Equal(Layout.Paths(AssetId).Audio(track), Layout.Dir(AssetId, OutputFolder.Audio(track)));
    }

    [Fact]
    public void Dir_de_subs_coincide_con_AssetPaths()
    {
        Assert.Equal(Layout.Paths(AssetId).Subs, Layout.Dir(AssetId, OutputFolder.Subs));
    }

    // OutputFolder reemplaza a la ruta como clave: la igualdad por valor tiene que distinguir carpeta y pista.
    [Fact]
    public void OutputFolder_compara_por_carpeta_y_pista()
    {
        Assert.Equal(OutputFolder.Audio(0), OutputFolder.Audio(0));
        Assert.NotEqual(OutputFolder.Audio(0), OutputFolder.Audio(1));
        Assert.NotEqual(OutputFolder.Audio(0), OutputFolder.Video);
        Assert.NotEqual(OutputFolder.Video, OutputFolder.Subs);
    }
}
