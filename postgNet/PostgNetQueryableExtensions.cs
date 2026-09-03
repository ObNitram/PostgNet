using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PostgNet.Internal;

namespace PostgNet;

/// <summary>
/// Extensions permettant d'exécuter une requête EF Core sous forme de JSON construit par PostgreSQL.
/// </summary>
public static class PostgNetQueryableExtensions
{
    /// <summary>
    /// Traduit une projection EF Core prise en charge en une unique requête PostgreSQL et retourne
    /// le tableau JSON produit par la base de données.
    /// </summary>
    public static async Task<string> ToPostgNetJsonAsync<T>(
        this IQueryable<T> source,
        DbContext context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        var commandDefinition = new PostgNetQueryTranslator(context).Translate(source);
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;

        if (shouldClose)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandDefinition.CommandText;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

            if (context.Database.GetCommandTimeout() is { } commandTimeout)
            {
                command.CommandTimeout = commandTimeout;
            }

            foreach (var parameterDefinition in commandDefinition.Parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = parameterDefinition.Name;
                parameter.Value = parameterDefinition.Value;
                command.Parameters.Add(parameter);
            }

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result as string
                ?? throw new InvalidOperationException(
                    "PostgreSQL n'a pas retourné la colonne JSON texte attendue."
                );
        }
        finally
        {
            if (shouldClose)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }
}
