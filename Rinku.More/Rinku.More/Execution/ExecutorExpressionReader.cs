using System.Linq.Expressions;
using System.Reflection;

namespace Rinku;

internal sealed class ExecutorExpressionReader
{
    private readonly ExecutorBuilder _builder;
    private readonly LambdaExpression _expression;
    private readonly Dictionary<ParameterExpression, OperationArgument> _parameters;
    private PlanSequence _sequence;
    private Func<OperationArgument, OperationArgument>? _capture;

    internal ExecutorExpressionReader(ExecutorBuilder builder, LambdaExpression expression, IReadOnlyList<object?> arguments, PlanSequence sequence)
    {
        _builder = builder;
        _expression = expression;
        _sequence = sequence;
        if (expression.Parameters.Count != arguments.Count)
            throw new ArgumentException($"The expression has {expression.Parameters.Count} parameters but received {arguments.Count} values.", nameof(arguments));

        _parameters = [];
        for (int i = 0; i < expression.Parameters.Count; i++)
        {
            OperationArgument argument = builder.ToOperationArgument(arguments[i], expression.Parameters[i].Type);
            builder.EnsureArgumentCompatible(expression.Parameters[i].Type, argument);
            _parameters.Add(expression.Parameters[i], argument);
        }
    }

    private ExecutorExpressionReader(ExecutorBuilder builder, LambdaExpression expression, Dictionary<ParameterExpression, OperationArgument> parameters, PlanSequence sequence)
    {
        _builder = builder;
        _expression = expression;
        _parameters = parameters;
        _sequence = sequence;
    }

    internal void ReadAction()
    {
        if (_expression.ReturnType != typeof(void))
            throw new ArgumentException("An action expression must return void.", nameof(_expression));
        ReadStatement(_expression.Body);
    }

    internal PlanValue ReadValue(Type resultType)
    {
        _builder.EnsureResultCompatible(resultType, _expression.ReturnType);
        OperationArgument value = ReadArgument(_expression.Body);
        if (value.IsValue && _builder.Data.GetValueType(value.Value) == resultType)
            return value.Value;
        PlanValue result = _builder.Data.CreateValue(resultType);
        _builder.Data.Add(_sequence, new ConvertOperation(_builder.ToRawOperand(value), result, false), outputs: [Variable.Create(_builder.Data, result)]);
        return result;
    }

    internal OperationArgument ReadValueArgument()
    {
        if (_expression.Parameters.Count != 0)
            throw new ArgumentException("The expression must not declare parameters when its values are captured from the builder.", nameof(_expression));
        if (_expression.ReturnType == typeof(void))
            throw new ArgumentException("The expression must produce a value.", nameof(_expression));
        return ReadArgument(_expression.Body);
    }

    internal static bool TryGetGuaranteedTypeTest(Expression expression, out Variable source, out Type type)
    {
        List<(Variable Source, Type Type)> tests = [];
        if (!CollectGuaranteedTypeTests(StripQuote(expression), tests) || tests.Count != 1)
        {
            source = null!;
            type = null!;
            return false;
        }

        (source, type) = tests[0];
        return true;
    }

    internal static bool TryGetGuaranteedNegatedTypeTest(Expression expression, out Variable source, out Type type)
    {
        expression = StripQuote(expression);
        if (expression is UnaryExpression { NodeType: ExpressionType.Not, Operand: TypeBinaryExpression { NodeType: ExpressionType.TypeIs } typeTest } && TryReadCapturedVariable(typeTest.Expression, out Variable? variable))
        {
            source = variable!;
            type = typeTest.TypeOperand;
            return true;
        }

        source = null!;
        type = null!;
        return false;
    }

    private static bool CollectGuaranteedTypeTests(Expression expression, List<(Variable Source, Type Type)> tests)
    {
        expression = StripQuote(expression);
        if (expression is TypeBinaryExpression { NodeType: ExpressionType.TypeIs } typeTest)
        {
            if (!TryReadCapturedVariable(typeTest.Expression, out Variable? variable))
                return false;
            tests.Add((variable!, typeTest.TypeOperand));
            return true;
        }

        if (expression is BinaryExpression { NodeType: ExpressionType.AndAlso } binary)
            return CollectGuaranteedTypeTests(binary.Left, tests) && CollectGuaranteedTypeTests(binary.Right, tests);

        return !ContainsTypeTest(expression);
    }

    private static bool ContainsTypeTest(Expression expression)
    {
        expression = StripQuote(expression);
        return expression switch
        {
            TypeBinaryExpression { NodeType: ExpressionType.TypeIs } => true,
            UnaryExpression unary => ContainsTypeTest(unary.Operand),
            BinaryExpression binary => ContainsTypeTest(binary.Left) || ContainsTypeTest(binary.Right),
            ConditionalExpression conditional => ContainsTypeTest(conditional.Test) || ContainsTypeTest(conditional.IfTrue) || ContainsTypeTest(conditional.IfFalse),
            MethodCallExpression call => (call.Object is not null && ContainsTypeTest(call.Object)) || call.Arguments.Any(ContainsTypeTest),
            MemberExpression member when member.Expression is not null => ContainsTypeTest(member.Expression),
            _ => false
        };
    }

    private static bool TryReadCapturedVariable(Expression expression, out Variable? variable)
    {
        expression = StripQuote(expression);
        if (expression is MemberExpression { Member: FieldInfo field } marker && field.Name == nameof(Variable<int>.Val) && field.DeclaringType?.IsGenericType == true && field.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>))
            return TryEvaluateCaptured(marker.Expression, out variable);
        if (expression is MethodCallExpression { Method: MethodInfo method } conversion && method.Name == "op_Implicit" && method.DeclaringType?.IsGenericType == true && method.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>) && conversion.Arguments.Count == 1)
            return TryReadCapturedVariable(conversion.Arguments[0], out variable);
        if (expression is UnaryExpression { Method: MethodInfo unaryMethod } unary && unaryMethod.Name == "op_Implicit" && unaryMethod.DeclaringType?.IsGenericType == true && unaryMethod.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>))
            return TryReadCapturedVariable(unary.Operand, out variable);
        return TryEvaluateCaptured(expression, out variable);
    }

    private static bool TryEvaluateCaptured(Expression? expression, out Variable? variable)
    {
        variable = null;
        if (expression is null || !typeof(Variable).IsAssignableFrom(expression.Type))
            return false;
        if (TryReadCapturedField(expression, out object? value))
        {
            variable = value as Variable;
            return variable is not null;
        }
        return false;
    }

    internal void ReadAssignment(object? value)
    {
        Expression target = StripQuote(_expression.Body);
        if (TryReadVariableExpression(target, out Variable variableTarget))
        {
            _builder.Store(variableTarget, _builder.ToOperationArgument(value, variableTarget.LogicalType));
            return;
        }
        if (target is BinaryExpression { NodeType: ExpressionType.ArrayIndex } arrayIndex)
        {
            OperationArgument array = ReadArgument(arrayIndex.Left);
            OperationArgument index = ReadArgument(arrayIndex.Right);
            Type elementType = arrayIndex.Left.Type.GetElementType() ?? throw new InvalidOperationException("The array target has no element type.");
            _builder.AddArrayAssignment(array, index, _builder.ToOperationArgument(value, elementType), _sequence);
            return;
        }
        if (target is IndexExpression indexExpression)
        {
            MethodInfo setter = indexExpression.Indexer?.SetMethod ?? throw new NotSupportedException("An index expression must have a setter.");
            List<OperationArgument> indexOperands = [];
            if (!setter.IsStatic)
                indexOperands.Add(ReadArgument(indexExpression.Object ?? throw new InvalidOperationException("The index expression has no object.")));
            indexOperands.AddRange(indexExpression.Arguments.Select(ReadArgument));
            indexOperands.Add(_builder.ToOperationArgument(value, setter.GetParameters().Last().ParameterType));
            _builder.AddCall(_sequence, setter, indexOperands, null);
            return;
        }
        if (target is not MemberExpression member)
            throw new NotSupportedException("Assign requires an expression ending in a writable field or property.");

        ValidateWritableReceiver(member.Expression);
        OperationArgument[] operands = MemberOperands(member);
        _builder.AddAssignment(_sequence, member.Member, operands, _builder.ToOperationArgument(value, GetMemberType(member.Member)));
    }

    internal void ReadAssignment(LambdaExpression valueExpression)
    {
        if (_expression.Parameters.Count != 0)
            throw new ArgumentException("The assignment target must not declare parameters when its values are captured from the builder.", nameof(_expression));
        Expression target = StripQuote(_expression.Body);
        ExecutorExpressionReader valueReader = new(_builder, valueExpression, Array.Empty<object?>(), _sequence);
        if (TryReadVariableExpression(target, out Variable variableTarget))
        {
            _builder.Store(variableTarget, valueReader.ReadValueArgument());
            return;
        }
        if (target is BinaryExpression { NodeType: ExpressionType.ArrayIndex } arrayIndex)
        {
            OperationArgument array = ReadArgument(arrayIndex.Left);
            OperationArgument index = ReadArgument(arrayIndex.Right);
            OperationArgument assignmentValue = valueReader.ReadValueArgument();
            _builder.AddArrayAssignment(array, index, assignmentValue, _sequence);
            return;
        }
        if (target is IndexExpression indexExpression)
        {
            MethodInfo setter = indexExpression.Indexer?.SetMethod ?? throw new NotSupportedException("An index expression must have a setter.");
            List<OperationArgument> indexOperands = [];
            if (!setter.IsStatic)
                indexOperands.Add(ReadArgument(indexExpression.Object ?? throw new InvalidOperationException("The index expression has no object.")));
            indexOperands.AddRange(indexExpression.Arguments.Select(ReadArgument));
            indexOperands.Add(valueReader.ReadValueArgument());
            _builder.AddCall(_sequence, setter, indexOperands, null);
            return;
        }
        if (target is not MemberExpression member)
            throw new NotSupportedException("Assign requires an expression ending in a writable field or property.");
        ValidateWritableReceiver(member.Expression);
        OperationArgument[] operands = MemberOperands(member);
        OperationArgument assignment = valueReader.ReadValueArgument();
        _builder.AddAssignment(_sequence, member.Member, operands, assignment);
    }

    private void ReadStatement(Expression expression)
    {
        expression = StripQuote(expression);
        if (expression is DefaultExpression && expression.Type == typeof(void)) return;
        if (expression is ConditionalExpression conditional && conditional.Type == typeof(void)) {
            OperationArgument condition = EnsureValue(ReadArgument(conditional.Test));
            var yes = new PlanSequence();
            var no = new PlanSequence();
            var outer = _sequence;
            try {
                _sequence = yes;
                ReadStatement(conditional.IfTrue);
                _sequence = no;
                ReadStatement(conditional.IfFalse);
            }
            finally { _sequence = outer; }
            _builder.Data.Add(_sequence, new ConditionNode(_builder.ToRawOperand(condition), yes, no));
            return;
        }
        if (expression is BlockExpression block)
        {
            foreach (Expression item in block.Expressions)
                ReadStatement(item);
            return;
        }
        if (expression is BinaryExpression { NodeType: ExpressionType.Assign } assignment)
        {
            ReadAssignmentExpression(assignment);
            return;
        }
        if (expression is MethodCallExpression call)
        {
            ReadCall(call, true);
            return;
        }
        OperationArgument value = ReadArgument(expression);
        if (value.IsValue && _builder.Data.GetValueType(value.Value) != typeof(void))
            return;
    }

    private void ReadAssignmentExpression(BinaryExpression assignment)
    {
        Expression target = StripQuote(assignment.Left);
        if (target is BinaryExpression { NodeType: ExpressionType.ArrayIndex } arrayIndex)
        {
            OperationArgument array = ReadArgument(arrayIndex.Left);
            OperationArgument index = ReadArgument(arrayIndex.Right);
            OperationArgument value = ReadArgument(assignment.Right);
            _builder.AddArrayAssignment(array, index, value, _sequence);
            return;
        }
        if (target is IndexExpression indexExpression)
        {
            MethodInfo setter = indexExpression.Indexer?.SetMethod ?? throw new NotSupportedException("An index expression must have a setter.");
            List<OperationArgument> indexOperands = [];
            if (!setter.IsStatic)
                indexOperands.Add(ReadArgument(indexExpression.Object ?? throw new InvalidOperationException("The index expression has no object.")));
            indexOperands.AddRange(indexExpression.Arguments.Select(ReadArgument));
            indexOperands.Add(ReadArgument(assignment.Right));
            _builder.AddCall(_sequence, setter, indexOperands, null);
            return;
        }
        if (target is not MemberExpression member)
            throw new NotSupportedException("Only field, property, array, and indexer assignments are supported.");
        ValidateWritableReceiver(member.Expression);
        _builder.AddAssignment(_sequence, member.Member, MemberOperands(member), ReadArgument(assignment.Right));
    }

    private OperationArgument ReadArgument(Expression expression)
    {
        expression = StripQuote(expression);
        if (TryReadVariableArgument(expression, out OperationArgument variableArgument))
            return variableArgument;
        switch (expression)
        {
            case ParameterExpression parameter:
                if (_parameters.TryGetValue(parameter, out OperationArgument parameterValue))
                    return parameterValue;
                throw new InvalidOperationException($"Expression parameter {parameter.Name} was not bound.");
            case ConstantExpression constant:
                return _builder.ToOperationArgument(constant.Value, constant.Type);
            case MemberExpression member:
                return ReadMember(member);
            case MethodCallExpression call:
                return ReadCall(call);
            case InvocationExpression invocation:
                return ReadCall(Expression.Call(invocation.Expression, invocation.Expression.Type.GetMethod(nameof(Action.Invoke))!, invocation.Arguments));
            case LambdaExpression lambda:
                return ReadLambda(lambda);
            case NewExpression constructor:
                return ReadConstructor(constructor);
            case NewArrayExpression array:
                return ReadArray(array);
            case DefaultExpression defaultValue:
                return _builder.ToOperationArgument(defaultValue.Type.IsValueType ? Activator.CreateInstance(defaultValue.Type) : null, defaultValue.Type);
            case UnaryExpression unary when unary.NodeType == ExpressionType.Not:
                return ReadNot(unary);
            case UnaryExpression unary when unary.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked:
                return ReadNegate(unary);
            case UnaryExpression unary when unary.NodeType == ExpressionType.UnaryPlus:
                if (unary.Method is MethodInfo method)
                    return OperationArgument.FromValue(_builder.AddCall(_sequence, method, [ReadArgument(unary.Operand)], unary.Type));
                OperationArgument operand = ReadArgument(unary.Operand);
                if (operand.IsValue && _builder.Data.GetValueType(operand.Value) == unary.Type)
                    return operand;
                PlanValue promoted = _builder.Data.CreateValue(unary.Type);
                _builder.Data.Add(_sequence, new ConvertOperation(_builder.ToRawOperand(operand), promoted, false), outputs: [Variable.Create(_builder.Data, promoted)]);
                return OperationArgument.FromValue(promoted);
            case UnaryExpression unary when unary.NodeType == ExpressionType.TypeAs:
                return ReadTypeAs(unary);
            case TypeBinaryExpression typeTest when typeTest.NodeType == ExpressionType.TypeIs:
                return ReadTypeTest(typeTest);
            case UnaryExpression unary when unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked:
                return ReadConvert(unary);
            case BinaryExpression binary:
                if (binary.NodeType == ExpressionType.ArrayIndex)
                    return ReadArrayIndex(binary);
                return ReadBinary(binary);
            case ConditionalExpression conditional:
                return ReadConditional(conditional);
            case IndexExpression index:
                return ReadIndex(index);
            default:
                throw new NotSupportedException($"Expression node {expression.NodeType} is not supported by the executor expression layer. Wrap it in a method or use the structural builder API.");
        }
    }

    private OperationArgument ReadMember(MemberExpression expression)
    {
        if (expression.Member is FieldInfo capturedField && (expression.Expression is ConstantExpression || capturedField.DeclaringType?.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) == true) && TryReadCapturedField(expression, out object? captured))
            return _builder.ToOperationArgument(captured, expression.Type);

        if (IsStatic(expression.Member))
            return OperationArgument.FromValue(_builder.AddMemberRead(_sequence, expression.Member, [], expression.Type));

        OperationArgument instance = ReadArgument(expression.Expression ?? throw new InvalidOperationException($"Member {expression.Member} has no instance."));
        return OperationArgument.FromValue(_builder.AddMemberRead(_sequence, expression.Member, [instance], expression.Type));
    }

    private bool TryReadVariableArgument(Expression expression, out OperationArgument argument)
    {
        if (TryReadVariableMarker(expression, out Variable markerVariable))
        {
            argument = VariableArgument(markerVariable);
            return true;
        }
        if (expression is MethodCallExpression { Method: MethodInfo method } conversion && method.Name == "op_Implicit" && method.DeclaringType?.IsGenericType == true && method.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>) && conversion.Arguments.Count == 1 && TryReadVariableArgument(conversion.Arguments[0], out argument))
            return true;
        if (expression is UnaryExpression { Method: MethodInfo unaryMethod } unary && unaryMethod.Name == "op_Implicit" && unaryMethod.DeclaringType?.IsGenericType == true && unaryMethod.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>) && TryReadVariableArgument(unary.Operand, out argument))
            return true;
        if (TryEvaluateCaptured(expression, out Variable? variable))
        {
            argument = VariableArgument(variable!);
            return true;
        }
        argument = default;
        return false;
    }

    private OperationArgument VariableArgument(Variable variable) => _capture is null ? _builder.ToOperationArgument(variable) : _capture(OperationArgument.FromVariable(variable));

    private OperationArgument ReadLambda(LambdaExpression lambda, bool methodGroup = false)
    {
        List<OperationArgument> captures = [];
        CaptureVisitor visitor = new(this, captures);
        visitor.Visit(lambda);
        Type[] inputs = captures.Select(argument => argument.Type).Concat(lambda.Parameters.Select(parameter => parameter.Type)).ToArray();
        ExecutorBuilder nested = new(inputs);
        Dictionary<ParameterExpression, OperationArgument> parameters = [];
        for (int index = 0; index < lambda.Parameters.Count; index++)
        {
            parameters.Add(lambda.Parameters[index], OperationArgument.FromValue(nested.Data.Inputs[captures.Count + index]));
        }
        foreach (var outer in _parameters)
        {
            int index = captures.FindIndex(argument => OperationArgumentComparer.Instance.Equals(argument, outer.Value));
            if (index >= 0)
            {
                parameters.TryAdd(outer.Key, OperationArgument.FromValue(nested.Data.Inputs[index]));
            }
        }
        ExecutorExpressionReader reader = new(nested, lambda, parameters, nested.Data.Body)
        {
            _capture = argument =>
            {
                OperationArgument source = _capture is null ? argument : _capture(argument);
                int index = captures.FindIndex(capture => OperationArgumentComparer.Instance.Equals(capture, source));
                if (index < 0)
                {
                    throw new InvalidOperationException("The nested delegate refers to an unrecorded capture.");
                }
                return OperationArgument.FromValue(nested.Data.Inputs[index]);
            }
        };
        if (lambda.ReturnType == typeof(void))
        {
            reader.ReadAction();
        }
        else
        {
            nested.Data.Output = Variable.Create(nested.Data, reader.ReadValue(lambda.ReturnType));
        }
        PlanValue result = _builder.Data.CreateValue(lambda.Type);
        _builder.Data.Add(_sequence, new DelegateOperation(lambda.Type, nested.Data, captures.ToArray(), result, methodGroup), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private sealed class CaptureVisitor(ExecutorExpressionReader reader, List<OperationArgument> captures) : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> parameters = [];

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            foreach (ParameterExpression parameter in node.Parameters)
            {
                parameters.Add(parameter);
            }
            Visit(node.Body);
            foreach (ParameterExpression parameter in node.Parameters)
            {
                parameters.Remove(parameter);
            }
            return node;
        }

        public override Expression? Visit(Expression? node)
        {
            if (node is not null && reader.TryReadVariableArgument(node, out OperationArgument argument))
            {
                Add(argument);
                return node;
            }
            if (node is ParameterExpression parameter && !parameters.Contains(parameter) && reader._parameters.TryGetValue(parameter, out OperationArgument value))
            {
                Add(value);
                return node;
            }
            return base.Visit(node);
        }

        private void Add(OperationArgument argument)
        {
            if (!captures.Any(capture => OperationArgumentComparer.Instance.Equals(capture, argument)))
            {
                captures.Add(argument);
            }
        }
    }

    private bool TryReadVariableMarker(Expression expression, out Variable variable)
    {
        expression = StripQuote(expression);
        if (expression is MemberExpression { Member: FieldInfo field } marker && field.Name == nameof(Variable<int>.Val) && field.DeclaringType?.IsGenericType == true && field.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>))
        {
            if (marker.Expression is ParameterExpression)
            {
                variable = null!;
                return false;
            }
            if (!TryEvaluateCaptured(marker.Expression, out Variable? capturedVariable))
                throw new InvalidOperationException("Variable.Val can only be used on a Variable<T> instance.");
            variable = capturedVariable!;
            return true;
        }
        variable = null!;
        return false;
    }

    private bool TryReadVariableExpression(Expression expression, out Variable variable)
    {
        expression = StripQuote(expression);
        if (TryReadVariableMarker(expression, out variable))
            return true;
        if (expression is MethodCallExpression { Method: MethodInfo method } conversion && method.Name == "op_Implicit" && method.DeclaringType?.IsGenericType == true && method.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>) && conversion.Arguments.Count == 1)
            return TryReadVariableExpression(conversion.Arguments[0], out variable);
        if (expression is UnaryExpression { Method: MethodInfo unaryMethod } unary && unaryMethod.Name == "op_Implicit" && unaryMethod.DeclaringType?.IsGenericType == true && unaryMethod.DeclaringType.GetGenericTypeDefinition() == typeof(Variable<>))
            return TryReadVariableExpression(unary.Operand, out variable);
        if (TryEvaluateCaptured(expression, out Variable? capturedVariable))
        {
            variable = capturedVariable!;
            return true;
        }
        variable = null!;
        return false;
    }

    private OperationArgument ReadCall(MethodCallExpression expression, bool discard = false)
    {
        MethodInfo method = expression.Method;
        List<OperationArgument> operands = [];
        if (!method.IsStatic)
            operands.Add(ReadArgument(expression.Object ?? throw new InvalidOperationException($"Instance method {method} has no instance expression.")));
        foreach (Expression argument in expression.Arguments)
            operands.Add(ReadArgument(argument));
        if (method.ReturnType == typeof(void) || discard)
        {
            _builder.AddCall(_sequence, method, operands, null);
            return OperationArgument.FromConstant(typeof(void), null);
        }
        return OperationArgument.FromValue(_builder.AddCall(_sequence, method, operands, method.ReturnType));
    }

    private OperationArgument ReadConstructor(NewExpression expression)
    {
        ConstructorInfo constructor = expression.Constructor ?? throw new InvalidOperationException("The constructor expression has no constructor.");
        OperationArgument[] arguments = expression.Arguments.Select(ReadArgument).ToArray();
        return OperationArgument.FromValue(_builder.AddCall(_sequence, constructor, arguments, expression.Type));
    }

    private OperationArgument ReadNot(UnaryExpression expression)
    {
        OperationArgument value = ReadArgument(expression.Operand);
        if (expression.Method is MethodInfo method)
            return OperationArgument.FromValue(_builder.AddCall(_sequence, method, [value], expression.Type));
        if (expression.Type != typeof(bool))
        {
            value = EnsureValue(value);
            PlanValue result = _builder.AddUnary(_sequence, value, ExecutorUnaryOperator.OnesComplement, expression.Type);
            return OperationArgument.FromValue(result);
        }
        value = EnsureValue(value);
        PlanValue logicalResult = _builder.Data.CreateValue(typeof(bool));
        _builder.Data.Add(_sequence, new LogicalNotOperation(_builder.ToRawOperand(value), logicalResult), outputs: [Variable.Create(_builder.Data, logicalResult)]);
        return OperationArgument.FromValue(logicalResult);
    }

    private OperationArgument ReadNegate(UnaryExpression expression)
    {
        OperationArgument value = ReadArgument(expression.Operand);
        if (expression.Method is MethodInfo method)
            return OperationArgument.FromValue(_builder.AddCall(_sequence, method, [value], expression.Type));
        PlanValue result = _builder.AddUnary(_sequence, value, ExecutorUnaryOperator.Negate, expression.Type, expression.NodeType == ExpressionType.NegateChecked);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadConvert(UnaryExpression expression)
    {
        if (TryReadMethodGroup(expression, out OperationArgument methodGroup))
            return methodGroup;
        OperationArgument value = ReadArgument(expression.Operand);
        if (expression.Method is MethodInfo { Name: not "op_Implicit" } method)
            return OperationArgument.FromValue(_builder.AddCall(_sequence, method, [value], expression.Type));
        if (value.IsValue && _builder.Data.GetValueType(value.Value) == expression.Type)
            return value;
        PlanValue result = _builder.Data.CreateValue(expression.Type);
        _builder.Data.Add(_sequence, new ConvertOperation(_builder.ToRawOperand(value), result, expression.NodeType == ExpressionType.ConvertChecked), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private bool TryReadMethodGroup(UnaryExpression expression, out OperationArgument result) {
        result = default;
        if (!typeof(Delegate).IsAssignableFrom(expression.Type) || expression.Operand is not MethodCallExpression { Object: ConstantExpression { Value: MethodInfo method }, Arguments.Count: 2 } call || call.Method.Name != nameof(MethodInfo.CreateDelegate) || call.Arguments[0] is not ConstantExpression { Value: Type delegateType } || delegateType != expression.Type)
            return false;
        Expression target = call.Arguments[1];
        while (target is UnaryExpression { NodeType: ExpressionType.Convert, Type: var type } convert && type == typeof(object)) target = convert.Operand;
        if (method.IsStatic && target is not ConstantExpression { Value: null }) return false;
        MethodInfo invoke = delegateType.GetMethod(nameof(Action.Invoke))!;
        ParameterExpression[] parameters = Array.ConvertAll(invoke.GetParameters(), static parameter => Expression.Parameter(parameter.ParameterType, parameter.Name));
        ParameterExpression? instance = method.IsStatic ? null : Expression.Parameter(target.Type);
        if (instance is not null) _parameters.Add(instance, ReadArgument(target));
        try {
            Expression body = Expression.Call(instance, method, parameters);
            result = ReadLambda(Expression.Lambda(delegateType, body, parameters), methodGroup: true);
            return true;
        }
        finally { if (instance is not null) _parameters.Remove(instance); }
    }

    private OperationArgument ReadTypeAs(UnaryExpression expression)
    {
        if (expression.Type.IsValueType)
            throw new NotSupportedException("Type-as expressions with value-type targets are not supported by the executor expression layer.");
        OperationArgument value = ReadArgument(expression.Operand);
        PlanValue result = _builder.Data.CreateValue(expression.Type);
        _builder.Data.Add(_sequence, new TypeAsOperation(_builder.ToRawOperand(value), result), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadTypeTest(TypeBinaryExpression expression)
    {
        OperationArgument value = ReadArgument(expression.Expression);
        PlanValue result = _builder.Data.CreateValue(typeof(bool));
        _builder.Data.Add(_sequence, new TypeTestOperation(_builder.ToRawOperand(value), expression.TypeOperand, result), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadBinary(BinaryExpression expression)
    {
        if (expression.NodeType == ExpressionType.Coalesce)
            return ReadCoalesce(expression);
        if (expression.Method is MethodInfo method && expression.NodeType is not (ExpressionType.AndAlso or ExpressionType.OrElse))
        {
            OperationArgument leftOperand = ReadArgument(expression.Left);
            OperationArgument rightOperand = ReadArgument(expression.Right);
            return OperationArgument.FromValue(_builder.AddCall(_sequence, method, [leftOperand, rightOperand], expression.Type));
        }
        if (expression.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
            return ReadLogicalBinary(expression);

        OperationArgument left = ReadArgument(expression.Left);
        OperationArgument right = ReadArgument(expression.Right);
        if (TryGetComparison(expression.NodeType, out ExecutorComparison comparison))
        {
            PlanValue result = _builder.AddCompare(_sequence, left, comparison, right);
            return OperationArgument.FromValue(result);
        }
        if (!TryGetBinary(expression.NodeType, out ExecutorBinaryOperator @operator))
            throw new NotSupportedException($"Binary expression {expression.NodeType} is not supported by the executor expression layer.");
        PlanValue binaryResult = _builder.AddBinary(_sequence, left, @operator, right, expression.Type, expression.NodeType is ExpressionType.AddChecked or ExpressionType.SubtractChecked or ExpressionType.MultiplyChecked);
        return OperationArgument.FromValue(binaryResult);
    }

    private OperationArgument ReadArray(NewArrayExpression expression)
    {
        if (expression.NodeType != ExpressionType.NewArrayInit)
            throw new NotSupportedException("Array bounds expressions are not supported by the executor expression layer.");
        OperationArgument[] elements = expression.Expressions.Select(ReadArgument).ToArray();
        PlanValue result = _builder.AddArrayCreate(_sequence, elements, expression.Type);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadArrayIndex(BinaryExpression expression)
    {
        OperationArgument array = ReadArgument(expression.Left);
        OperationArgument index = ReadArgument(expression.Right);
        PlanValue result = _builder.AddArrayRead(array, index, _sequence);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadLogicalBinary(BinaryExpression expression)
    {
        OperationArgument left = EnsureValue(ReadArgument(expression.Left));
        ExecutorLogicalOperator @operator = expression.NodeType == ExpressionType.AndAlso ? ExecutorLogicalOperator.AndAlso : ExecutorLogicalOperator.OrElse;
        var instructions = new PlanSequence();
        OperationArgument right = ReadInSequence(expression.Right, instructions);
        PlanValue result = _builder.Data.CreateValue(typeof(bool));
        _builder.Data.Add(_sequence, new LogicalConditionNode(_builder.ToRawOperand(left), @operator, instructions, _builder.ToRawOperand(right), result), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadConditional(ConditionalExpression expression)
    {
        OperationArgument condition = EnsureValue(ReadArgument(expression.Test));
        var yes = new PlanSequence();
        var no = new PlanSequence();
        OperationArgument trueResult = ReadInSequence(expression.IfTrue, yes);
        OperationArgument falseResult = ReadInSequence(expression.IfFalse, no);
        PlanValue result = _builder.Data.CreateValue(expression.Type);
        _builder.Data.Add(_sequence, new ConditionalValueNode(_builder.ToRawOperand(condition), yes, _builder.ToRawOperand(trueResult), no, _builder.ToRawOperand(falseResult), result), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadCoalesce(BinaryExpression expression)
    {
        if (expression.Conversion is not null)
            throw new NotSupportedException("Coalescing conversion lambdas are not supported by the executor expression layer.");
        OperationArgument left = EnsureValue(ReadArgument(expression.Left));
        var yes = new PlanSequence();
        var no = new PlanSequence();
        OperationArgument condition;
        OperationArgument value = left;
        if (Nullable.GetUnderlyingType(expression.Left.Type) is not null)
        {
            condition = OperationArgument.FromValue(_builder.AddCall(_sequence, expression.Left.Type.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!, [left], typeof(bool)));
            value = OperationArgument.FromValue(_builder.AddCall(yes, expression.Left.Type.GetProperty(nameof(Nullable<int>.Value))!.GetMethod!, [left], Nullable.GetUnderlyingType(expression.Left.Type)!));
        }
        else
        {
            condition = OperationArgument.FromValue(_builder.AddCompare(_sequence, left, ExecutorComparison.NotEqual, OperationArgument.FromTypedConstant(expression.Left.Type, null)));
        }
        OperationArgument fallback = ReadInSequence(expression.Right, no);
        PlanValue result = _builder.Data.CreateValue(expression.Type);
        _builder.Data.Add(_sequence, new ConditionalValueNode(_builder.ToRawOperand(condition), yes, _builder.ToRawOperand(value), no, _builder.ToRawOperand(fallback), result), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument ReadInSequence(Expression expression, PlanSequence sequence)
    {
        var outer = _sequence;
        _sequence = sequence;
        try
        {
            return EnsureValue(ReadArgument(expression));
        }
        finally
        {
            _sequence = outer;
        }
    }
    private OperationArgument ReadIndex(IndexExpression expression)
    {
        MethodInfo getter = expression.Indexer?.GetMethod ?? throw new NotSupportedException("An index expression must have a getter.");
        List<OperationArgument> operands = [];
        if (!getter.IsStatic)
            operands.Add(ReadArgument(expression.Object ?? throw new InvalidOperationException("The index expression has no object.")));
        operands.AddRange(expression.Arguments.Select(ReadArgument));
        return OperationArgument.FromValue(_builder.AddCall(_sequence, getter, operands, expression.Type));
    }

    private OperationArgument EnsureValue(OperationArgument argument)
    {
        if (argument.IsValue || argument.IsVariable)
            return argument;
        PlanValue result = _builder.Data.CreateValue(argument.Type);
        _builder.Data.Add(_sequence, new CapturedValueOperation(argument.Data, result), outputs: [Variable.Create(_builder.Data, result)]);
        return OperationArgument.FromValue(result);
    }

    private OperationArgument[] MemberOperands(MemberExpression member)
        => IsStatic(member.Member) ? [] : [ReadArgument(member.Expression ?? throw new InvalidOperationException($"Member {member.Member} has no instance."))];

    private void ValidateWritableReceiver(Expression? receiver)
    {
        if (receiver is not null && receiver is not ParameterExpression && receiver.Type.IsValueType && !TryReadVariableExpression(receiver, out _))
            throw new NotSupportedException("Assignments through a value-type member, array element, indexer, or method result are not supported because they require write-back to the containing value.");
    }

    private static Expression StripQuote(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } unary)
            expression = unary.Operand;
        return expression;
    }

    private static bool IsStatic(MemberInfo member)
        => member switch
        {
            FieldInfo field => field.IsStatic,
            PropertyInfo property => property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic == true,
            _ => false
        };

    private static Type GetMemberType(MemberInfo member)
        => member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => throw new NotSupportedException($"Member {member} is not a field or property.")
        };

    private static bool TryReadCapturedField(Expression expression, out object? value)
    {
        expression = StripQuote(expression);
        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }
        if (expression is MemberExpression { Member: FieldInfo field, Expression: not null } member && TryReadCapturedField(member.Expression, out object? owner))
        {
            value = field.GetValue(owner);
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryGetComparison(ExpressionType type, out ExecutorComparison comparison)
    {
        comparison = type switch
        {
            ExpressionType.Equal => ExecutorComparison.Equal,
            ExpressionType.NotEqual => ExecutorComparison.NotEqual,
            ExpressionType.LessThan => ExecutorComparison.LessThan,
            ExpressionType.LessThanOrEqual => ExecutorComparison.LessThanOrEqual,
            ExpressionType.GreaterThan => ExecutorComparison.GreaterThan,
            ExpressionType.GreaterThanOrEqual => ExecutorComparison.GreaterThanOrEqual,
            _ => default
        };
        return type is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual;
    }

    private static bool TryGetBinary(ExpressionType type, out ExecutorBinaryOperator @operator)
    {
        @operator = type switch
        {
            ExpressionType.Add or ExpressionType.AddChecked => ExecutorBinaryOperator.Add,
            ExpressionType.Subtract or ExpressionType.SubtractChecked => ExecutorBinaryOperator.Subtract,
            ExpressionType.Multiply or ExpressionType.MultiplyChecked => ExecutorBinaryOperator.Multiply,
            ExpressionType.Divide => ExecutorBinaryOperator.Divide,
            ExpressionType.Modulo => ExecutorBinaryOperator.Modulo,
            ExpressionType.And => ExecutorBinaryOperator.And,
            ExpressionType.Or => ExecutorBinaryOperator.Or,
            ExpressionType.ExclusiveOr => ExecutorBinaryOperator.ExclusiveOr,
            ExpressionType.LeftShift => ExecutorBinaryOperator.LeftShift,
            ExpressionType.RightShift => ExecutorBinaryOperator.RightShift,
            _ => default
        };
        return type is ExpressionType.Add or ExpressionType.AddChecked or ExpressionType.Subtract or ExpressionType.SubtractChecked or ExpressionType.Multiply or ExpressionType.MultiplyChecked or ExpressionType.Divide or ExpressionType.Modulo or ExpressionType.And or ExpressionType.Or or ExpressionType.ExclusiveOr or ExpressionType.LeftShift or ExpressionType.RightShift;
    }
}
