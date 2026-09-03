using Npgsql;

namespace PostgNet;

/// <summary>
/// Crée une source de données Npgsql réutilisable par une application API.
/// </summary>
public static class PostgNetDataSource
{
    /// <summary>
    /// Construit une source de données PostgreSQL à partir d'une chaîne de connexion.
    /// L'appelant est responsable de sa libération.
    /// </summary>
    public static NpgsqlDataSource Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new NpgsqlDataSourceBuilder(connectionString).Build();
    }
}
