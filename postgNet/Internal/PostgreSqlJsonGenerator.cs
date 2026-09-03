using System.Text;

namespace PostgNet.Internal;

internal sealed class PostgreSqlJsonGenerator
{
    private readonly StringBuilder _sql = new();

    public string Generate(SqlSelectQuery query)
    {
        RenderAggregateQuery(query, returnText: true, nested: false);
        return _sql.ToString();
    }

    private void RenderAggregateQuery(SqlSelectQuery query, bool returnText, bool nested)
    {
        if (nested)
        {
            _sql.Append('(');
        }

        if (query.Offset is not null || query.Limit is not null)
        {
            RenderPagedAggregate(query, returnText);
        }
        else
        {
            _sql.Append("SELECT COALESCE(jsonb_agg(");
            RenderExpression(query.Projection);
            RenderAggregateOrdering(query.Orderings);
            _sql.Append("), '[]'::jsonb)");
            if (returnText)
            {
                _sql.Append("::text");
            }

            RenderFromWhere(query);
        }

        if (nested)
        {
            _sql.Append(')');
        }
    }

    private void RenderPagedAggregate(SqlSelectQuery query, bool returnText)
    {
        _sql.Append("SELECT COALESCE(jsonb_agg(\"__postgnet_rows\".\"value\"");
        for (var index = 0; index < query.Orderings.Count; index++)
        {
            _sql.Append(index == 0 ? " ORDER BY " : ", ");
            _sql.Append("\"__postgnet_rows\".\"order");
            _sql.Append(index);
            _sql.Append('"');
        }

        _sql.Append("), '[]'::jsonb)");
        if (returnText)
        {
            _sql.Append("::text");
        }

        _sql.AppendLine();
        _sql.Append("FROM (");
        _sql.AppendLine();
        _sql.Append("  SELECT ");
        RenderExpression(query.Projection);
        _sql.Append(" AS \"value\"");
        for (var index = 0; index < query.Orderings.Count; index++)
        {
            _sql.Append(", ");
            RenderExpression(query.Orderings[index].Expression);
            _sql.Append(" AS \"order");
            _sql.Append(index);
            _sql.Append('"');
        }

        RenderFromWhere(query, indent: "  ");
        _sql.AppendLine();
        _sql.Append("  ORDER BY ");
        RenderOrderings(query.Orderings);

        if (query.Offset is not null)
        {
            _sql.AppendLine();
            _sql.Append("  OFFSET ");
            RenderExpression(query.Offset);
        }

        if (query.Limit is not null)
        {
            _sql.AppendLine();
            _sql.Append("  LIMIT ");
            RenderExpression(query.Limit);
        }

        _sql.AppendLine();
        _sql.Append(") AS \"__postgnet_rows\"");
    }

    private void RenderFromWhere(SqlSelectQuery query, string indent = "")
    {
        _sql.AppendLine();
        _sql.Append(indent);
        _sql.Append("FROM ");
        RenderTable(query.From);

        foreach (var join in query.Joins)
        {
            _sql.AppendLine();
            _sql.Append(indent);
            _sql.Append("INNER JOIN ");
            RenderTable(join.Table);
            _sql.Append(" ON ");
            RenderExpression(join.Predicate);
        }

        if (query.Predicate is not null)
        {
            _sql.AppendLine();
            _sql.Append(indent);
            _sql.Append("WHERE ");
            RenderExpression(query.Predicate);
        }
    }

    private void RenderExpression(SqlExpression expression)
    {
        switch (expression)
        {
            case SqlColumnExpression column:
                RenderIdentifier(column.TableAlias);
                _sql.Append('.');
                RenderIdentifier(column.ColumnName);
                break;
            case SqlParameterExpression parameter:
                _sql.Append('@');
                _sql.Append(parameter.Parameter.Name);
                break;
            case SqlBooleanExpression boolean:
                _sql.Append(boolean.Value ? "TRUE" : "FALSE");
                break;
            case SqlBinaryExpression binary:
                _sql.Append('(');
                RenderExpression(binary.Left);
                _sql.Append(
                    binary.Operator switch
                    {
                        SqlBinaryOperator.Equal => " = ",
                        SqlBinaryOperator.NotEqual => " <> ",
                        SqlBinaryOperator.And => " AND ",
                        SqlBinaryOperator.Or => " OR ",
                        _ => throw new ArgumentOutOfRangeException(nameof(binary.Operator)),
                    }
                );
                RenderExpression(binary.Right);
                _sql.Append(')');
                break;
            case SqlNotExpression not:
                _sql.Append("NOT (");
                RenderExpression(not.Operand);
                _sql.Append(')');
                break;
            case SqlNullTestExpression nullTest:
                _sql.Append('(');
                RenderExpression(nullTest.Operand);
                _sql.Append(nullTest.Negated ? " IS NOT NULL)" : " IS NULL)");
                break;
            case SqlJsonObjectExpression jsonObject:
                _sql.Append("jsonb_build_object(");
                for (var index = 0; index < jsonObject.Properties.Count; index++)
                {
                    if (index > 0)
                    {
                        _sql.Append(", ");
                    }

                    RenderStringLiteral(jsonObject.Properties[index].Name);
                    _sql.Append(", ");
                    RenderExpression(jsonObject.Properties[index].Value);
                }

                _sql.Append(')');
                break;
            case SqlJsonCollectionExpression collection:
                RenderAggregateQuery(collection.Query, returnText: false, nested: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expression), expression, null);
        }
    }

    private void RenderAggregateOrdering(IReadOnlyList<SqlOrdering> orderings)
    {
        if (orderings.Count == 0)
        {
            return;
        }

        _sql.Append(" ORDER BY ");
        RenderOrderings(orderings);
    }

    private void RenderOrderings(IReadOnlyList<SqlOrdering> orderings)
    {
        for (var index = 0; index < orderings.Count; index++)
        {
            if (index > 0)
            {
                _sql.Append(", ");
            }

            RenderExpression(orderings[index].Expression);
        }
    }

    private void RenderTable(SqlTable table)
    {
        if (!string.IsNullOrEmpty(table.Schema))
        {
            RenderIdentifier(table.Schema);
            _sql.Append('.');
        }

        RenderIdentifier(table.Name);
        _sql.Append(" AS ");
        RenderIdentifier(table.Alias);
    }

    private void RenderIdentifier(string identifier)
    {
        _sql.Append('"');
        _sql.Append(identifier.Replace("\"", "\"\"", StringComparison.Ordinal));
        _sql.Append('"');
    }

    private void RenderStringLiteral(string value)
    {
        _sql.Append('\'');
        _sql.Append(value.Replace("'", "''", StringComparison.Ordinal));
        _sql.Append('\'');
    }
}
