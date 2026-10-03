namespace StreamMedia.Application.MediaExceptions

/// <summary>Rechazo de admision: sin lugar (too_many_jobs) o sin espacio (storage_limit).</summary>
{
    public class CapacityException : Exception
    {
        public string Code { get; }

        public CapacityException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}