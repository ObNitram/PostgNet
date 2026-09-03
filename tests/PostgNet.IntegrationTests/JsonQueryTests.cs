using System.Text.Json.Nodes;

namespace PostgNet.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class JsonQueryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Simple_projection_is_built_by_postgresql()
    {
        await using var context = fixture.CreateContext();
        var query = context
            .Users.OrderBy(user => user.Id)
            .Select(user => new { user.Id, user.Name });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson(
            """
            [
              { "Id": 1, "Name": "Ada" },
              { "Id": 2, "Name": "Grace" },
              { "Id": 3, "Name": "Linus" }
            ]
            """,
            json
        );
    }

    [Fact]
    public async Task Empty_query_returns_an_empty_array()
    {
        await using var context = fixture.CreateContext();
        var missingName = "Nobody";
        var query = context
            .Users.Where(user => user.Name == missingName)
            .Select(user => new { user.Id });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson("[]", json);
    }

    [Fact]
    public async Task Equality_boolean_and_nullable_inequality_are_executed_in_sql()
    {
        await using var context = fixture.CreateContext();
        var excludedNickname = "Lin";
        var query = context
            .Users.Where(user => user.IsActive && user.Nickname != excludedNickname)
            .OrderBy(user => user.Id)
            .Select(user => new { user.Id, user.Name });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson("""[{ "Id": 1, "Name": "Ada" }]""", json);
    }

    [Fact]
    public async Task Ordered_skip_and_take_are_applied_before_json_aggregation()
    {
        await using var context = fixture.CreateContext();
        var query = context
            .Users.OrderBy(user => user.Id)
            .Skip(1)
            .Take(1)
            .Select(user => new { user.Id, user.Name });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson("""[{ "Id": 2, "Name": "Grace" }]""", json);
    }

    [Fact]
    public async Task One_to_many_navigation_is_a_correlated_json_array()
    {
        await using var context = fixture.CreateContext();
        var query = context
            .Users.OrderBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                Interventions = user
                    .Interventions.OrderBy(item => item.Id)
                    .Select(item => new { item.Id, item.Title }),
            });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson(
            """
            [
              {
                "Id": 1,
                "Interventions": [
                  { "Id": 100, "Title": "Inspect" },
                  { "Id": 101, "Title": "Repair" }
                ]
              },
              {
                "Id": 2,
                "Interventions": [{ "Id": 102, "Title": "Calibrate" }]
              },
              { "Id": 3, "Interventions": [] }
            ]
            """,
            json
        );
    }

    [Fact]
    public async Task Many_to_one_navigation_is_a_nested_json_object()
    {
        await using var context = fixture.CreateContext();
        var query = context
            .Interventions.OrderBy(item => item.Id)
            .Select(item => new { item.Id, Machine = new { item.Machine.Id, item.Machine.Name } });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson(
            """
            [
              { "Id": 100, "Machine": { "Id": 10, "Name": "Lathe" } },
              { "Id": 101, "Machine": { "Id": 20, "Name": "Press" } },
              { "Id": 102, "Machine": { "Id": 10, "Name": "Lathe" } }
            ]
            """,
            json
        );
    }

    [Fact]
    public async Task Two_navigation_hops_are_rendered_in_one_json_query()
    {
        await using var context = fixture.CreateContext();
        var query = context
            .Users.OrderBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                Interventions = user
                    .Interventions.OrderBy(item => item.Id)
                    .Select(item => new
                    {
                        item.Id,
                        Machine = new { item.Machine.Id, item.Machine.Name },
                    }),
            });

        var json = await query.ToPostgNetJsonAsync(context);

        AssertJson(
            """
            [
              {
                "Id": 1,
                "Interventions": [
                  { "Id": 100, "Machine": { "Id": 10, "Name": "Lathe" } },
                  { "Id": 101, "Machine": { "Id": 20, "Name": "Press" } }
                ]
              },
              {
                "Id": 2,
                "Interventions": [
                  { "Id": 102, "Machine": { "Id": 10, "Name": "Lathe" } }
                ]
              },
              { "Id": 3, "Interventions": [] }
            ]
            """,
            json
        );
    }

    private static void AssertJson(string expected, string actual)
    {
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(actual);
        Assert.True(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"Expected: {expected}{Environment.NewLine}Actual: {actual}"
        );
    }
}
