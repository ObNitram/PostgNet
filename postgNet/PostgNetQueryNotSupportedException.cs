namespace PostgNet;

/// <summary>
/// Signale qu'une requête contient une construction qui ne fait pas partie du sous-ensemble LINQ
/// pris en charge par PostgNet.
/// </summary>
public sealed class PostgNetQueryNotSupportedException : NotSupportedException
{
    public PostgNetQueryNotSupportedException(string message)
        : base(message) { }
}
