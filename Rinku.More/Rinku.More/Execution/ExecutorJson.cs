using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinku;

/// <summary>Serializes and deserializes editable executor builders.</summary>
public static class ExecutorJson
{
    private const int CurrentVersion = 13;
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(true);

    /// <summary>Serializes the editable instruction tree. Instance-dependent constants are rejected.</summary>
    public static string Serialize(ExecutorBuilder builder, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        JsonWriteContext context = new(builder.Data);
        return JsonSerializer.Serialize(context.WritePlan(), indented ? IndentedOptions : CompactOptions);
    }

    /// <summary>Deserializes a builder for the requested executor contract.</summary>
    public static ExecutorBuilder Deserialize(string json)
    {
        PlanDto dto = JsonSerializer.Deserialize<PlanDto>(json, CompactOptions) ?? throw new JsonException("The executor JSON is empty.");
        return ReadPlan(dto);
    }

    private static ExecutorBuilder ReadPlan(PlanDto dto)
    {
        if (dto.Version != CurrentVersion)
            throw new JsonException($"Unsupported executor JSON version {dto.Version}.");
        if (dto.InputCount < 0 || dto.Variables is null || dto.InputCount > dto.Variables.Count)
            throw new JsonException("Invalid executor input count.");
        ExecutorBuilder builder = new(dto.Variables.Take(dto.InputCount).Select(variable => ReadType(variable.Type, "input type")).ToArray());
        ReadVariables(dto, builder);
        ReadItems(dto.Body ?? throw new JsonException("The executor body is missing."), builder);
        if (dto.Output is int output)
            builder.Data.Output = Variable.Create(builder.Data, ReadSlot(output, builder));
        return builder;
    }

    private static JsonSerializerOptions CreateOptions(bool indented)
        => new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented
        };

    private sealed class JsonWriteContext(ExecutorScope plan, HashSet<ExecutorScope>? ancestors = null)
    {
        private readonly Dictionary<PlanSlot, int> variables = plan.Slots.Select((slot, index) => (slot, index)).ToDictionary(x => x.slot, x => x.index);

        private readonly HashSet<ExecutorScope> active = ancestors ?? [];

        internal PlanDto WritePlan()
        {
            if (!active.Add(plan))
                throw new NotSupportedException("Recursive executor scopes cannot be serialized.");
            try
            {
                return new()
                {
                    Version = CurrentVersion,
                    InputCount = plan.Inputs.Count,
                    Output = plan.Output is null ? null : variables[plan.Output.Slot],
                    Variables = plan.Slots.Select(x => new VariableDto { Type = WriteType(x.LogicalType), Name = x.Name, Published = x.IsPublished, Reference = x.Kind == PlanSlotKind.Reference, DefaultWhenMissing = x.DefaultWhenMissing }).ToList(),
                    Body = WriteSequence(plan.Body)
                };
            }
            finally
            {
                active.Remove(plan);
            }
        }

        private ItemDto WriteItem(PlanInstruction instruction)
        {
            IPlanNode item = instruction.Node;
            ItemDto dto = item switch
            {
                DeclarationOperation declaration => new() { Kind = "declare", Variable = variables[declaration.Local.Slot] },
                ReferenceOperation reference => new() { Kind = "reference", Result = variables[reference.Result.Slot] },
                ExecutorScope scopeData => new() { Kind = "compose", Plan = new JsonWriteContext(scopeData, active).WritePlan() },
                CompositionNode composition => new() { Kind = "compose", Plan = new JsonWriteContext(composition.Source, active).WritePlan(), Result = composition.Result is null ? null : variables[composition.Result.Slot] },
                ScopeNode scope => new() { Kind = "scope", Body = WriteSequence(scope.Body), Variable = scope.Output is null ? null : variables[scope.Output] },
                CallOperation x => new() { Kind = "call", Name = x.ConstructorType is null ? x.Names[0] : ".ctor", Names = x.ConstructorType is null && x.Names.Count > 1 ? x.Names.ToList() : null, Type = x.ConstructorType is null ? null : WriteType(x.ConstructorType), Passing = x.Passing, GenericArguments = x.GenericArguments.Select(WriteType).ToList(), Operands = WriteArguments(x.Operands), Result = x.Result is null ? null : variables[x.Result.Slot] },
                CapturedValueOperation x => new() { Kind = "constant", Value = WriteArgument(x.Value), Result = variables[x.Result.Slot] },
                ConvertOperation x => new() { Kind = "convert", Value = WriteArgument(x.Value), Result = variables[x.Result.Slot], Checked = x.IsChecked },
                TypeAsOperation x => new() { Kind = "typeAs", Value = WriteArgument(x.Value), Result = variables[x.Result.Slot] },
                TypeTestOperation x => new() { Kind = "typeTest", Value = WriteArgument(x.Value), Type = WriteType(x.Type), Result = variables[x.Result.Slot] },
                BinaryOperation x => new() { Kind = "binary", Left = WriteArgument(x.Left), Right = WriteArgument(x.Right), Operator = x.Operator.ToString(), Checked = x.IsChecked, Result = variables[x.Result.Slot] },
                CompareOperation x => new() { Kind = "compare", Left = WriteArgument(x.Left), Right = WriteArgument(x.Right), Comparison = x.Comparison.ToString(), Result = variables[x.Result.Slot] },
                LogicalNotOperation x => new() { Kind = "logicalNot", Value = WriteArgument(x.Value), Result = variables[x.Result.Slot] },
                UnaryOperation x => new() { Kind = "unary", Value = WriteArgument(x.Value), Operator = x.Operator.ToString(), Checked = x.IsChecked, Result = variables[x.Result.Slot] },
                ArrayCreateOperation x => new() { Kind = "arrayCreate", Operands = WriteArguments(x.Elements), Result = variables[x.Result.Slot] },
                ArrayReadOperation x => new() { Kind = "arrayRead", Array = WriteArgument(x.Array), Index = WriteArgument(x.Index), Result = variables[x.Result.Slot] },
                ArrayAssignmentOperation x => new() { Kind = "arrayAssignment", Array = WriteArgument(x.Array), Index = WriteArgument(x.Index), Value = WriteArgument(x.Value) },
                StoreOperation x => new() { Kind = x.Target.IsVariable ? "localStore" : "valueStore", Variable = x.Target.IsVariable ? variables[x.Target.Local.Slot] : null, Result = x.Target.IsValue ? variables[x.Target.Value.Slot] : null, Value = WriteArgument(x.Source) },
                LocalLoadOperation x => new() { Kind = "localLoad", Variable = variables[x.Local.Slot], Result = variables[x.Result.Slot] },

                ConditionNode x => new() { Kind = "if", Value = WriteArgument(x.Condition), WhenTrue = WriteSequence(x.WhenTrue), WhenFalse = x.WhenFalse is null ? null : WriteSequence(x.WhenFalse) },
                ForEachNode x => new() { Kind = "foreach", Value = WriteArgument(x.Source), Result = variables[x.Item.Slot], Body = WriteSequence(x.Body) },
                WhileNode x => new() { Kind = "while", Condition = WriteSequence(x.Condition), Value = WriteArgument(x.ConditionResult), Body = WriteSequence(x.Body) },
                DoWhileNode x => new() { Kind = "doWhile", Condition = WriteSequence(x.Condition), Value = WriteArgument(x.ConditionResult), Body = WriteSequence(x.Body) },
                ForNode x => new() { Kind = "for", Setup = WriteSequence(x.Setup), Condition = WriteSequence(x.Condition), Value = WriteArgument(x.ConditionResult), Increment = WriteSequence(x.Increment), Body = WriteSequence(x.Body) },
                LogicalConditionNode x => new() { Kind = "logical", Left = WriteArgument(x.Left), Operator = x.Operator.ToString(), Body = WriteSequence(x.Right), Right = WriteArgument(x.RightResult), Result = variables[x.Result.Slot] },
                ConditionalValueNode x => new() { Kind = "conditional", Value = WriteArgument(x.Condition), WhenTrue = WriteSequence(x.WhenTrue), Left = WriteArgument(x.TrueResult), WhenFalse = WriteSequence(x.WhenFalse), Right = WriteArgument(x.FalseResult), Result = variables[x.Result.Slot] },
                BreakOperation => new() { Kind = "break" },
                ContinueOperation => new() { Kind = "continue" },
                ReturnOperation => new() { Kind = "return" },
                _ => throw new NotSupportedException($"Executor item {item.GetType()} is not part of the builder representation.")
            };
            dto.Requirement = instruction.Requirement;
            dto.Outputs = instruction.Outputs.Select(output => variables[output.Slot]).ToList();
            return dto;
        }

        private SequenceDto WriteSequence(PlanSequence sequence) => new() { Items = sequence.Items.Select(WriteItem).ToList() };

        private List<ArgumentDto> WriteArguments(IReadOnlyList<OperationArgument>? arguments)
            => arguments is null ? [] : arguments.Select(x => WriteArgument(x)!).ToList();

        private ArgumentDto? WriteArgument(OperationArgument argument)
        {
            if (argument.Variable is Variable variable)
            {
                return new()
                {
                    Variable = variables[variable.Slot]
                };
            }
            return WriteConstant(argument.Type, argument.Data);
        }

        private ArgumentDto? WriteArgument(object? argument) => WriteArgument(OperationArgument.FromRaw(argument));

        private ArgumentDto WriteConstant(Type type, object? value)
        {
            if (value is ExecutorScope builder)
                return new()
                {
                    Plan = new JsonWriteContext(builder, active).WritePlan()
                };
            if (value is not null && !IsSerializableConstantType(type))
                throw new NotSupportedException($"The constant of type {type} is instance-dependent and cannot be serialized.");
            return new()
            {
                Type = WriteType(type),
                Data = value is null ? null : JsonSerializer.SerializeToElement(value, type, CompactOptions)
            };
        }
    }

    private static void ReadVariables(PlanDto dto, ExecutorBuilder builder)
    {
        if (dto.Variables is null || dto.Variables.Count < builder.Data.Inputs.Count)
            throw new JsonException("The JSON does not contain all executor inputs.");
        HashSet<int> resultVariables = [];
        HashSet<int> localVariables = [];
        CollectVariableKinds(dto.Body ?? throw new JsonException("The executor body is missing."), resultVariables, localVariables);
        for (int i = 0; i < builder.Data.Inputs.Count; i++)
        {
            VariableDto variable = dto.Variables[i] ?? throw new JsonException($"Variable {i} is missing.");
            ValidateType(variable.Type, builder.Data.GetValueType(builder.Data.Inputs[i]), $"input {i}");
            if (variable.Published)
                builder.Data.PublishValue(builder.Data.Inputs[i], variable.Name);
            else if (variable.Name is not null)
                throw new JsonException($"Input {i} has a name but is not published.");
        }
        for (int i = builder.Data.Inputs.Count; i < dto.Variables.Count; i++)
        {
            VariableDto variable = dto.Variables[i] ?? throw new JsonException($"Variable {i} is missing.");
            Type type = ReadType(variable.Type, $"variable {i}");
            if (variable.Reference)
            {
                PlanSlot slot = new(builder.Data, type, PlanSlotKind.Reference)
                {
                    Name = variable.Name,
                    DefaultWhenMissing = variable.DefaultWhenMissing
                };
                slot.Value = new(slot);
                builder.Data.AddSlot(slot);
            }
            else if (resultVariables.Contains(i))
            {
                PlanValue value = builder.Data.CreateValue(type);
                if (variable.Published)
                    builder.Data.PublishValue(value, variable.Name);
                else if (variable.Name is not null)
                    throw new JsonException($"Variable {i} has a name but is not published.");
            }
            else
            {
                if (!localVariables.Contains(i) && !variable.Published)
                    throw new JsonException($"Variable {i} has no defining operation.");
                builder.Data.CreateLocal(type, variable.Name);
            }
        }
    }

    private static void CollectVariableKinds(SequenceDto sequence, HashSet<int> resultVariables, HashSet<int> localVariables)
    {
        foreach (ItemDto item in sequence.Items ?? throw new JsonException("The executor body has no items."))
        {
            if (item.Result is int result)
                resultVariables.Add(result);
            if (item.Kind is "localLoad" or "localStore" && item.Variable is int local)
                localVariables.Add(local);
            foreach (var child in new[] { item.Body, item.Condition, item.Setup, item.Increment, item.WhenTrue, item.WhenFalse })
            {
                if (child is not null)
                {
                    CollectVariableKinds(child, resultVariables, localVariables);
                }
            }

        }
    }

    private static void ReadItems(SequenceDto dto, ExecutorBuilder builder)
        => ReadSequence(dto, builder, builder.Data.Body);

    private static void ReadSequence(SequenceDto dto, ExecutorBuilder builder, PlanSequence target)
    {
        if (dto.Items is null)
            throw new JsonException("The executor body has no items.");
        foreach (ItemDto item in dto.Items)
        {
            if (item is null)
                throw new JsonException("An executor item is missing.");
            if (!Enum.IsDefined(item.Requirement))
                throw new JsonException($"Unknown executor requirement {item.Requirement}.");
            IPlanNode operation = ReadItem(item, builder);
            target.Add(operation, item.Requirement, item.Outputs?.Select(index => Variable.Create(builder.Data, ReadSlot(index, builder))).ToArray() ?? []);
        }
    }

    private static IPlanNode ReadItem(ItemDto dto, ExecutorBuilder builder)
        => dto.Kind switch
        {
            "declare" => new DeclarationOperation(ReadSlot(dto.Variable ?? throw new JsonException("A declaration has no variable."), builder).Local ?? throw new JsonException("A declaration requires a local.")),
            "reference" => new ReferenceOperation(ReadValue(dto.Result, builder)),
            "compose" => new CompositionNode(ReadPlan(dto.Plan ?? throw new JsonException("A composition has no plan.")).ToScope(), dto.Result is int compositionResult ? ReadValue(compositionResult, builder) : null),
            "scope" => ReadScope(dto, builder),
            "call" => ReadCall(dto, builder),

            "arrayCreate" => new ArrayCreateOperation(ReadRawArguments(dto.Operands, builder), ReadValue(dto.Result, builder)),
            "arrayRead" => new ArrayReadOperation(ReadRawArgument(dto.Array, builder), ReadRawArgument(dto.Index, builder), ReadValue(dto.Result, builder)),
            "arrayAssignment" => new ArrayAssignmentOperation(ReadRawArgument(dto.Array, builder), ReadRawArgument(dto.Index, builder), ReadRawArgument(dto.Value, builder)),
            "constant" => new CapturedValueOperation(ReadConstant(dto.Value), ReadValue(dto.Result, builder)),
            "convert" => new ConvertOperation(ReadRawArgument(dto.Value, builder), ReadValue(dto.Result, builder), dto.Checked),
            "typeAs" => new TypeAsOperation(ReadRawArgument(dto.Value, builder), ReadValue(dto.Result, builder)),
            "binary" => new BinaryOperation(ReadRawArgument(dto.Left, builder), ReadEnum<ExecutorBinaryOperator>(dto.Operator, "binary operator"), ReadRawArgument(dto.Right, builder), ReadValue(dto.Result, builder), dto.Checked),
            "compare" => new CompareOperation(ReadRawArgument(dto.Left, builder), ReadEnum<ExecutorComparison>(dto.Comparison, "comparison"), ReadRawArgument(dto.Right, builder), ReadValue(dto.Result, builder)),
            "typeTest" => new TypeTestOperation(ReadRawArgument(dto.Value, builder), ReadType(dto.Type, "type-test type"), ReadValue(dto.Result, builder)),
            "logicalNot" => new LogicalNotOperation(ReadRawArgument(dto.Value, builder), ReadValue(dto.Result, builder)),
            "unary" => new UnaryOperation(ReadRawArgument(dto.Value, builder), ReadEnum<ExecutorUnaryOperator>(dto.Operator, "unary operator"), ReadValue(dto.Result, builder), dto.Checked),
            "localLoad" => new LocalLoadOperation(ReadLocal(dto.Variable, builder), ReadValue(dto.Result, builder)),
            "localStore" => new StoreOperation(OperationArgument.FromLocal(ReadLocal(dto.Variable, builder)), ReadRawArgument(dto.Value, builder)),
            "valueStore" => new StoreOperation(OperationArgument.FromValue(ReadValue(dto.Result, builder)), ReadRawArgument(dto.Value, builder)),

            "if" => new ConditionNode(ReadRawArgument(dto.Value, builder), ReadBody(dto.WhenTrue, builder), dto.WhenFalse is null ? null : ReadBody(dto.WhenFalse, builder)),
            "foreach" => new ForEachNode(ReadRawArgument(dto.Value, builder), ReadValue(dto.Result, builder), ReadBody(dto.Body, builder)),
            "while" => new WhileNode(ReadBody(dto.Condition, builder), ReadRawArgument(dto.Value, builder), ReadBody(dto.Body, builder)),
            "doWhile" => new DoWhileNode(ReadBody(dto.Body, builder), ReadBody(dto.Condition, builder), ReadRawArgument(dto.Value, builder)),
            "for" => new ForNode(ReadBody(dto.Setup, builder), ReadBody(dto.Condition, builder), ReadRawArgument(dto.Value, builder), ReadBody(dto.Increment, builder), ReadBody(dto.Body, builder)),
            "logical" => new LogicalConditionNode(ReadRawArgument(dto.Left, builder), ReadEnum<ExecutorLogicalOperator>(dto.Operator, "logical operator"), ReadBody(dto.Body, builder), ReadRawArgument(dto.Right, builder), ReadValue(dto.Result, builder)),
            "conditional" => new ConditionalValueNode(ReadRawArgument(dto.Value, builder), ReadBody(dto.WhenTrue, builder), ReadRawArgument(dto.Left, builder), ReadBody(dto.WhenFalse, builder), ReadRawArgument(dto.Right, builder), ReadValue(dto.Result, builder)),
            "break" => new BreakOperation(),
            "continue" => new ContinueOperation(),
            "return" => new ReturnOperation(),
            _ => throw new JsonException($"Unknown executor item kind '{dto.Kind}'.")
        };

    private static CallOperation ReadCall(ItemDto dto, ExecutorBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Passing is not { } passing || passing.Length != dto.Operands?.Count || passing.Any(mode => !Enum.IsDefined(mode)))
        {
            throw new JsonException("A symbolic call has an invalid name or argument modes.");
        }
        return new(dto.Names?.ToArray() ?? [dto.Name], ReadRawArguments(dto.Operands, builder), dto.Result is null ? null : ReadValue(dto.Result, builder), passing, (dto.GenericArguments ?? []).Select(type => ReadType(type, "generic argument")).ToArray(), dto.Type is null ? null : ReadType(dto.Type, "constructor type"));
    }

    private static IPlanNode ReadScope(ItemDto dto, ExecutorBuilder builder)
    {
        PlanSequence body = new();
        ReadSequence(dto.Body ?? throw new JsonException("A scope has no body."), builder, body);
        return new ScopeNode(body, dto.Variable is null ? null : ReadSlot(dto.Variable.Value, builder));
    }

    private static PlanSequence ReadBody(SequenceDto? dto, ExecutorBuilder builder)
    {
        var body = new PlanSequence();
        ReadSequence(dto ?? throw new JsonException("An operation body is missing."), builder, body);
        return body;
    }
    private static OperationArgument ReadRawArgument(ArgumentDto? dto, ExecutorBuilder plan)
    {
        if (dto is null)
            return default;
        if (dto.Variable is int variable)
            return Variable.Create(plan.Data, ReadSlot(variable, plan));
        return OperationArgument.FromRaw(ReadConstant(dto));
    }

    private static OperationArgument[] ReadRawArguments(List<ArgumentDto>? arguments, ExecutorBuilder plan)
        => (arguments ?? throw new JsonException("A plan item is missing its operands.")).Select(x => ReadRawArgument(x, plan)).ToArray();

    private static object? ReadConstant(ArgumentDto? dto)
    {
        if (dto?.Plan is PlanDto nested)
            return ReadPlan(nested).ToScope();
        if (dto is null || dto.Type is null)
            return null;
        Type type = ReadType(dto.Type, "constant type");
        if (dto.Data is null || dto.Data.Value.ValueKind == JsonValueKind.Null)
            return null;
        if (!IsSerializableConstantType(type))
            throw new NotSupportedException($"The constant of type {type} is instance-dependent and cannot be deserialized.");
        return dto.Data.Value.Deserialize(type, CompactOptions);
    }

    private static PlanValue ReadValue(int? index, ExecutorBuilder plan)
    {
        if (index is not int value)
            throw new JsonException($"Value reference {index?.ToString() ?? "<missing>"} is invalid.");
        return ReadSlot(value, plan).Value ?? throw new JsonException($"Variable reference {value} is not a produced value.");
    }

    private static PlanLocal ReadLocal(int? index, ExecutorBuilder plan)
    {
        if (index is not int value)
            throw new JsonException($"Local reference {index?.ToString() ?? "<missing>"} is invalid.");
        return ReadSlot(value, plan).Local ?? throw new JsonException($"Variable reference {value} is not mutable storage.");
    }

    private static PlanSlot ReadSlot(int index, ExecutorBuilder plan)
    {
        if ((uint)index >= (uint)plan.Data.Slots.Count)
            throw new JsonException($"Variable reference {index} is invalid.");
        return plan.Data.GetSlot(index);
    }

    private static TEnum ReadEnum<TEnum>(string? value, string description) where TEnum : struct, Enum => value is not null && Enum.TryParse(value, out TEnum result) ? result : throw new JsonException($"Invalid {description} '{value}'.");
    private static Type ReadType(string? name, string description) => name is not null && Type.GetType(name, true) is Type type ? type : throw new JsonException($"The {description} '{name}' could not be resolved.");

    private static void ValidateType(string? serializedType, Type expected, string description)
    {
        if (ReadType(serializedType, description) != expected)
            throw new JsonException($"The {description} does not match the builder input type.");
    }

    private static bool IsSerializableConstantType(Type type)
    {
        Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType == typeof(string) || effectiveType == typeof(decimal) || effectiveType.IsPrimitive || effectiveType.IsEnum;
    }

    private static string WriteType(Type type) => type.AssemblyQualifiedName ?? throw new NotSupportedException($"Type {type} cannot be serialized.");

    private sealed class PlanDto
    {
        public int? Output
        {
            get; set;
        }
        public int InputCount
        {
            get; set;
        }
        public int Version
        {
            get; set;
        }
        public List<VariableDto>? Variables
        {
            get; set;
        }
        public SequenceDto? Body
        {
            get; set;
        }
    }
    private sealed class VariableDto
    {
        public bool Reference
        {
            get; set;
        }
        public bool DefaultWhenMissing
        {
            get; set;
        }
        public string? Type
        {
            get; set;
        }
        public string? Name
        {
            get; set;
        }
        public bool Published
        {
            get; set;
        }
    }
    private sealed class SequenceDto
    {
        public List<ItemDto>? Items
        {
            get; set;
        }
    }
    private sealed class ArgumentDto
    {
        public PlanDto? Plan
        {
            get; set;
        }
        public int? Variable
        {
            get; set;
        }
        public string? Type
        {
            get; set;
        }
        public JsonElement? Data
        {
            get; set;
        }
    }

    private sealed class ItemDto
    {
        public List<int>? Outputs
        {
            get; set;
        }
        public List<string>? Names
        {
            get; set;
        }
        public SequenceDto? Condition
        {
            get; set;
        }
        public SequenceDto? Setup
        {
            get; set;
        }
        public SequenceDto? Increment
        {
            get; set;
        }
        public SequenceDto? WhenTrue
        {
            get; set;
        }
        public SequenceDto? WhenFalse
        {
            get; set;
        }
        public PlanDto? Plan
        {
            get; set;
        }
        public string? Name
        {
            get; set;
        }
        public ExecutorArgumentPassing[]? Passing
        {
            get; set;
        }
        public List<string>? GenericArguments
        {
            get; set;
        }
        public ExecutorRequirement Requirement
        {
            get; set;
        }
        public string? Kind
        {
            get; set;
        }
        public string? Type
        {
            get; set;
        }
        public List<ArgumentDto>? Operands
        {
            get; set;
        }
        public ArgumentDto? Value
        {
            get; set;
        }
        public ArgumentDto? Left
        {
            get; set;
        }
        public ArgumentDto? Right
        {
            get; set;
        }
        public ArgumentDto? Array
        {
            get; set;
        }
        public ArgumentDto? Index
        {
            get; set;
        }
        public SequenceDto? Body
        {
            get; set;
        }
        public int? Result
        {
            get; set;
        }
        public int? Variable
        {
            get; set;
        }
        public string? Operator
        {
            get; set;
        }
        public string? Comparison
        {
            get; set;
        }
        public bool Checked
        {
            get; set;
        }
    }

}
