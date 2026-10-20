namespace StreamMedia.Application.Media;

public sealed record UpdateMediaResult
{
	public bool IsNotFound { get; private init; }

	public Domain.Entities.Media? Media { get; private init; }

	public static UpdateMediaResult Updated(Domain.Entities.Media media) => new()
	{
		Media = media
	};

	public static UpdateMediaResult NotFound() => new()
	{
		IsNotFound = true
	};
}
