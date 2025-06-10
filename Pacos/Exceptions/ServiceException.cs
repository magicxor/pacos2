namespace Pacos.Exceptions;

public sealed class ServiceException : Exception
{
    public IReadOnlyDictionary<string, string>? Details { get; }

    public ServiceException(string message, IReadOnlyDictionary<string, string>? details = null) : base(message)
    {
        Details = details;
    }

    public ServiceException(string message, Exception innerException, IReadOnlyDictionary<string, string>? details = null) : base(message, innerException)
    {
        Details = details;
    }
}
