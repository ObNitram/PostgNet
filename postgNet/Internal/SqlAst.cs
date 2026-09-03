namespace PostgNet.Internal;

internal sealed record SqlTable(string? Schema, string Name, string Alias);

internal sealed record SqlJoin(SqlTable Table, SqlExpression Predicate);

internal sealed record SqlOrdering(SqlColumnExpression Expression);

internal sealed record SqlParameterValue(string Name, object Value);

internal sealed record SqlSelectQuery(
    SqlTable From,
    IReadOnlyList<SqlJoin> Joins,
    SqlJsonObjectExpression Projection,
    SqlExpression? Predicate,
    IReadOnlyList<SqlOrdering> Orderings,
    SqlParameterExpression? Offset,
    SqlParameterExpression? Limit
);

internal abstract record SqlExpression;

internal sealed record SqlColumnExpression(string TableAlias, string ColumnName) : SqlExpression;

internal sealed record SqlParameterExpression(SqlParameterValue Parameter) : SqlExpression;

internal sealed record SqlBooleanExpression(bool Value) : SqlExpression;

internal sealed record SqlBinaryExpression(
    SqlExpression Left,
    SqlBinaryOperator Operator,
    SqlExpression Right
) : SqlExpression;

internal sealed record SqlNotExpression(SqlExpression Operand) : SqlExpression;

internal sealed record SqlNullTestExpression(SqlExpression Operand, bool Negated) : SqlExpression;

internal sealed record SqlJsonObjectExpression(IReadOnlyList<SqlJsonProperty> Properties)
    : SqlExpression;

internal sealed record SqlJsonProperty(string Name, SqlExpression Value);

internal sealed record SqlJsonCollectionExpression(SqlSelectQuery Query) : SqlExpression;

internal enum SqlBinaryOperator
{
    Equal,
    NotEqual,
    And,
    Or,
}

internal sealed record PostgNetCommand(
    string CommandText,
    IReadOnlyList<SqlParameterValue> Parameters
);
