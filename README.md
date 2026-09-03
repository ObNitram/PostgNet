# PostgNet

`PostgNet` traduit un sous-ensemble volontairement limité de requêtes LINQ EF Core
en une requête PostgreSQL unique. Les objets et tableaux JSON sont construits par
PostgreSQL avec `jsonb_build_object` et `jsonb_agg`, puis lus comme une seule valeur
texte. La bibliothèque ne reconstruit jamais le résultat côté .NET.

## Requête JSON

```csharp
using PostgNet;

var query = dbContext.Users
    .Where(user => user.IsActive)
    .OrderBy(user => user.Id)
    .Take(20)
    .Select(user => new
    {
        user.Id,
        user.Name,
        Interventions = user.Interventions
            .OrderBy(intervention => intervention.Id)
            .Select(intervention => new
            {
                intervention.Id,
                intervention.Title,
                Machine = new
                {
                    intervention.Machine.Id,
                    intervention.Machine.Name,
                },
            }),
    });

string json = await query.ToPostgNetJsonAsync(dbContext, cancellationToken);
```

Le résultat est toujours un tableau JSON. Une requête vide retourne `[]`.

## Sous-ensemble LINQ du MVP

La requête doit suivre cette forme :

```text
DbSet → Where* → OrderBy? → Skip? → Take? → Select
```

Fonctionnalités prises en charge :

- projection objet explicite avec un type anonyme ou un DTO initialisé par propriétés ;
- propriétés scalaires mappées par EF Core ;
- `Where` avec `==`, `!=`, valeurs booléennes, `null`, `&&`, `||` et `!` ;
- valeurs constantes ou capturées, toujours envoyées comme paramètres SQL ;
- un unique `OrderBy` ascendant sur une colonne directe ;
- `Skip` et `Take` après un `OrderBy`, avec des valeurs supérieures ou égales à zéro ;
- navigations requises à clés simples sur deux sauts au maximum ;
- collections sous la forme `OrderBy(...).Select(...)`.

Tout autre opérateur, calcul, appel de méthode, projection scalaire ou mapping avancé
provoque une `PostgNetQueryNotSupportedException` avant l'exécution. Cela inclut
notamment les navigations optionnelles, clés composites, filtres globaux EF Core,
héritages, mappings multi-tables, `Include`, `AsNoTracking`, `OrderByDescending`,
`ThenBy`, `Distinct`, `GroupBy`, les agrégats et les filtres ou paginations imbriqués.

L'ordre du tableau racine n'est pas garanti sans `OrderBy`.

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

## Tests

Les tests unitaires ne nécessitent pas Docker :

```shell
dotnet test tests/PostgNet.Tests
```

Les tests d'intégration lancent automatiquement une instance PostgreSQL 18 jetable
avec Testcontainers et nécessitent un démon Docker disponible :

```shell
dotnet test tests/PostgNet.IntegrationTests
```

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
