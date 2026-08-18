using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Rinku.Tracking.Runtime;

internal static class RuntimeTrackingContract<TOriginal, TEdit> where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private static readonly Type Contract = typeof(TEdit);

    internal static RuntimeTrackingOptions<TOriginal> BuildOptions(bool includeDefaultMembers = false)
        => Resolve(new RuntimeTrackingOptions<TOriginal>(includeDefaultMembers));

    internal static RuntimeTrackingOptions<TOriginal> Resolve(RuntimeTrackingOptions<TOriginal> baseOptions) {
        ArgumentNullException.ThrowIfNull(baseOptions);
        if (!Contract.IsInterface) throw new NotSupportedException($"Generated runtime contract {Contract} must be an interface.");
        if (baseOptions.IsResolvedFor(Contract)) return baseOptions;
        if (baseOptions.IsResolved)
            throw new InvalidOperationException($"RuntimeTrackingOptions are already resolved for {baseOptions.ResolvedContract} and cannot be reused as the resolved contract {Contract}.");

        RuntimeTrackingOptions<TOriginal> options = baseOptions.CloneUnfrozen();
        ApplyTypeAttributes(options);

        foreach (PropertyGroup group in PropertyGroups()) {
            bool generateGetter = false;
            bool generateSetter = false;
            foreach (ContractProperty entry in group.Properties) {
                PropertyInfo property = entry.Property;
                if (property.GetIndexParameters().Length != 0)
                    throw new NotSupportedException($"Runtime contract property {property.DeclaringType}.{property.Name} cannot be an indexer.");
                if (property.GetMethod is null)
                    throw new NotSupportedException($"Runtime contract property {property.DeclaringType}.{property.Name} is write-only. Generated members require a readable value.");
                generateGetter |= RequiresGeneration(property.GetMethod);
                generateSetter |= RequiresGeneration(property.SetMethod);
            }

            if (!generateGetter && !generateSetter) continue;
            RuntimeTrackingMemberOptions member = options.Member(group.Name, group.ValueType);

            // Original/type-wide behavior is already present. Contract member configuration overlays it:
            // base interface declarations first, most-derived declaration last.
            PropertyInfo[] declarations = group.Properties
                .OrderByDescending(static x => x.Distance)
                .ThenBy(static x => x.Property.DeclaringType!.FullName, StringComparer.Ordinal)
                .Select(static x => x.Property)
                .ToArray();

            for (int i = 0; i < declarations.Length; i++) member.AddMetadataSource(declarations[i]);

            // Signature + type convention establish defaults. Property attributes are then the most-local
            // configuration and can intentionally override those defaults.
            member.ExposeProperty = true;
            if (!generateSetter) member.ReadOnly();
            options.ApplyContractMemberConvention(new RuntimeTrackingContractMemberContext<TOriginal>(
                options, Contract, member, declarations, generateGetter, generateSetter));

            for (int i = 0; i < declarations.Length; i++)
                member.ApplyAttributes(declarations[i].GetCustomAttributes(true));

            // Strict is the final fallback, not the first action: this lets member attributes override
            // type conventions and lets a type policy deliberately Ignore + supply the member later.
            if (!member.Ignore && !member.IsConfigured) member.BindDefault();
        }

        AddImplicitMetadataStorage(options);
        options.MarkResolved(Contract);
        return options;
    }

    private static void ApplyTypeAttributes(RuntimeTrackingOptions<TOriginal> options) {
        foreach (Type source in OriginalTypeHierarchy())
            ApplyTypeAttributes(options, source);

        // Reusable policy interfaces work because attributes are collected across the whole contract graph.
        // Base/far interfaces run first; the requested contract runs last and can override them. At the
        // same specificity, Order is authoritative before type-name tie breakers.
        foreach (var distanceGroup in InterfaceGraphWithDistance()
            .GroupBy(static x => x.Distance)
            .OrderByDescending(static x => x.Key)) {
            var attributes = new List<(Type Source, IRuntimeTrackingTypeAttribute Attribute)>();
            foreach ((Type type, _) in distanceGroup)
                foreach (IRuntimeTrackingTypeAttribute attribute in type.GetCustomAttributes(false).OfType<IRuntimeTrackingTypeAttribute>())
                    attributes.Add((type, attribute));

            foreach ((Type source, IRuntimeTrackingTypeAttribute attribute) in attributes
                .OrderBy(static x => x.Attribute.Order)
                .ThenBy(static x => x.Source.FullName, StringComparer.Ordinal)
                .ThenBy(static x => x.Attribute.GetType().FullName, StringComparer.Ordinal))
                attribute.Apply(new RuntimeTrackingTypeContext<TOriginal>(options, Contract, source));
        }
    }

    private static void ApplyTypeAttributes(RuntimeTrackingOptions<TOriginal> options, Type source) {
        IRuntimeTrackingTypeAttribute[] attributes = source.GetCustomAttributes(false)
            .OfType<IRuntimeTrackingTypeAttribute>()
            .OrderBy(static x => x.Order)
            .ThenBy(static x => x.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

        if (attributes.Length == 0) return;
        var context = new RuntimeTrackingTypeContext<TOriginal>(options, Contract, source);
        for (int i = 0; i < attributes.Length; i++) attributes[i].Apply(context);
    }

    private static IEnumerable<Type> OriginalTypeHierarchy() {
        Type type = typeof(TOriginal);
        if (type.IsInterface || type.IsValueType) {
            yield return type;
            yield break;
        }

        var stack = new Stack<Type>();
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            stack.Push(current);
        while (stack.Count != 0) yield return stack.Pop();
    }

    private static IEnumerable<PropertyGroup> PropertyGroups() {
        var groups = new Dictionary<string, List<ContractProperty>>(StringComparer.OrdinalIgnoreCase);
        foreach ((Type type, int distance) in InterfaceGraphWithDistance()) {
            if (IsFrameworkContract(type)) continue;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)) {
                if (!groups.TryGetValue(property.Name, out List<ContractProperty>? list)) groups.Add(property.Name, list = []);
                list.Add(new(property, distance));
            }
        }

        foreach ((string name, List<ContractProperty> properties) in groups.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase)) {
            Type valueType = properties[0].Property.PropertyType;
            for (int i = 1; i < properties.Count; i++) {
                if (properties[i].Property.PropertyType != valueType)
                    throw new InvalidOperationException($"Runtime contract {Contract} declares incompatible '{name}' property types: {valueType} and {properties[i].Property.PropertyType}.");
            }

            // Unrelated declarations are ambiguous unless another declaration in the contract graph
            // explicitly joins them (normally a redeclaration on the derived contract).
            for (int i = 0; i < properties.Count; i++)
                for (int j = i + 1; j < properties.Count; j++) {
                    Type a = properties[i].Property.DeclaringType!;
                    Type b = properties[j].Property.DeclaringType!;
                    if (a.IsAssignableFrom(b) || b.IsAssignableFrom(a)) continue;
                    bool resolved = properties.Any(x => a.IsAssignableFrom(x.Property.DeclaringType!) && b.IsAssignableFrom(x.Property.DeclaringType!));
                    if (!resolved)
                        throw new InvalidOperationException($"Runtime contract {Contract} inherits ambiguous property '{name}' from unrelated interfaces {a} and {b}. Redeclare it on a common derived contract to make the intent explicit.");
                }

            yield return new(name, valueType, properties);
        }
    }

    // Metadata has one obvious generated storage implementation. Default interface implementations still win.
    private static void AddImplicitMetadataStorage(RuntimeTrackingOptions<TOriginal> options) {
        var readers = new HashSet<Type>();
        var writers = new HashSet<Type>();
        foreach (Type iface in InterfaceGraph()) {
            if (!iface.IsGenericType) continue;
            Type def = iface.GetGenericTypeDefinition();
            Type metadata = iface.GetGenericArguments()[0];
            if (def == typeof(IMetadataReader<>)) {
                MethodInfo getter = iface.GetProperty(nameof(IMetadataReader<object>.Metadata))!.GetMethod!;
                if (RequiresGeneration(getter)) readers.Add(metadata);
            } else if (def == typeof(IMetadataWriter<>)) {
                MethodInfo setter = iface.GetMethod(nameof(IMetadataWriter<object>.SetMetadata))!;
                if (RequiresGeneration(setter)) writers.Add(metadata);
            }
        }

        foreach (Type metadata in readers.OrderBy(static x => x.AssemblyQualifiedName, StringComparer.Ordinal)) {
            Type capability = typeof(RuntimeMetadataReaderCapability<,>).MakeGenericType(typeof(TOriginal), metadata);
            options.AddCapability((IRuntimeTrackingCapability<TOriginal>)Activator.CreateInstance(capability)!);
        }
        foreach (Type metadata in writers.OrderBy(static x => x.AssemblyQualifiedName, StringComparer.Ordinal)) {
            Type capability = typeof(RuntimeMetadataWriterCapability<,>).MakeGenericType(typeof(TOriginal), metadata);
            options.AddCapability((IRuntimeTrackingCapability<TOriginal>)Activator.CreateInstance(capability)!);
        }
    }

    internal static bool RequiresGeneration(MethodInfo? contract)
        => contract is not null && contract.IsAbstract && !RuntimeInterfaceDefaults.HasImplementation(Contract, contract);

    internal static void ValidateRequirements(RuntimeTrackingCapabilityBuilder builder) {
        foreach (Type iface in InterfaceGraph()) {
            foreach (MethodInfo method in iface.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                if (method.IsStatic || !method.IsAbstract) continue;
                if (builder.IsImplemented(method)) continue;
                if (RuntimeInterfaceDefaults.HasImplementation(Contract, method)) continue;
                throw new InvalidOperationException(
                    $"Runtime contract {Contract} requires {Format(method)}, but no implementation was resolved. " +
                    "Provide a default interface implementation or supply the behavior through RuntimeTrackingOptions.");
            }
        }
    }

    internal static PropertyInfo? FindProperty(string name, Type propertyType) {
        foreach (PropertyGroup group in PropertyGroups()) {
            if (!string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase) || group.ValueType != propertyType) continue;
            return group.Properties.OrderBy(static x => x.Distance).ThenBy(static x => x.Property.DeclaringType!.FullName, StringComparer.Ordinal).First().Property;
        }
        return null;
    }

    internal static IEnumerable<Type> CustomContracts() {
        foreach (Type type in InterfaceGraph()) if (!IsFrameworkContract(type)) yield return type;
    }

    internal static IEnumerable<Type> InterfaceGraph() => InterfaceGraphWithDistance().Select(static x => x.Type);

    private static IEnumerable<(Type Type, int Distance)> InterfaceGraphWithDistance() {
        var distances = new Dictionary<Type, int> { [Contract] = 0 };
        var pending = new Queue<Type>();
        pending.Enqueue(Contract);
        while (pending.Count != 0) {
            Type current = pending.Dequeue();
            int nextDistance = distances[current] + 1;
            foreach (Type inherited in DirectInterfaces(current).OrderBy(static x => x.FullName, StringComparer.Ordinal)) {
                if (distances.TryGetValue(inherited, out int existing) && existing <= nextDistance) continue;
                distances[inherited] = nextDistance;
                pending.Enqueue(inherited);
            }
        }
        foreach ((Type type, int distance) in distances.OrderBy(static x => x.Value).ThenBy(static x => x.Key.FullName, StringComparer.Ordinal))
            yield return (type, distance);
    }

    private static IEnumerable<Type> DirectInterfaces(Type type) {
        Type[] all = type.GetInterfaces();
        for (int i = 0; i < all.Length; i++) {
            bool inheritedThroughAnother = false;
            for (int j = 0; j < all.Length; j++) {
                if (i == j) continue;
                if (all[i].IsAssignableFrom(all[j])) { inheritedThroughAnother = true; break; }
            }
            if (!inheritedThroughAnother) yield return all[i];
        }
    }

    private static bool IsFrameworkContract(Type type) {
        if (type == typeof(INotifyPropertyChanged)) return true;
        return type.Assembly == typeof(IRuntimeTrackingItem<>).Assembly &&
            type.Namespace is string ns && (ns == "Rinku.Tracking" || ns == "Rinku.Tracking.Runtime");
    }

    private static string Format(MethodInfo method)
        => $"{method.DeclaringType}.{method.Name}({string.Join(", ", Array.ConvertAll(method.GetParameters(), static p => p.ParameterType.Name))})";

    private sealed record ContractProperty(PropertyInfo Property, int Distance);
    private sealed record PropertyGroup(string Name, Type ValueType, List<ContractProperty> Properties);
}

internal static class RuntimeInterfaceDefaults {
    internal static bool HasImplementation(Type exposedContract, MethodInfo requirement) {
        if (!requirement.IsAbstract) return true;
        foreach (Type iface in Graph(exposedContract)) {
            foreach (MethodInfo candidate in iface.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                if (candidate.IsStatic || candidate.IsAbstract) continue;
                if (ReferenceEquals(candidate, requirement) || candidate == requirement) return true;
                Type? requiredBy = requirement.DeclaringType;
                Type? implementedBy = candidate.DeclaringType;
                if (requiredBy is null || implementedBy is null || !requiredBy.IsAssignableFrom(implementedBy)) continue;
                bool matchingName = candidate.Name == requirement.Name ||
                    (candidate.Name.IndexOf('.') >= 0 && candidate.Name.EndsWith('.' + requirement.Name, StringComparison.Ordinal));
                if (matchingName && SameSignature(candidate, requirement)) return true;
            }
        }
        return false;
    }

    private static IEnumerable<Type> Graph(Type root) {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(root);
        while (pending.Count != 0) {
            Type current = pending.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (Type inherited in current.GetInterfaces()) pending.Push(inherited);
        }
    }

    private static bool SameSignature(MethodInfo x, MethodInfo y) {
        if (x.ReturnType != y.ReturnType || x.GetGenericArguments().Length != y.GetGenericArguments().Length) return false;
        ParameterInfo[] xp = x.GetParameters();
        ParameterInfo[] yp = y.GetParameters();
        if (xp.Length != yp.Length) return false;
        for (int i = 0; i < xp.Length; i++) if (xp[i].ParameterType != yp[i].ParameterType) return false;
        return true;
    }
}
