namespace Symphony.Linear;

public sealed class LinearException : Exception
{
    public LinearException(string message)
        : base(message)
    {
    }

    public LinearException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

