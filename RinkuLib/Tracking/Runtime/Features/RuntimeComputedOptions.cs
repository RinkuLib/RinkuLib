using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Adds readonly expressions evaluated against current tracked values and accepted originals.</summary>
public static class RuntimeComputedOptions {
    /// <summary>Configures a computed member backed by a getter expression.</summary>
    public static RuntimeTrackingOptions<TOriginal> Getter<TOriginal, TValue>(this RuntimeTrackingOptions<TOriginal> options, string name, Expression<Func<TOriginal, TValue>> expression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(expression);
        return options.Add(new ComputedOption<TOriginal, TValue>(name, expression));
    }

    private sealed class ComputedOption<TOriginal, TValue>(string name, Expression<Func<TOriginal, TValue>> expression) : IRuntimeTrackingOption<TOriginal> {
        public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) => type.GetOrAddMember(name, typeof(TValue)).Emitter = new RuntimeComputedEmitter<TOriginal, TValue>(expression);
    }
}

internal interface IRuntimeComputedMember {
    IReadOnlyList<string> Dependencies { get; }
}

internal sealed class RuntimeComputedEmitter<TOriginal, TValue> : RuntimeTrackingMemberEmitter<TOriginal>, IRuntimeComputedMember {
    private readonly Expression<Func<TOriginal, TValue>> _expression;
    private FieldBuilder _current = null!, _original = null!;
    public IReadOnlyList<string> Dependencies { get; }
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool UsesSnapshot => false;
    public override bool TracksChanges => true;

    internal RuntimeComputedEmitter(Expression<Func<TOriginal, TValue>> expression) {
        _expression = expression;
        var dependencies = new List<string>();
        new ReplaceMembers(expression.Parameters[0], member => {
            if (!dependencies.Contains(member.Member.Name))
                dependencies.Add(member.Member.Name);
            return Expression.Default(member.Type);
        }).Visit(expression.Body);
        Dependencies = dependencies;
    }

    protected internal override void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context) {
        Validate(context.Type.Definition, context.Member.Name, new HashSet<string>(StringComparer.Ordinal));
        _current = context.Type.TypeBuilder.DefineField("s_current_" + context.Member.Name, typeof(Func<IRuntimeMemberAccess, TValue>), FieldAttributes.Private | FieldAttributes.Static);
        _original = context.Type.TypeBuilder.DefineField("s_original_" + context.Member.Name, typeof(Func<TOriginal, TValue>), FieldAttributes.Private | FieldAttributes.Static);
        context.Type.AddInitializer(type => {
            var item = Expression.Parameter(typeof(IRuntimeMemberAccess), "item");
            Expression body = new ReplaceMembers(_expression.Parameters[0], member => Expression.PropertyOrField(Expression.Convert(item, type), member.Member.Name)).Visit(_expression.Body)!;
            var current = Expression.Lambda<Func<IRuntimeMemberAccess, TValue>>(body, item).Compile();
            type.GetField(_current.Name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, current);
            type.GetField(_original.Name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, _expression.Compile());
        });
    }

    private static void Validate(RuntimeTrackingTypeDefinition<TOriginal> definition, string name, HashSet<string> visiting) {
        if (!visiting.Add(name))
            throw new InvalidOperationException($"Computed tracking member '{name}' has a dependency cycle.");
        RuntimeTrackingMemberDefinition<TOriginal>? member = null;
        foreach (var candidate in definition.Members)
            if (candidate.Name == name) { member = candidate; break; }
        if (member is null || member.Emitter?.CanRead != true)
            throw new InvalidOperationException($"Computed tracking dependency '{name}' must have a readable tracking property.");
        if (member.Emitter is IRuntimeComputedMember computed)
            foreach (string dependency in computed.Dependencies)
                Validate(definition, dependency, visiting);
        visiting.Remove(name);
    }

    protected internal override void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) {
        il.Emit(OpCodes.Ldsfld, _current);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Func<IRuntimeMemberAccess, TValue>).GetMethod(nameof(Func<IRuntimeMemberAccess, TValue>.Invoke))!);
    }

    private void EmitOriginal(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) {
        Label available = il.DefineLabel(), done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, context.Type.HasOriginalField);
        il.Emit(OpCodes.Brtrue, available);
        LocalBuilder empty = il.DeclareLocal(typeof(TValue));
        il.Emit(OpCodes.Ldloca, empty);
        il.Emit(OpCodes.Initobj, typeof(TValue));
        il.Emit(OpCodes.Ldloc, empty);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(available);
        il.Emit(OpCodes.Ldsfld, _original);
        context.EmitLoadOriginal(il);
        il.Emit(OpCodes.Callvirt, typeof(Func<TOriginal, TValue>).GetMethod(nameof(Func<TOriginal, TValue>.Invoke))!);
        il.MarkLabel(done);
    }

    protected internal override void EmitHasChange(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) {
        EmitOriginal(context, il);
        EmitGet(context, il);
        il.Emit(OpCodes.Call, typeof(RuntimeTrackingValue).GetMethod(nameof(RuntimeTrackingValue.Different))!.MakeGenericMethod(typeof(TValue)));
    }
    protected internal override void EmitOriginalValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) {
        EmitOriginal(context, il);
        if (typeof(TValue).IsValueType) il.Emit(OpCodes.Box, typeof(TValue));
    }
    protected internal override void EmitEditValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) {
        EmitGet(context, il);
        if (typeof(TValue).IsValueType) il.Emit(OpCodes.Box, typeof(TValue));
    }

    private sealed class ReplaceMembers(ParameterExpression source, Func<MemberExpression, Expression> replace) : ExpressionVisitor {
        protected override Expression VisitMember(MemberExpression node) => node.Expression == source ? replace(node) : base.VisitMember(node);
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? throw new NotSupportedException("A computed tracking expression must use source members rather than the source instance itself.") : base.VisitParameter(node);
    }
}
