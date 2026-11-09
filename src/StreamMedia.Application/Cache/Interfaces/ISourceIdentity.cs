namespace StreamMedia.Application.Cache.Interfaces;

// Mira el origen, no el cache: de (ruta, mtime, tamano) sale el id del asset.
public interface ISourceIdentity
{
    // Identidad (ruta, mtime, tamano). Lanza FileNotFoundException si el origen no existe.
    string AssetIdFor(string source);
}
