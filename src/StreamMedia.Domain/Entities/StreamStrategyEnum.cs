namespace StreamMedia.Domain.Entities;

public enum StreamStrategy
{
    Remux,      // H.264: reempaquetar sin recodificar
    Transcode   // cualquier otro codec
}