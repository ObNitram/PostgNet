using Microsoft.EntityFrameworkCore;
using PostgNet.Internal;

namespace PostgNet.Tests;

public sealed class QueryTranslationTests
{
    [Fact]
    public void Simple_anonymous_projection_builds_json_in_postgresql()
    {
        using var context = new TestDbContext();
        var query = context.Users.Select(user => new { user.Id, user.Name });

        var command = new PostgNetQueryTranslator(context).Translate(query);

        Assert.Contains("jsonb_build_object('Id'", command.CommandText);
        Assert.Contains("\"test\".\"app_users\" AS \"t0\"", command.CommandText);
        Assert.Contains("\"t0\".\"user_id\"", command.CommandText);
        Assert.Contains("\"t0\".\"display_name\"", command.CommandText);
        Assert.EndsWith("::text\nFROM \"test\".\"app_users\" AS \"t0\"", command.CommandText);
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void Member_initialized_dto_is_supported()
    {
        using var context = new TestDbContext();
        var query = context.Users.Select(user => new UserProjection
        {
            Id = user.Id,
            Name = user.Name,
        });

        var command = new PostgNetQueryTranslator(context).Translate(query);

        Assert.Contains("'Id', \"t0\".\"user_id\"", command.CommandText);
        Assert.Contains("'Name', \"t0\".\"display_name\"", command.CommandText);
    }

    [Fact]
    public void Equality_boolean_and_captured_values_are_parameterized()
    {
        using var context = new TestDbContext();
        var expectedName = "Ada";
        var query = context
            .Users.Where(user => user.Name == expectedName && user.IsActive)
            .Select(user => new { user.Id });

        var command = new PostgNetQueryTranslator(context).Translate(query);

        Assert.Contains("@p0", command.CommandText);
        Assert.DoesNotContain(expectedName, command.CommandText);
        Assert.Contains("= TRUE", command.CommandText);
        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(expectedName, parameter.Value);
    }

    [Fact]
    public void Nullable_inequality_preserves_dotnet_null_semantics()
    {
        using var context = new TestDbContext();
        var nickname = "ace";
        var query = context
            .Users.Where(user => user.Nickname != nickname)
            .Select(user => new { user.Id });

        var command = new PostgNetQueryTranslator(context).Translate(query);

        Assert.Contains("<> @p0", command.CommandText);
        Assert.Contains("IS NULL", command.CommandText);
    }

    [Fact]
    public void Null_equality_uses_is_null_without_parameter()
    {
        using var context = new TestDbContext();
        var query = context
            .Users.Where(user => user.Nickname == null)
            .Select(user => new { user.Id });

        var command = new PostgNetQueryTranslator(context).Translate(query);

        Assert.Contains("IS NULL", command.CommandText);
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void Ordered_pagination_uses_parameterized_offset_and_limit()
    {
        using var context = new TestDbContext();
        var skip = 2;
        var query = context
            .Users.OrderBy(user => user.Id)
            .Skip(skip)
            .Take(3)
            .Select(user => new { user.Id });

        var command = new PostgNetQueryTranslator(context).Translate(query);

        Assert.Contains("ORDER BY \"t0\".\"user_id\"", command.CommandText);
        Assert.Contains("OFFSET @p0", command.CommandText);
        Assert.Contains("LIMIT @p1", command.CommandText);
        Assert.Equal([2, 3], command.Parameters.Select(parameter => parameter.Value));
    }

    [Fact]
    public void Unsupported_method_reports_the_method_name()
    {
        using var context = new TestDbContext();
        var query = context
            .Users.Where(user => user.Name.StartsWith("A"))
            .Select(user => new { user.Id });

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("StartsWith", exception.Message);
    }

    [Fact]
    public void Skip_without_order_by_is_rejected()
    {
        using var context = new TestDbContext();
        var query = context.Users.Skip(1).Select(user => new { user.Id });

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("sans un 'OrderBy'", exception.Message);
    }

    [Fact]
    public void Query_without_object_select_is_rejected()
    {
        using var context = new TestDbContext();

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(context.Users)
        );

        Assert.Contains("Select", exception.Message);
    }

    [Fact]
    public void Scalar_projection_is_rejected()
    {
        using var context = new TestDbContext();
        var query = context.Users.Select(user => user.Name);

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("ne construisent pas un objet", exception.Message);
    }

    [Fact]
    public void Descending_order_is_rejected()
    {
        using var context = new TestDbContext();
        var query = context
            .Users.OrderByDescending(user => user.Id)
            .Select(user => new { user.Id });

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("OrderByDescending", exception.Message);
    }

    [Fact]
    public void Ef_query_modifier_is_rejected()
    {
        using var context = new TestDbContext();
        var query = context.Users.AsNoTracking().Select(user => new { user.Id });

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("AsNoTracking", exception.Message);
    }

    [Fact]
    public void Collection_without_order_by_is_rejected()
    {
        using var context = new TestDbContext();
        var query = context.Users.Select(user => new
        {
            Interventions = user.Interventions.Select(intervention => new { intervention.Id }),
        });

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("OrderBy(...).Select(...)", exception.Message);
    }

    [Fact]
    public void Global_query_filter_is_rejected_before_execution()
    {
        using var context = new FilteredTestDbContext();
        var query = context.Users.Select(user => new { user.Id });

        var exception = Assert.Throws<PostgNetQueryNotSupportedException>(() =>
            new PostgNetQueryTranslator(context).Translate(query)
        );

        Assert.Contains("filtres globaux", exception.Message);
    }
}
