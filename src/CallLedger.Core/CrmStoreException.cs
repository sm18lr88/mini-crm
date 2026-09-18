namespace CallLedger.Core;

public sealed class CrmStoreException : Exception
{
    public CrmStoreException(string message)
        : base(message)
    {
    }

    public CrmStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
