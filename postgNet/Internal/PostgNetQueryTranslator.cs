using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace PostgNet.Internal;

internal sealed class PostgNetQueryTranslator
{
    private readonly DbContext _context;
    private readonly List<SqlParameterValue> _parameters = [];
    private int _nextAlias;

    public PostgNetQueryTranslator(DbContext context)
    {
        _context = context;
    }

    public PostgNetCommand Translate(IQueryable source)
    {
        if (_context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            throw new PostgNetQueryNotSupportedException(
                "PostgNet prend uniquement en charge le provider EF Core Npgsql pour PostgreSQL."
            );
        }

        var (root, operations) = ReadRootOperations(source.Expression);
        var entityType = ResolveRootEntityType(root);
        ValidateEntityType(entityType);

        var builder = new QueryBuilder(this, CreateTable(entityType), entityType);
        var sawOrderBy = false;
        var sawSkip = false;
        var sawTake = false;
        var sawSelect = false;

        foreach (var operation in operations)
        {
            if (operation.Method.DeclaringType != typeof(Queryable))
            {
                throw Unsupported(operation, $"l'opérateur '{operation.Method.Name}'");
            }

            switch (operation.Method.Name)
            {
                case nameof(Queryable.Where) when !sawOrderBy && !sawSkip && !sawTake && !sawSelect:
                {
                    var lambda = ReadLambda(operation, expectedParameterCount: 1);
                    var scope = new TranslationScope(
                        lambda.Parameters[0],
                        entityType,
                        builder.From.Alias,
                        0
                    );
                    builder.AddPredicate(TranslatePredicate(lambda.Body, scope, builder));
                    break;
                }
                case nameof(Queryable.OrderBy)
                    when !sawOrderBy && !sawSkip && !sawTake && !sawSelect:
                {
                    var lambda = ReadLambda(operation, expectedParameterCount: 1);
                    var scope = new TranslationScope(
                        lambda.Parameters[0],
                        entityType,
                        builder.From.Alias,
                        0
                    );
                    builder.Orderings.Add(
                        new SqlOrdering(TranslateDirectScalar(lambda.Body, scope, "OrderBy"))
                    );
                    sawOrderBy = true;
                    break;
                }
                case nameof(Queryable.Skip) when sawOrderBy && !sawSkip && !sawTake && !sawSelect:
                    builder.Offset = CreatePaginationParameter(operation.Arguments[1], "Skip");
                    sawSkip = true;
                    break;
                case nameof(Queryable.Take) when sawOrderBy && !sawTake && !sawSelect:
                    builder.Limit = CreatePaginationParameter(operation.Arguments[1], "Take");
                    sawTake = true;
                    break;
                case nameof(Queryable.Select) when !sawSelect && operation == operations[^1]:
                {
                    var lambda = ReadLambda(operation, expectedParameterCount: 1);
                    var scope = new TranslationScope(
                        lambda.Parameters[0],
                        entityType,
                        builder.From.Alias,
                        0
                    );
                    builder.Projection = TranslateJsonObject(lambda.Body, scope, builder);
                    sawSelect = true;
                    break;
                }
                case nameof(Queryable.Skip) or nameof(Queryable.Take) when !sawOrderBy:
                    throw Unsupported(
                        operation,
                        $"'{operation.Method.Name}' sans un 'OrderBy' préalable"
                    );
                default:
                    throw Unsupported(
                        operation,
                        $"l'opérateur ou l'ordre '{operation.Method.Name}'"
                    );
            }
        }

        if (!sawSelect)
        {
            throw new PostgNetQueryNotSupportedException(
                "La requête doit se terminer par Select avec une projection objet explicite."
            );
        }

        var ast = builder.Build();
        var sql = new PostgreSqlJsonGenerator().Generate(ast);
        return new PostgNetCommand(sql, _parameters);
    }

    private (
        EntityQueryRootExpression Root,
        IReadOnlyList<MethodCallExpression> Operations
    ) ReadRootOperations(Expression expression)
    {
        var operations = new List<MethodCallExpression>();
        var current = expression;

        while (
            current is MethodCallExpression call
            && call.Arguments.Count > 0
            && typeof(IQueryable).IsAssignableFrom(call.Arguments[0].Type)
        )
        {
            operations.Add(call);
            current = call.Arguments[0];
        }

        operations.Reverse();

        if (current is not EntityQueryRootExpression root)
        {
            throw new PostgNetQueryNotSupportedException(
                $"La racine de requête '{current.NodeType}' n'est pas une racine d'entité EF Core prise en charge."
            );
        }

        return (root, operations);
    }

    private IEntityType ResolveRootEntityType(EntityQueryRootExpression root)
    {
        var entityType = _context.Model.FindEntityType(root.EntityType.Name);
        if (entityType is null || entityType.ClrType != root.EntityType.ClrType)
        {
            throw new ArgumentException(
                "Le DbContext fourni ne correspond pas au modèle de la requête.",
                nameof(_context)
            );
        }

        return entityType;
    }

    private void ValidateEntityType(IEntityType entityType)
    {
        if (entityType.IsOwned())
        {
            throw Unsupported(entityType, "les types possédés");
        }

        if (entityType.BaseType is not null || entityType.GetDerivedTypes().Any())
        {
            throw Unsupported(entityType, "l'héritage d'entités");
        }

        if (entityType.GetDeclaredQueryFilters().Any())
        {
            throw Unsupported(entityType, "les filtres globaux EF Core");
        }

        if (entityType.GetMappingFragments(StoreObjectType.Table).Any())
        {
            throw Unsupported(entityType, "les mappings d'entité répartis sur plusieurs tables");
        }

        if (entityType.GetTableName() is null)
        {
            throw Unsupported(entityType, "les entités qui ne sont pas mappées sur une table");
        }

        if (entityType.FindPrimaryKey()?.Properties.Count != 1)
        {
            throw Unsupported(entityType, "les entités sans clé simple à une colonne");
        }
    }

    private SqlJsonObjectExpression TranslateJsonObject(
        Expression expression,
        TranslationScope scope,
        QueryBuilder builder
    )
    {
        expression = StripConvert(expression);
        var properties = new List<SqlJsonProperty>();

        if (expression is NewExpression { Members: not null } newExpression)
        {
            for (var index = 0; index < newExpression.Arguments.Count; index++)
            {
                properties.Add(
                    new SqlJsonProperty(
                        newExpression.Members[index].Name,
                        TranslateProjectionValue(newExpression.Arguments[index], scope, builder)
                    )
                );
            }
        }
        else if (expression is MemberInitExpression memberInit)
        {
            foreach (var binding in memberInit.Bindings)
            {
                if (binding is not MemberAssignment assignment)
                {
                    throw Unsupported(binding, "les liaisons DTO autres qu'une affectation simple");
                }

                properties.Add(
                    new SqlJsonProperty(
                        assignment.Member.Name,
                        TranslateProjectionValue(assignment.Expression, scope, builder)
                    )
                );
            }
        }
        else
        {
            throw Unsupported(expression, "les projections qui ne construisent pas un objet");
        }

        if (properties.Count == 0)
        {
            throw Unsupported(expression, "les projections objet vides");
        }

        return new SqlJsonObjectExpression(properties);
    }

    private SqlExpression TranslateProjectionValue(
        Expression expression,
        TranslationScope scope,
        QueryBuilder builder
    )
    {
        expression = StripConvert(expression);

        if (expression is NewExpression or MemberInitExpression)
        {
            return TranslateJsonObject(expression, scope, builder);
        }

        if (
            expression is MethodCallExpression methodCall
            && IsEnumerableMethod(methodCall, "Select")
        )
        {
            return TranslateCollection(methodCall, scope);
        }

        if (TryTranslateScalar(expression, scope, builder, out var column, out _))
        {
            return column;
        }

        throw Unsupported(expression, "la valeur projetée");
    }

    private SqlJsonCollectionExpression TranslateCollection(
        MethodCallExpression select,
        TranslationScope parentScope
    )
    {
        var selector = ReadLambda(select, expectedParameterCount: 1);
        if (
            select.Arguments[0] is not MethodCallExpression orderBy
            || !IsEnumerableMethod(orderBy, "OrderBy")
        )
        {
            throw Unsupported(
                select,
                "une collection de navigation sans la forme OrderBy(...).Select(...)"
            );
        }

        var navigation = ResolveDirectCollectionNavigation(orderBy.Arguments[0], parentScope);
        if (parentScope.NavigationDepth + 1 > 2)
        {
            throw Unsupported(select, "une navigation d'une profondeur supérieure à deux");
        }

        ValidateCollectionNavigation(navigation);
        var childEntityType = navigation.TargetEntityType;
        ValidateEntityType(childEntityType);

        var childTable = CreateTable(childEntityType);
        var childBuilder = new QueryBuilder(this, childTable, childEntityType);
        childBuilder.AddPredicate(CreateCollectionCorrelation(navigation, parentScope, childTable));

        var orderLambda = ReadLambda(orderBy, expectedParameterCount: 1);
        var orderScope = new TranslationScope(
            orderLambda.Parameters[0],
            childEntityType,
            childTable.Alias,
            parentScope.NavigationDepth + 1
        );
        childBuilder.Orderings.Add(
            new SqlOrdering(TranslateDirectScalar(orderLambda.Body, orderScope, "OrderBy imbriqué"))
        );

        var selectorScope = new TranslationScope(
            selector.Parameters[0],
            childEntityType,
            childTable.Alias,
            parentScope.NavigationDepth + 1
        );
        childBuilder.Projection = TranslateJsonObject(selector.Body, selectorScope, childBuilder);

        return new SqlJsonCollectionExpression(childBuilder.Build());
    }

    private SqlExpression TranslatePredicate(
        Expression expression,
        TranslationScope scope,
        QueryBuilder builder
    )
    {
        expression = StripConvert(expression);

        if (expression is BinaryExpression binary)
        {
            if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
            {
                return new SqlBinaryExpression(
                    TranslatePredicate(binary.Left, scope, builder),
                    binary.NodeType == ExpressionType.AndAlso
                        ? SqlBinaryOperator.And
                        : SqlBinaryOperator.Or,
                    TranslatePredicate(binary.Right, scope, builder)
                );
            }

            if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                return TranslateEquality(binary, scope, builder);
            }

            throw Unsupported(binary, $"l'opérateur de comparaison '{binary.NodeType}'");
        }

        if (expression is UnaryExpression { NodeType: ExpressionType.Not } unary)
        {
            return new SqlNotExpression(TranslatePredicate(unary.Operand, scope, builder));
        }

        if (
            TryTranslateScalar(expression, scope, builder, out var booleanColumn, out var property)
            && property.ClrType == typeof(bool)
        )
        {
            return new SqlBinaryExpression(
                booleanColumn,
                SqlBinaryOperator.Equal,
                new SqlBooleanExpression(true)
            );
        }

        if (TryEvaluate(expression, out var value) && value is bool boolean)
        {
            return new SqlBooleanExpression(boolean);
        }

        throw Unsupported(expression, "le prédicat Where");
    }

    private SqlExpression TranslateEquality(
        BinaryExpression binary,
        TranslationScope scope,
        QueryBuilder builder
    )
    {
        var leftIsColumn = TryTranslateScalar(
            binary.Left,
            scope,
            builder,
            out var left,
            out var leftProperty
        );
        var rightIsColumn = TryTranslateScalar(
            binary.Right,
            scope,
            builder,
            out var right,
            out var rightProperty
        );

        if (leftIsColumn == rightIsColumn)
        {
            throw Unsupported(binary, "une égalité qui n'oppose pas une colonne et une valeur");
        }

        var column = leftIsColumn ? left : right;
        var property = leftIsColumn ? leftProperty : rightProperty;
        var valueExpression = leftIsColumn ? binary.Right : binary.Left;

        if (!TryEvaluate(valueExpression, out var value))
        {
            throw Unsupported(valueExpression, "une valeur non constante dans une égalité");
        }

        var isNotEqual = binary.NodeType == ExpressionType.NotEqual;
        if (value is null)
        {
            return new SqlNullTestExpression(column, isNotEqual);
        }

        var convertedValue = property.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value;
        var parameter = CreateParameter(convertedValue);
        var comparison = new SqlBinaryExpression(
            column,
            isNotEqual ? SqlBinaryOperator.NotEqual : SqlBinaryOperator.Equal,
            parameter
        );

        if (isNotEqual && property.IsNullable)
        {
            return new SqlBinaryExpression(
                comparison,
                SqlBinaryOperator.Or,
                new SqlNullTestExpression(column, Negated: false)
            );
        }

        return comparison;
    }

    private bool TryTranslateScalar(
        Expression expression,
        TranslationScope scope,
        QueryBuilder builder,
        out SqlColumnExpression column,
        out IProperty property
    )
    {
        expression = StripConvert(expression);
        if (!TryReadMemberPath(expression, scope.Parameter, out var members))
        {
            column = null!;
            property = null!;
            return false;
        }

        var currentEntityType = scope.EntityType;
        var currentAlias = scope.TableAlias;
        var depth = scope.NavigationDepth;

        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            var mappedProperty = currentEntityType.FindProperty(member.Name);
            if (mappedProperty is not null)
            {
                if (index != members.Count - 1)
                {
                    throw Unsupported(
                        expression,
                        $"l'accès après la propriété scalaire '{member.Name}'"
                    );
                }

                column = CreateColumn(currentEntityType, currentAlias, mappedProperty);
                property = mappedProperty;
                return true;
            }

            var navigation = currentEntityType.FindNavigation(member.Name);
            if (navigation is null || navigation.IsCollection)
            {
                throw Unsupported(member, $"le membre non mappé '{member.Name}'");
            }

            depth++;
            if (depth > 2)
            {
                throw Unsupported(expression, "une navigation d'une profondeur supérieure à deux");
            }

            currentAlias = builder.GetOrAddRequiredReferenceJoin(currentAlias, navigation, depth);
            currentEntityType = navigation.TargetEntityType;
        }

        column = null!;
        property = null!;
        return false;
    }

    private SqlColumnExpression TranslateDirectScalar(
        Expression expression,
        TranslationScope scope,
        string operatorName
    )
    {
        expression = StripConvert(expression);
        if (
            !TryReadMemberPath(expression, scope.Parameter, out var members)
            || members.Count != 1
            || scope.EntityType.FindProperty(members[0].Name) is not { } property
        )
        {
            throw Unsupported(
                expression,
                $"une clé de {operatorName} qui n'est pas une colonne directe"
            );
        }

        return CreateColumn(scope.EntityType, scope.TableAlias, property);
    }

    private INavigation ResolveDirectCollectionNavigation(
        Expression expression,
        TranslationScope scope
    )
    {
        expression = StripConvert(expression);
        if (
            !TryReadMemberPath(expression, scope.Parameter, out var members)
            || members.Count != 1
            || scope.EntityType.FindNavigation(members[0].Name)
                is not { IsCollection: true } navigation
        )
        {
            throw Unsupported(expression, "une collection qui n'est pas une navigation directe");
        }

        return navigation;
    }

    private void ValidateCollectionNavigation(INavigation navigation)
    {
        if (
            navigation.IsOnDependent
            || !navigation.ForeignKey.IsRequired
            || navigation.ForeignKey.Properties.Count != 1
        )
        {
            throw Unsupported(navigation, "une navigation collection sans clé étrangère simple");
        }

        if (navigation.ForeignKey.PrincipalKey.Properties.Count != 1)
        {
            throw Unsupported(navigation, "une navigation collection avec une clé composite");
        }
    }

    private SqlExpression CreateCollectionCorrelation(
        INavigation navigation,
        TranslationScope parentScope,
        SqlTable childTable
    )
    {
        var principalProperty = navigation.ForeignKey.PrincipalKey.Properties.Single();
        var foreignKeyProperty = navigation.ForeignKey.Properties.Single();

        return new SqlBinaryExpression(
            CreateColumn(parentScope.EntityType, parentScope.TableAlias, principalProperty),
            SqlBinaryOperator.Equal,
            CreateColumn(navigation.TargetEntityType, childTable.Alias, foreignKeyProperty)
        );
    }

    private SqlParameterExpression CreatePaginationParameter(Expression expression, string name)
    {
        if (!TryEvaluate(expression, out var value) || value is not int count || count < 0)
        {
            throw Unsupported(
                expression,
                $"une valeur {name} qui n'est pas un entier positif ou nul"
            );
        }

        return CreateParameter(count);
    }

    private SqlParameterExpression CreateParameter(object value)
    {
        var parameter = new SqlParameterValue($"p{_parameters.Count}", value);
        _parameters.Add(parameter);
        return new SqlParameterExpression(parameter);
    }

    private SqlTable CreateTable(IEntityType entityType)
    {
        return new SqlTable(entityType.GetSchema(), entityType.GetTableName()!, $"t{_nextAlias++}");
    }

    private static SqlColumnExpression CreateColumn(
        IEntityType entityType,
        string alias,
        IProperty property
    )
    {
        var storeObject = StoreObjectIdentifier.Table(
            entityType.GetTableName()!,
            entityType.GetSchema()
        );
        var columnName = property.GetColumnName(storeObject);
        if (columnName is null)
        {
            throw Unsupported(
                property,
                $"la propriété '{property.Name}' sans colonne dans la table"
            );
        }

        return new SqlColumnExpression(alias, columnName);
    }

    private static LambdaExpression ReadLambda(
        MethodCallExpression methodCall,
        int expectedParameterCount
    )
    {
        var candidate = StripQuotes(methodCall.Arguments[^1]);
        if (
            candidate is not LambdaExpression lambda
            || lambda.Parameters.Count != expectedParameterCount
        )
        {
            throw Unsupported(methodCall, $"la surcharge de '{methodCall.Method.Name}' utilisée");
        }

        return lambda;
    }

    private static bool IsEnumerableMethod(MethodCallExpression methodCall, string name)
    {
        return methodCall.Method.DeclaringType == typeof(Enumerable)
            && methodCall.Method.Name == name;
    }

    private static bool TryReadMemberPath(
        Expression expression,
        ParameterExpression parameter,
        out IReadOnlyList<MemberInfo> members
    )
    {
        var path = new List<MemberInfo>();
        var current = StripConvert(expression);

        while (current is MemberExpression member)
        {
            path.Add(member.Member);
            current = StripConvert(member.Expression!);
        }

        path.Reverse();
        members = path;
        return current == parameter && path.Count > 0;
    }

    private static bool TryEvaluate(Expression expression, out object? value)
    {
        expression = StripConvert(expression);
        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;
            case MemberExpression member when member.Expression is not null:
                if (!TryEvaluate(member.Expression, out var owner))
                {
                    break;
                }

                value = member.Member switch
                {
                    FieldInfo field => field.GetValue(owner),
                    PropertyInfo property => property.GetValue(owner),
                    _ => null,
                };
                return member.Member is FieldInfo or PropertyInfo;
        }

        value = null;
        return false;
    }

    private static Expression StripConvert(Expression expression)
    {
        while (
            expression
                is UnaryExpression
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
                } unary
        )
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static Expression StripQuotes(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static PostgNetQueryNotSupportedException Unsupported(object expression, string feature)
    {
        return new PostgNetQueryNotSupportedException(
            $"PostgNet ne prend pas en charge {feature}. Expression : {expression}."
        );
    }

    private sealed record TranslationScope(
        ParameterExpression Parameter,
        IEntityType EntityType,
        string TableAlias,
        int NavigationDepth
    );

    private sealed class QueryBuilder
    {
        private readonly PostgNetQueryTranslator _translator;
        private readonly Dictionary<string, string> _referenceAliases = [];

        public QueryBuilder(
            PostgNetQueryTranslator translator,
            SqlTable from,
            IEntityType entityType
        )
        {
            _translator = translator;
            From = from;
            EntityType = entityType;
        }

        public SqlTable From { get; }

        public IEntityType EntityType { get; }

        public List<SqlJoin> Joins { get; } = [];

        public List<SqlOrdering> Orderings { get; } = [];

        public SqlExpression? Predicate { get; private set; }

        public SqlJsonObjectExpression? Projection { get; set; }

        public SqlParameterExpression? Offset { get; set; }

        public SqlParameterExpression? Limit { get; set; }

        public void AddPredicate(SqlExpression predicate)
        {
            Predicate = Predicate is null
                ? predicate
                : new SqlBinaryExpression(Predicate, SqlBinaryOperator.And, predicate);
        }

        public string GetOrAddRequiredReferenceJoin(
            string sourceAlias,
            INavigation navigation,
            int depth
        )
        {
            if (
                navigation.IsCollection
                || !navigation.IsOnDependent
                || !navigation.ForeignKey.IsRequired
                || navigation.ForeignKey.Properties.Count != 1
                || navigation.ForeignKey.PrincipalKey.Properties.Count != 1
            )
            {
                throw Unsupported(
                    navigation,
                    "une navigation de référence non requise ou à clé composite"
                );
            }

            var key =
                $"{sourceAlias}:{navigation.DeclaringEntityType.Name}:{navigation.Name}:{depth}";
            if (_referenceAliases.TryGetValue(key, out var existingAlias))
            {
                return existingAlias;
            }

            _translator.ValidateEntityType(navigation.TargetEntityType);
            var targetTable = _translator.CreateTable(navigation.TargetEntityType);
            var foreignKeyProperty = navigation.ForeignKey.Properties.Single();
            var principalProperty = navigation.ForeignKey.PrincipalKey.Properties.Single();
            var predicate = new SqlBinaryExpression(
                CreateColumn(navigation.DeclaringEntityType, sourceAlias, foreignKeyProperty),
                SqlBinaryOperator.Equal,
                CreateColumn(navigation.TargetEntityType, targetTable.Alias, principalProperty)
            );

            Joins.Add(new SqlJoin(targetTable, predicate));
            _referenceAliases.Add(key, targetTable.Alias);
            return targetTable.Alias;
        }

        public SqlSelectQuery Build()
        {
            if (Projection is null)
            {
                throw new InvalidOperationException("La projection SQL n'a pas été définie.");
            }

            return new SqlSelectQuery(From, Joins, Projection, Predicate, Orderings, Offset, Limit);
        }
    }
}
