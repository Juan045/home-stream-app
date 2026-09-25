namespace StreamMedia.Application.Media;

public sealed record RegisterMediaResult
{
    public bool IsAlreadyExists { get; private init; }

    public Domain.Entities.Media? Media { get; private init; }
    
    public Guid? ExistingId  { get; private init; }

    public static RegisterMediaResult Created(Domain.Entities.Media media) => new()
    {
        Media = media
    };

    public static RegisterMediaResult AlreadyExists(Guid id) => new()
    {
        IsAlreadyExists = true,
        ExistingId = id
    };
}