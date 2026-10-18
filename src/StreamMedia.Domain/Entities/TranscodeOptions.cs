namespace StreamMedia.Domain.Entities;

// Con que se codifica y empaqueta un asset. Se guarda en el manifest: describe
// los segmentos que hay en disco, no la configuracion del server.
public sealed record TranscodeOptions
{
    public int HlsTime { get; init; } = 6;
    public int Crf { get; init; } = 23;
    public string Preset { get; init; } = "veryfast";
    public string VideoMaxBitrate { get; init; } = "8000k";
    public string VideoBufsize { get; init; } = "8000k";
    public int? VideoMaxHeight { get; init; } = 1080; // null = conservar la del origen
    public string VideoCodec { get; init; } = "h264";
    public string AudioBitrate { get; init; } = "128k";
    public int AudioChannels { get; init; } = 2;
    public bool ForceTranscode { get; init; }
    public bool TonemapHdr { get; init; } // HDR -> SDR; un manifest viejo sin el campo lee false

    // El video se puede reempaquetar sin recodificar: la estrategia elegida es remux.
    public bool CanCopyVideo(StreamStrategy strategy)
        => !ForceTranscode && strategy == StreamStrategy.Remux;

    // La pista se puede copiar tal cual: AAC con los canales de salida.
    public bool CanCopyAudio(AudioTrack track)
        => !ForceTranscode && track.Codec == "aac" && track.Channels == AudioChannels;
}