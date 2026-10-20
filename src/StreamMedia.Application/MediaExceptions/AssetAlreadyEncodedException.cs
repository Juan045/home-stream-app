namespace StreamMedia.Application.MediaExceptions;

/// <summary>El asset ya existe con el video despachado: recodificarlo exige borrarlo del cache (409).</summary>
public sealed class AssetAlreadyEncodedException(string message) : Exception(message)
{
    public string Code => "asset_already_encoded";
}
