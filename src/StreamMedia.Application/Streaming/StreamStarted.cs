using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

public sealed record StreamStarted(Asset Asset, Session Session, bool Playable);