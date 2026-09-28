namespace StreamMedia.Domain.DTO;

public class UpdateMediaDTO
{
    public string? title { get; set; }
    public string? kind { get; set; }
    public string? synopsis { get; set; }
    public int[]? ignored_audio { get; set; }
    public int[]? ignored_subtitles { get; set; }
}
