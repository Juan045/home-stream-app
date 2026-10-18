using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// Perfiles de codificacion con nombre. Un perfil es un TranscodeOptions ya armado:
// hay un solo perfil (la biblioteca) y lo unico que depende de la fuente (el tonemap)
// lo ajusta quien lo usa con "with", asi que no hay factory ni interfaz; agregar un
// perfil es agregar un campo aca.
public static class TranscodeProfiles
{
    /// <summary>
    /// Perfil de la codificacion asincrona de biblioteca: AV1 por SVT-AV1, para
    /// reprocesar algo que se va a ver muchas veces (el encode es lento y consume CPU).
    /// </summary>
    /// <remarks>
    /// Por que se fuerza el transcode: sin <see cref="TranscodeOptions.ForceTranscode"/>
    /// una pelicula que ya viene en H.264 se copiaria con <c>-c:v copy</c> en vez de
    /// codificarse; el perfil quedaria a merced del codec del origen y no produciria AV1.
    ///
    /// Por que el tope de altura: el perfil fija <see cref="TranscodeOptions.VideoMaxHeight"/>
    /// en 1080 para que la copia que se guarda para siempre no arrastre un 4K. El
    /// <see cref="TranscodeOptions.Crf"/> de 24 con preset 6 prioriza calidad sobre peso:
    /// es la copia que queda; la salida es de 10 bits.
    ///
    /// Por que no fija el tonemap: <see cref="TranscodeOptions.TonemapHdr"/> depende de la
    /// fuente (HDR siempre se entrega como SDR), lo decide <see cref="EncodeMediaUseCase"/>.
    /// </remarks>
    public static readonly TranscodeOptions Archive = new()
    {
        VideoCodec = "av1",
        Crf = 24,
        Preset = "6",
        VideoMaxHeight = 1080,
        ForceTranscode = true,
    };
}
