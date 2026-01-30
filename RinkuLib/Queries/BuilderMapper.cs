using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace RinkuLib.Queries;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ForBoolCondAttribute : Attribute;
public delegate void QueryFillAction<T, TBuilder>(ref T instance, TBuilder builder);
/// <summary>
/// Provides high-performance extension methods to populate an <see cref="IQueryBuilder"/> 
/// from the properties and fields of a source object.
/// </summary>
public static class QueryBuilderExtensions {
    public static QueryBuilder<QueryCommand> StartBuilder<T>(this QueryCommand command, ref T value) {
        var builder = new QueryBuilder<QueryCommand>(command);
        builder.Use(ref value);
        return builder;
    }
    public static QueryBuilderCommand<QueryCommand, TCmd> StartBuilder<TCmd, T>(this QueryCommand command, TCmd cmd, ref T value) where TCmd : IDbCommand {
        var builder = new QueryBuilderCommand<QueryCommand, TCmd>(command, cmd);
        builder.Use(ref value);
        return builder;
    }
    public static QueryBuilder<QueryCommand> StartBuilder<T>(this QueryCommand command, T value) {
        var builder = new QueryBuilder<QueryCommand>(command);
        builder.Use(ref value);
        return builder;
    }
    public static QueryBuilderCommand<QueryCommand, TCmd> StartBuilder<TCmd, T>(this QueryCommand command, TCmd cmd, T value) where TCmd : IDbCommand {
        var builder = new QueryBuilderCommand<QueryCommand, TCmd>(command, cmd);
        builder.Use(ref value);
        return builder;
    }
    /// <summary>
    /// Activates variables in the builder using the members of a source struct by reference, 
    /// avoiding unnecessary memory copies.
    /// </summary>
    /// <param name="builder">The query builder to populate.</param>
    /// <param name="value">The source struct to read from.</param>
    /// <remarks>
    /// Uses a cached IL-generated delegate. If <typeparamref name="T"/> implements 
    /// <see cref="IQueryFillable"/>, the custom Fill logic is used instead.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Use<TBuilder, T>(this TBuilder builder, ref T value) where TBuilder : IQueryBuilder {
        if (typeof(T).IsValueType)
            Unsafe.As<StructFillAction<T, TBuilder>>(BuilderMapper<T, TBuilder>.FillDelegate)(ref value, builder);
        else
            Unsafe.As<ClassFillAction<T, TBuilder>>(BuilderMapper<T, TBuilder>.FillDelegate)(value, builder);
    }

    /// <summary>
    /// Activates variables in the builder using the members of a source object or struct.
    /// </summary>
    /// <remarks>
    /// For structs, consider using the <c>ref</c> overload to prevent copying the value onto the stack.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Use<TBuilder, T>(this TBuilder builder, T value) where TBuilder : IQueryBuilder {
        if (typeof(T).IsValueType)
            Unsafe.As<StructFillAction<T, TBuilder>>(BuilderMapper<T, TBuilder>.FillDelegate)(ref value, builder);
        else
            Unsafe.As<ClassFillAction<T, TBuilder>>(BuilderMapper<T, TBuilder>.FillDelegate)(value, builder);
    }
}

internal delegate void StructFillAction<T, TBuilder>(ref T instance, TBuilder builder);
internal delegate void ClassFillAction<T, TBuilder>(T instance, TBuilder builder);
/// <summary>
/// An internal mapper that compiles and caches a high-performance delegate to transfer 
/// data from type <typeparamref name="T"/> to an <see cref="IQueryBuilder"/>.
/// </summary>
public static class BuilderMapper<T, TBuilder> where TBuilder : IQueryBuilder {
    /// <summary>
    /// The cached delegate (either <see cref="StructFillAction{T, TBuilder}"/> 
    /// or <see cref="ClassFillAction{T, TBuilder}"/>).
    /// </summary>
    internal static readonly object FillDelegate = CreateAction();
    private static object CreateAction() {
        Type typeT = typeof(T);
        Type typeBuilder = typeof(TBuilder);
        bool isStruct = typeT.IsValueType;
        bool builderIsStruct = typeBuilder.IsValueType;

        var dynamicMethod = new DynamicMethod("Fill_" + typeT.Name, null, [isStruct ? typeT.MakeByRefType() : typeT, typeBuilder], typeT.Module, true);
        var il = dynamicMethod.GetILGenerator();
        var useMethod = typeof(IQueryBuilder).GetMethod(nameof(IQueryBuilder.Use), [typeof(string), typeof(object)])!;
        var useBoolCondMethod = typeof(IQueryBuilder).GetMethod(nameof(IQueryBuilder.Use), [typeof(string)])!;
        var boolCondAttrType = typeof(ForBoolCondAttribute);

        HashSet<string> seenNames = [];
        var valLocal = il.DeclareLocal(typeof(object));

        foreach (var member in typeT.GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
            if (!seenNames.Add(member.Name))
                continue;

            Type mType;
            MethodInfo? getMethod = null;
            FieldInfo? fieldInfo = null;

            if (member is PropertyInfo p && p.CanRead) { mType = p.PropertyType; getMethod = p.GetGetMethod(); }
            else if (member is FieldInfo f) { mType = f.FieldType; fieldInfo = f; }
            else continue;
            bool isBoolCond = member.IsDefined(boolCondAttrType, inherit: true);
            if (isBoolCond && mType != typeof(bool))
                throw new InvalidOperationException($"Member '{member.Name}' has [ForBoolCond] but is not a bool. Type: {mType.Name}");

            Label skipLabel = il.DefineLabel();

            // 1. Load the instance
            il.Emit(OpCodes.Ldarg_0);

            // 2. Load the member value
            if (fieldInfo is not null)
                il.Emit(OpCodes.Ldfld, fieldInfo);
            else
                il.Emit(isStruct ? OpCodes.Call : OpCodes.Callvirt, getMethod!);

            // 3. Null handling (Stack management)
            if (!mType.IsValueType) {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse_S, skipLabel);
            }
            else if (Nullable.GetUnderlyingType(mType) != null) {
                var loc = il.DeclareLocal(mType);
                il.Emit(OpCodes.Stloc, loc);
                il.Emit(OpCodes.Ldloca_S, loc);
                il.Emit(OpCodes.Call, mType.GetProperty("HasValue")!.GetGetMethod()!);
                il.Emit(OpCodes.Brfalse_S, skipLabel);
                il.Emit(OpCodes.Ldloc, loc);
            }
            if (isBoolCond) {
                il.Emit(OpCodes.Brfalse_S, skipLabel);
            }
            else {
                // 4. Box and Store
                il.Emit(OpCodes.Box, mType);
                il.Emit(OpCodes.Stloc, valLocal);
            }

            // 5. Load Builder (Handle Ref vs Value type)
            if (builderIsStruct)
                il.Emit(OpCodes.Ldarga_S, 1);
            else
                il.Emit(OpCodes.Ldarg_1);
            var varName = IQueryCommand.DefaultVariableChar == default || isBoolCond ? member.Name : IQueryCommand.DefaultVariableChar + member.Name;
            il.Emit(OpCodes.Ldstr, varName);
            if (!isBoolCond)
                il.Emit(OpCodes.Ldloc, valLocal);

            // 6. Call Use
            if (builderIsStruct)
                il.Emit(OpCodes.Constrained, typeBuilder);
            il.Emit(OpCodes.Callvirt, isBoolCond ? useBoolCondMethod : useMethod);
            if (!isBoolCond && mType.IsValueType)
                il.Emit(OpCodes.Pop); // Clear return of Use() is struct else let the after label handle it

            il.MarkLabel(skipLabel);
            if (!mType.IsValueType)
                il.Emit(OpCodes.Pop); // If we branched here from a RefType null check, there is still a 'null' on the stack from Dup
        }

        il.Emit(OpCodes.Ret);
        return dynamicMethod.CreateDelegate(isStruct ? typeof(StructFillAction<T, TBuilder>) : typeof(ClassFillAction<T, TBuilder>));
    }
}