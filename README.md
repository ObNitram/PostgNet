# postgNet

`postgNet` est une bibliothèque .NET 10 destinée à la création d'API connectées
directement à PostgreSQL avec Npgsql.

## Utilisation de base

```csharp
await using var dataSource = PostgNet.PostgNetDataSource.Create(
    "Host=localhost;Database=my_database;Username=postgres;Password=postgres");

await using var command = dataSource.CreateCommand("SELECT now()");
var serverTime = await command.ExecuteScalarAsync();
```

Ne stockez pas une chaîne de connexion réelle dans le dépôt. Pour une API,
chargez-la depuis la configuration ou depuis un gestionnaire de secrets.

## PostgreSQL avec Docker

Une instance PostgreSQL 18 vide peut être lancée pour le développement local :

```shell
docker compose -f docker/compose.yml up -d
```

Paramètres par défaut :

- hôte : `localhost`
- port : `5432`
- base : `postgnet`
- utilisateur : `postgres`
- mot de passe : `postgres`

Ces valeurs peuvent être remplacées avec les variables `POSTGRES_PORT`,
`POSTGRES_DB`, `POSTGRES_USER` et `POSTGRES_PASSWORD`.

Pour vérifier la connexion depuis .NET :

```shell
dotnet run --project samples/PostgNet.SmokeTest
```

Le programme utilise les paramètres Docker par défaut. Une autre connexion peut
être testée en définissant la variable `POSTGNET_CONNECTION_STRING`.

## Formatage avec CSharpier

CSharpier est installé comme outil .NET local. Après avoir cloné le dépôt,
restaurez les outils avec :

```shell
dotnet tool restore
```

Pour formater le code et vérifier le formatage sans modifier les fichiers :

```shell
dotnet csharpier format .
dotnet csharpier check .
```
