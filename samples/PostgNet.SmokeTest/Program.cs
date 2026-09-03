using PostgNet;

const string defaultConnectionString =
    "Host=localhost;Port=5432;Database=postgnet;Username=postgres;Password=postgres";

var connectionString = Environment.GetEnvironmentVariable("POSTGNET_CONNECTION_STRING")
    ?? defaultConnectionString;

try
{
    await using var dataSource = PostgNetDataSource.Create(connectionString);
    await using var command = dataSource.CreateCommand(
        "SELECT current_database(), version()");
    await using var reader = await command.ExecuteReaderAsync();

    await reader.ReadAsync();
    Console.WriteLine($"Connexion réussie à la base '{reader.GetString(0)}'.");
    Console.WriteLine(reader.GetString(1));

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Connexion PostgreSQL impossible : {exception.Message}");
    return 1;
}
