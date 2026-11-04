using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;

namespace Rinku;

internal static partial class ExecutorCompiler
{
    internal static int _nextAssemblyId;

    internal static TExecutor Build<TExecutor>(ExecutorBuilder plan, ExecutorBuildConfigurationSnapshot registry) where TExecutor : IExecutor
    {
        Type generatedType = BuildType(plan, registry, typeof(TExecutor) == typeof(IExecutor) ? null : typeof(TExecutor));
        return (TExecutor)(Activator.CreateInstance(generatedType) ?? throw new InvalidOperationException("The generated executor could not be created."));
    }

    internal static Func<TExecutor> Compile<TExecutor>(ExecutorBuilder plan, ExecutorBuildConfigurationSnapshot registry) where TExecutor : IExecutor
    {
        Type generatedType = BuildType(plan, registry, typeof(TExecutor) == typeof(IExecutor) ? null : typeof(TExecutor));
        return Expression.Lambda<Func<TExecutor>>(Expression.New(generatedType)).Compile();
    }

    internal static Type BuildType(ExecutorBuilder plan, ExecutorBuildConfigurationSnapshot registry, Type? requestedInterface = null)
    {
        List<CompiledEntry> entries = [];
        Type[] interfaces = (requestedInterface is null ? registry.ExecutorInterfaces : registry.ExecutorInterfaces.Prepend(requestedInterface)).Concat(plan.Data.MethodContracts).Distinct().ToArray();
        if (interfaces.Length == 0)
            throw new ArgumentException("Compilation requires at least one explicitly requested executor interface.");
        var signatures = interfaces.SelectMany(ExecutorType.GetSignatures).ToArray();
        foreach (var signature in signatures)
        {
            CompiledEntry? existing = entries.FirstOrDefault(entry => entry.Name == signature.Method.Name && entry.Entry.ReturnType == signature.Method.ReturnType && entry.Entry.InputTypes.SequenceEqual(signature.Inputs));
            if (existing is null)
            {
                ExecutorScope source = plan.Data.Methods.TryGetValue((signature.Method.Name, signature.Inputs.Length), out var methodPlan) ? methodPlan : signature.Method.Name == nameof(IExecutor<object>.Execute) ? plan.Data : throw new ArgumentException($"No plan was recorded for {signature.Method}.");
                if (signature.Inputs.Length != source.Inputs.Count)
                    throw new ArgumentException("Every executor signature must have the same number of inputs as the plan.");
                foreach (Type input in signature.Inputs)
                {
                    ValidatePlanType(input, "executor input");
                }
                ValidatePlanType(signature.Method.ReturnType, "executor result");
                ExecutorScope compilation = CloneScope(source);
                var instructions = compilation.Body;
                OperationBindingContext binding = new(compilation, registry, signature.Inputs);
                MethodInfo[] contracts = signatures.Where(other => other.Method.Name == signature.Method.Name && other.Method.ReturnType == signature.Method.ReturnType && other.Inputs.SequenceEqual(signature.Inputs)).Select(other => other.Method).Distinct().ToArray();
                binding.Body = binding.BindPlan(instructions, contracts);
                existing = new(signature.Method.Name, new(signature.Inputs, signature.Method.ReturnType), binding);
                entries.Add(existing);
            }
            existing.Methods.Add(signature.Method);
        }
        ExecutorTypeBuild build = new(entries, interfaces);
        Type generatedType = build.EmitType();
        build.PopulateStaticFields();
        return generatedType;
    }

    private sealed record CompiledEntry(string Name, ExecutorEntry Entry, OperationBindingContext Binding)
    {
        internal HashSet<MethodInfo> Methods { get; } = [];
    }

    internal static void ValidatePlanType(Type type, string description)
    {
        if (type.ContainsGenericParameters)
            throw new InvalidOperationException($"The {description} type {type} must be closed before an executor is built.");
    }

    private sealed class ExecutorTypeBuild
    {
        private readonly IReadOnlyList<CompiledEntry> entries;
        private readonly IReadOnlyList<Type> interfaces;
        private readonly Dictionary<OperationArgument, FieldBuilder> constantFields = new(OperationArgumentComparer.Instance);
        private readonly Dictionary<PlanSlot, FieldBuilder> capturedFields = [];
        private readonly Dictionary<(string? Name, PlanSlot? Slot, Type Type), FieldBuilder> instanceFields = [];
        private readonly Dictionary<string, Type> entryFieldTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<BoundDelegateOperation, FieldBuilder> delegates = [];

        private readonly TypeBuilder type = CreateType();
        private readonly List<(string Name, object? Value)> fieldValues = [];
        private readonly HashSet<string> accessAssemblies = new(StringComparer.Ordinal);
        private Type? createdType;

        internal ExecutorTypeBuild(IReadOnlyList<CompiledEntry> entries, IReadOnlyList<Type> interfaces)
        {
            this.entries = entries;
            this.interfaces = interfaces;
        }

        internal Type EmitType()
        {
            foreach (Type contract in interfaces.Concat(entries.SelectMany(entry => entry.Methods.Select(method => method.DeclaringType!))).Distinct())
            {
                AllowAccess(contract);
                type.AddInterfaceImplementation(contract);
            }
            foreach (CompiledEntry entry in entries)
            {
                entryFieldTypes.Clear();
                PrepareCaptures(entry.Binding.Body, entry.Binding);
                EmitDirectEntry(entry);
            }
            ConstructorBuilder constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            ILGenerator initialize = constructor.GetILGenerator();
            initialize.Emit(OpCodes.Ldarg_0);
            initialize.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            foreach (var callback in delegates)
            {
                initialize.Emit(OpCodes.Ldarg_0);
                initialize.Emit(OpCodes.Ldarg_0);
                initialize.Emit(OpCodes.Ldftn, callback.Key.Method!);
                initialize.Emit(OpCodes.Newobj, callback.Key.DelegateType.GetConstructor([typeof(object), typeof(IntPtr)])!);
                initialize.Emit(OpCodes.Stfld, callback.Value);
            }
            initialize.Emit(OpCodes.Ret);
            return createdType = type.CreateType() ?? throw new InvalidOperationException("The generated executor type could not be created.");
        }

        internal void PopulateStaticFields()
        {
            Type generatedType = createdType ?? throw new InvalidOperationException("The generated type has not been created.");
            foreach ((string name, object? value) in fieldValues)
            {
                FieldInfo field = generatedType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException($"Generated constant field {name} is missing.");
                field.SetValue(null, value);
            }
        }

        internal static TypeBuilder CreateType()
        {
            AssemblyName assemblyName = new($"Rinku.Executor.{Interlocked.Increment(ref _nextAssemblyId)}");
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name ?? throw new InvalidOperationException("The dynamic assembly has no name."));
            return module.DefineType($"Executor_{Guid.NewGuid():N}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        }

        private void AllowAccess(Type referenced) {
            if (referenced.HasElementType) { AllowAccess(referenced.GetElementType()!); return; }
            if (referenced.IsGenericType) {
                foreach (Type argument in referenced.GetGenericArguments()) AllowAccess(argument);
                referenced = referenced.GetGenericTypeDefinition();
            }
            if (referenced.IsVisible) return;
            string name = referenced.Assembly.GetName().Name!;
            if (accessAssemblies.Add(name))
                ((AssemblyBuilder)type.Assembly).SetCustomAttribute(new CustomAttributeBuilder(typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)])!, [name]));
        }

        private FieldBuilder ConstantField(OperationArgument argument)
        {
            if (constantFields.TryGetValue(argument, out FieldBuilder? field))
            {
                return field;
            }
            AllowAccess(argument.Type);
            field = type.DefineField($"constant_{constantFields.Count}", argument.Type, FieldAttributes.Private | FieldAttributes.Static);
            constantFields.Add(argument, field);
            fieldValues.Add((field.Name, argument.Data));
            return field;
        }

        private void EmitDirectEntry(CompiledEntry compiled)
        {
            ExecutorEntry entry = compiled.Entry;
            MethodBuilder method = type.DefineMethod(compiled.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig, entry.ReturnType, entry.InputTypes.ToArray());
            for (int index = 0; index < entry.InputTypes.Count; index++)
            {
                method.DefineParameter(index + 1, ParameterAttributes.None, $"value{index + 1}");
            }
            foreach (MethodInfo contract in compiled.Methods)
            {
                type.DefineMethodOverride(method, contract);
            }
            EmitBody(method, compiled.Binding, entry, 0);
        }

        private void PrepareCaptures(PlanSequence sequence, OperationBindingContext binding)
        {
            foreach (Type referenced in binding.AccessTypes) AllowAccess(referenced);
            foreach (var field in binding.InstanceFields)
            {
                if (field.Value.Name is { } name) {
                    if (entryFieldTypes.TryGetValue(name, out var previousType) && previousType != field.Value.Type)
                        throw new InvalidOperationException($"Instance field {name} has conflicting types within one execution signature.");
                    entryFieldTypes[name] = field.Value.Type;
                }
                var key = (field.Value.Name, field.Value.Name is null ? field.Key : null, field.Value.Type);
                if (!instanceFields.TryGetValue(key, out var storage))
                {
                    storage = type.DefineField($"state_{instanceFields.Count}", field.Value.Type, FieldAttributes.Private);
                    instanceFields.Add(key, storage);
                }
                capturedFields[field.Key] = storage;
            }
            foreach (PlanInstruction instruction in sequence.Items)
            {
                if (instruction.Node is BoundDelegateOperation callback)
                {
                    for (int index = 0; index < callback.Captures.Length; index++)
                    {
                        OperationArgument capture = callback.Captures[index];
                        if (capture.Variable is not Variable variable)
                        {
                            capturedFields.Add(callback.Binding.Plan.Inputs[index].Slot, ConstantField(capture));
                            continue;
                        }
                        if (!capturedFields.TryGetValue(variable.Slot, out FieldBuilder? field))
                        {
                            field = type.DefineField($"capture_{capturedFields.Count}", binding.TypeOf(capture), FieldAttributes.Private);
                            capturedFields.Add(variable.Slot, field);
                        }
                        capturedFields.Add(callback.Binding.Plan.Inputs[index].Slot, field);
                    }
                    PrepareCaptures(callback.Binding.Body, callback.Binding);
                }
                else if (instruction.Node is IPlanGroup group)
                {
                    foreach (PlanSequence child in group.Sequences)
                    {
                        PrepareCaptures(child, binding);
                    }
                }
            }
        }

        private void EmitBody(MethodBuilder method, OperationBindingContext binding, ExecutorEntry entry, int captureCount)
        {
            new OperationEmissionContext(method.GetILGenerator(), binding.Plan, binding.Body, ConstantField, entry, binding, capturedFields, DelegateField, captureCount).EmitEntry();
        }

        private FieldBuilder DelegateField(BoundDelegateOperation callback)
        {
            if (delegates.TryGetValue(callback, out FieldBuilder? field))
            {
                return field;
            }
            AllowAccess(callback.DelegateType);
            field = type.DefineField($"delegate_{delegates.Count}", callback.DelegateType, FieldAttributes.Private | FieldAttributes.InitOnly);
            delegates.Add(callback, field);
            MethodInfo signature = callback.DelegateType.GetMethod(nameof(Action.Invoke))!;
            Type[] inputs = signature.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            MethodBuilder method = type.DefineMethod($"lambda_{delegates.Count}", MethodAttributes.Private | MethodAttributes.HideBySig, signature.ReturnType, inputs);
            EmitBody(method, callback.Binding, new(inputs, signature.ReturnType), callback.Captures.Length);
            callback.Method = method;
            return field;
        }
    }

    internal readonly record struct ExecutorEntry(IReadOnlyList<Type> InputTypes, Type ReturnType);

    internal readonly record struct ValueLocation(Type Type, LocalBuilder? Local, int ArgumentIndex, FieldBuilder? Field = null);
}
