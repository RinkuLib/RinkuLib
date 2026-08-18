using System;
using System.Collections.Generic;
using System.ComponentModel;
using Rinku;
using Rinku.Querying.Parameters;
using Rinku.Tracking;
using Rinku.Tracking.Runtime;
using Xunit;

namespace Rinku.Tracking.Tests;

public sealed class Employee {
    public int Number { get; set; }
    [DisplayName("Original name")]
    public string? Name { get; set; }
    public string? Department;
    public string Code { get; private set; } = "A";
    public void ChangeCode(string value) => Code = value;
}

[IncludeOriginalMembers]
[RuntimeDynamicAccess]
public sealed class TypeConfiguredEmployee {
    public int Number { get; set; }
    public string? Name { get; set; }
}

public interface ITypeConfiguredEmployeeEdit : IRuntimeTrackingItem<TypeConfiguredEmployee> {
    string? Name { get; set; }
}

public sealed class EqualRow(int id, string value) {
    public int Id { get; } = id;
    public string Value { get; } = value;
    public override bool Equals(object? obj) => obj is EqualRow other && Id == other.Id;
    public override int GetHashCode() => Id;
}

public sealed class ObservableRow : INotifyPropertyChanged {
    private PropertyChangedEventHandler? _changed;
    public int AddCount { get; private set; }
    public int RemoveCount { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged {
        add { AddCount++; _changed += value; }
        remove { RemoveCount++; _changed -= value; }
    }
    public void Raise(string propertyName) => _changed?.Invoke(this, new(propertyName));
}

public readonly record struct EmployeeProjection(string? Name) : IFromOriginal<Employee, EmployeeProjection> {
    public static EmployeeProjection Create(Employee original) => new(original.Name);
}

public interface IEmployeeEdit : IRuntimeTrackingItem<Employee> {
    [DisplayName("Interaction name")]
    string? Name { get; set; }
    [RuntimeValue] bool Selected { get; set; }
}

public interface INotifyEmployeeEdit : IRuntimeTrackingItem<Employee>, INotifyPropertyChanged {
    string? Name { get; set; }
}

public interface IDynamicEmployeeEdit : IRuntimeTrackingItem<Employee>, IRuntimeMemberAccess {
    string? Name { get; set; }
}

public interface IAliasEmployeeEdit : IRuntimeTrackingItem<Employee> {
    [BindTo(nameof(Employee.Name))]
    string? DisplayName { get; set; }
    [ReadFrom(nameof(Employee.Code)), WriteWith(typeof(Employee), nameof(Employee.ChangeCode))]
    string Code { get; set; }
}

public interface IBadEmployeeEdit : IRuntimeTrackingItem<Employee> {
    string? Nmae { get; set; }
}

public interface IDefaultMethodEmployeeEdit : IRuntimeTrackingItem<Employee> {
    string? Name { get; set; }
    int Magic() => 42;
}

public interface IStateEmployeeEdit : IRuntimeTrackingItem<Employee>, IMetadata<bool> {
    string? Name { get; set; }
    [RuntimeValue] bool Selected { get; set; }
}

public interface ILeftAlias { string? Alias { get; set; } }
public interface IRightAlias { string? Alias { get; set; } }
public interface IAmbiguousAliasEdit : IRuntimeTrackingItem<Employee>, ILeftAlias, IRightAlias { }
public interface IResolvedAliasEdit : IRuntimeTrackingItem<Employee>, ILeftAlias, IRightAlias {
    [BindTo(nameof(Employee.Name))]
    new string? Alias { get; set; }
}


[IncludeOriginalMembers]
[RuntimeNotifications]
public interface IOriginalSurfacePolicy { }

public interface IOverlayEmployeeEdit : IRuntimeTrackingItem<Employee>, IRuntimeMemberAccess, IOriginalSurfacePolicy {
    [TrackingReadOnly] string? Name { get; }
}

public interface IParameterEmployeeEdit : IRuntimeTrackingItem<Employee> {
    [ParameterName("EmployeeName")]
    string? Name { get; set; }
    [RuntimeValue, RuntimeParameter]
    bool Selected { get; set; }
}

[RuntimeParameters(false)]
public interface ISelectiveParameterEmployeeEdit : IRuntimeTrackingItem<Employee> {
    [RuntimeParameter]
    string? Name { get; set; }
    int Number { get; set; }
}

public sealed class RuntimeValuesWhenUnmatchedAttribute : RuntimeTrackingTypeAttribute {
    public override void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type) =>
        type.Options.ContractMembers(static member => {
            if (!member.Member.IsConfigured && !member.Member.TryBindDefault()) member.Member.RuntimeValue();
        });
}

[RuntimeValuesWhenUnmatched]
public interface IConventionEmployeeEdit : IRuntimeTrackingItem<Employee> {
    string? Name { get; set; }
    bool Selected { get; set; }
}

public sealed class HideNumberFromRuntimeAttribute : RuntimeTrackingTypeAttribute {
    public override void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type) {
        RuntimeTrackingMemberOptions? number = type.Options.FindMember(nameof(Employee.Number));
        if (number is not null) number.IncludeInRuntimeAccess = false;
    }
}

[IncludeOriginalMembers(Order = 0)]
[HideNumberFromRuntime(Order = 1)]
[RuntimeDynamicAccess(Order = 2)]
public interface IFilteredRuntimePolicy { }

public interface IFilteredRuntimeEmployeeEdit : IRuntimeTrackingItem<Employee>, IFilteredRuntimePolicy {
    string? Name { get; set; }
}

public sealed class ManualEmployeeEdit : IEmployeeEdit {
    private readonly Employee _original;
    private string? _name;
    public ManualEmployeeEdit(Employee original) => _original = original;
    public string? Name { get => IsEditing ? _name : _original.Name; set { EnsureEditing(); _name = value; } }
    public bool Selected { get; set; }
    public bool HasOriginal => true;
    public bool IsEditing { get; private set; }
    public bool TryGetOriginal(out Employee original) { original = _original; return true; }
    public bool EnsureEditing() { if (IsEditing) return false; _name = _original.Name; IsEditing = true; return true; }
    public bool CommitEdit() { if (!IsEditing) return false; _original.Name = _name; IsEditing = false; return true; }
    public bool CancelEdit() { if (!IsEditing) return false; IsEditing = false; return true; }
}

public sealed class HandwrittenRuntimeEdit : IRuntimeTrackingItem<Employee>, IFromOriginal<Employee, HandwrittenRuntimeEdit> {
    private readonly Employee _original;
    private HandwrittenRuntimeEdit(Employee original) => _original = original;
    public static HandwrittenRuntimeEdit Create(Employee original) => new(original);
    public bool WasGenerated => false;
    public bool HasOriginal => true;
    public bool IsEditing => false;
    public bool TryGetOriginal(out Employee original) { original = _original; return true; }
    public bool EnsureEditing() => false;
    public bool CommitEdit() => false;
    public bool CancelEdit() => false;
}

public sealed class TrackingCapabilityTests {
    [Fact]
    public void Structural_list_tracks_add_remove_restore_without_item_capabilities() {
        var list = new TrackingList<int>([1, 2]);
        list.Add(3);
        list.Remove(1);

        Assert.Equal(new[] { 2, 3 }, list.AsSpan().ToArray());
        Assert.Equal(new[] { 1 }, list.RemovedSpan.ToArray());
        Assert.Equal(new[] { 3 }, list.Added);

        list.Restore(1, 0);
        Assert.Equal(new[] { 1, 2, 3 }, list.AsSpan().ToArray());
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Null_is_normal_collection_data_when_T_allows_it() {
        var list = new TrackingList<string?>(new string?[] { null, "A" });
        Assert.True(list.Remove(null));
        Assert.Single(list.Removed);
        Assert.Null(list.Removed[0]);

        list.Add(null);
        Assert.Empty(list.Removed);
        Assert.Empty(list.Added);
        Assert.Null(list[1]);
    }

    [Fact]
    public void Raw_AddNew_factory_may_create_null_when_null_is_valid_collection_data() {
        var list = new TrackingList<string?> { NewItemFactory = static () => null };
        string? added = list.AddNew();
        Assert.Null(added);
        Assert.Null(list[0]);
        Assert.Single(list.Added);
    }

    [Fact]
    public void Equal_remove_then_add_cancels_the_structural_delta_and_keeps_new_instance_active() {
        var original = new EqualRow(1, "old");
        var replacement = new EqualRow(1, "new");
        var list = new TrackingList<EqualRow>([original]);

        list.Remove(original);
        list.Add(replacement);

        Assert.False(list.HasStructuralChanges);
        Assert.Same(replacement, list[0]);
    }

    [Fact]
    public void Equal_rows_keep_per_row_origin_instead_of_guessing_from_Added() {
        var baseline = new EqualRow(1, "baseline");
        var added = new EqualRow(1, "added");
        var list = new TrackingList<EqualRow>([baseline]);

        list.Add(added);
        list.RemoveAt(0);

        Assert.Single(list);
        Assert.Same(added, list[0]);
        Assert.Single(list.Added);
        Assert.Same(added, list.Added[0]);
        Assert.Single(list.Removed);
        Assert.Same(baseline, list.Removed[0]);
    }

    [Fact]
    public void Added_is_a_current_order_view_and_moves_keep_origin_with_the_row() {
        var list = new TrackingList<int>([1, 2, 3]);
        list.Insert(1, 10);
        list.Add(20);

        Assert.Equal(new[] { 10, 20 }, list.Added);

        list.Move(4, 0);
        Assert.Equal(new[] { 20, 1, 10, 2, 3 }, list);
        Assert.Equal(new[] { 20, 10 }, list.Added);

        list.Move(2, 4);
        Assert.Equal(new[] { 20, 1, 2, 3, 10 }, list);
        Assert.Equal(new[] { 20, 10 }, list.Added);
    }

    [Fact]
    public void HasOriginal_is_authoritative_and_requires_no_list_provenance() {
        var existing = new ManualEmployeeEdit(new Employee { Name = "existing" });
        var baseline = new ManualEmployeeEdit(new Employee { Name = "baseline" });
        var list = new TrackingList<IEmployeeEdit>([baseline]);

        list.Add(existing);
        Assert.Empty(list.Added);

        list.Move(1, 0);
        Assert.Same(existing, list[0]);
        Assert.Empty(list.Added);
    }

    [Fact]
    public void New_generated_item_gets_an_original_when_CommitEdit_succeeds() {
        TrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToTrackingList<Employee, IEmployeeEdit>();
        IEmployeeEdit item = list.AddNew();

        item.Name = "new";
        Assert.False(item.HasOriginal);
        Assert.True(item.CommitEdit());
        Assert.True(item.HasOriginal);
        Assert.True(item.TryGetOriginal(out Employee? original));
        Assert.Equal("new", original!.Name);
        Assert.False(item.IsEditing);
        Assert.Empty(list.Added);
    }

    [Fact]
    public void New_item_CommitEdit_creates_the_local_original_then_normal_edit_cancel_uses_it() {
        TrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToTrackingList<Employee, IEmployeeEdit>();
        IEmployeeEdit item = list.AddNew();

        item.Name = "A";
        item.CommitEdit();
        item.Name = "B";
        item.CancelEdit();

        Assert.Equal("A", item.Name);
        Assert.True(item.HasOriginal);
        Assert.Empty(list.Added);
    }

    [Fact]
    public void CommitStructuralChanges_does_not_override_item_owned_origin() {
        TrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToTrackingList<Employee, IEmployeeEdit>();
        IEmployeeEdit item = list.AddNew();

        list.CommitStructuralChanges();
        Assert.Single(list.Added);
        Assert.False(item.HasOriginal);

        list.RemoveAt(0);
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Moving_pending_AddNew_keeps_CancelNew_bound_to_the_row() {
        var list = new TrackingList<int>([1, 2]) { NewItemFactory = static () => 3 };
        list.AddNew();
        list.Move(2, 0);

        Assert.Equal(new[] { 3, 1, 2 }, list);
        Assert.Equal(new[] { 3 }, list.Added);

        list.CancelNew(0);
        Assert.Equal(new[] { 1, 2 }, list);
        Assert.Empty(list.Added);
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Moving_pending_restore_then_cancel_restores_the_removed_delta() {
        var original = new EqualRow(1, "original");
        var other = new EqualRow(2, "other");
        var list = new TrackingList<EqualRow>([original, other]) { NewItemFactory = () => new EqualRow(1, "pending") };

        list.RemoveAt(0);
        EqualRow pending = list.AddNew();
        list.Move(1, 0);

        Assert.Empty(list.Removed);
        Assert.Same(pending, list[0]);

        list.CancelNew(0);
        Assert.Single(list);
        Assert.Same(other, list[0]);
        Assert.Single(list.Removed);
        Assert.Same(original, list.Removed[0]);
    }

    [Fact]
    public void Item_owned_origin_remains_authoritative_even_when_materialized_as_initial_list_content() {
        TrackingList<IEmployeeEdit> source = Array.Empty<Employee>().ToTrackingList<Employee, IEmployeeEdit>();
        IEmployeeEdit item = source.AddNew();

        var other = new TrackingList<IEmployeeEdit>(new[] { item });
        Assert.Single(other.Added);
        Assert.True(other.IsAddedAt(0));
    }

    [Fact]
    public void CommitStructuralChanges_makes_active_rows_the_new_baseline() {
        var row = new EqualRow(1, "new");
        var list = new TrackingList<EqualRow>();
        list.Add(row);
        list.CommitStructuralChanges();
        list.RemoveAt(0);

        Assert.Empty(list.Added);
        Assert.Single(list.Removed);
        Assert.Same(row, list.Removed[0]);
    }

    [Fact]
    public void CancelNew_exactly_reverses_a_removed_delta_that_AddNew_cancelled() {
        var original = new EqualRow(1, "old");
        var list = new TrackingList<EqualRow>([original]) { NewItemFactory = () => new EqualRow(1, "pending") };
        list.RemoveAt(0);
        EqualRow pending = list.AddNew();

        Assert.Empty(list.Removed);
        Assert.Empty(list.Added);
        Assert.Same(pending, list[0]);

        list.CancelNew(0);
        Assert.Empty(list);
        Assert.Single(list.Removed);
        Assert.Same(original, list.Removed[0]);
    }

    [Fact]
    public void Replacing_a_pending_new_row_replaces_its_structural_delta() {
        var list = new TrackingList<EqualRow> { NewItemFactory = () => new EqualRow(1, "first") };
        list.AddNew();
        var replacement = new EqualRow(2, "second");
        list[0] = replacement;

        Assert.Single(list.Added);
        Assert.Same(replacement, list.Added[0]);

        list.CancelNew(0);
        Assert.Empty(list);
        Assert.Empty(list.Added);
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void CommitChanges_commits_items_then_clears_structural_history() {
        TrackingList<IEmployeeEdit> list = new[] { new Employee { Number = 1, Name = "A" } }
            .ToTrackingList<Employee, IEmployeeEdit>();
        list[0].Name = "A2";
        IEmployeeEdit added = list.AddNew();
        added.Name = "B";

        Assert.True(list[0].IsEditing);
        Assert.False(added.HasOriginal);
        int before = 0;
        foreach (IEmployeeEdit item in list)
            if (!item.HasOriginal || item.IsEditing) before++;
        Assert.Equal(2, before);

        Assert.True(list.CommitChanges());

        Assert.False(list[0].IsEditing);
        Assert.False(added.IsEditing);
        Assert.True(added.HasOriginal);
        Assert.Empty(list.Added);
        Assert.Empty(list.Removed);
        int after = 0;
        foreach (IEmployeeEdit item in list)
            if (!item.HasOriginal || item.IsEditing) after++;
        Assert.Equal(0, after);
    }

    [Fact]
    public void UseWith_can_read_hidden_runtime_members_from_the_generated_runtime_type() {
        IOverlayEmployeeEdit edit = new Employee { Number = 7, Name = "A", Department = "Old" }
            .ToTrackingItem<Employee, IOverlayEmployeeEdit>();
        Assert.True(edit.Set(nameof(Employee.Department), "Platform"));

        var command = new QueryCommand("UPDATE Employee SET Department = @Department WHERE Number = @Number");
        var builder = new QueryBuilder(command);
        builder.UseWith((object)edit); // The object overload intentionally dispatches to the generated runtime type.

        Assert.Equal("Platform", builder["@Department"]);
        Assert.Equal(7, builder["@Number"]);
    }

    [Fact]
    public void UseWith_projection_can_rename_include_and_exclude_generated_members() {
        IParameterEmployeeEdit edit = new Employee { Name = "A", Number = 4 }.ToTrackingItem<Employee, IParameterEmployeeEdit>();
        edit.Name = "B";
        edit.Selected = true;

        var command = new QueryCommand("SELECT @EmployeeName, @Selected");
        var builder = new QueryBuilder(command);
        builder.UseWith(edit);

        Assert.Equal("B", builder["@EmployeeName"]);
        Assert.Equal(true, builder["@Selected"]);
    }

    [Fact]
    public void Type_wide_parameter_default_can_be_overridden_per_member() {
        ISelectiveParameterEmployeeEdit edit = new Employee { Name = "A", Number = 4 }
            .ToTrackingItem<Employee, ISelectiveParameterEmployeeEdit>();
        edit.Name = "B";
        edit.Number = 9;

        var command = new QueryCommand("SELECT @Name, @Number");
        var builder = new QueryBuilder(command);
        builder.UseWith(edit);

        Assert.Equal("B", builder["@Name"]);
        Assert.Null(builder["@Number"]);

        // Parameter projection only provides defaults. Normal builder customization remains authoritative afterwards.
        builder.Use("@Number", 12);
        Assert.Equal(12, builder["@Number"]);
    }

    [Fact]
    public void RuntimeValue_is_excluded_from_parameters_by_default() {
        IEmployeeEdit edit = new Employee { Name = "A" }.ToTrackingItem<Employee, IEmployeeEdit>();
        edit.Selected = true;

        var command = new QueryCommand("SELECT @Name, @Selected");
        var builder = new QueryBuilder(command);
        builder.UseWith(edit);

        Assert.Equal("A", builder["@Name"]);
        Assert.Null(builder["@Selected"]);
    }

    [Fact]
    public void Binding_subscriptions_are_created_only_when_ListChanged_is_observed() {
        var row = new ObservableRow();
        var list = new TrackingList<ObservableRow>([row]);
        Assert.Equal(0, row.AddCount);

        int changes = 0;
        ListChangedEventHandler handler = (_, e) => { if (e.ListChangedType == ListChangedType.ItemChanged) changes++; };
        list.ListChanged += handler;
        Assert.Equal(1, row.AddCount);

        row.Raise("Value");
        Assert.Equal(1, changes);

        list.ListChanged -= handler;
        Assert.Equal(1, row.RemoveCount);
    }

    [Fact]
    public void Handwritten_materializer_can_be_a_struct() {
        EmployeeProjection edit = new Employee { Name = "A" }.ToTrackingItem<Employee, EmployeeProjection>();
        Assert.Equal("A", edit.Name);
    }

    [Fact]
    public void Concrete_IFromOriginal_wins_over_runtime_generation() {
        HandwrittenRuntimeEdit edit = new Employee().ToTrackingItem<Employee, HandwrittenRuntimeEdit>();
        Assert.False(edit.WasGenerated);
        Assert.Equal(typeof(HandwrittenRuntimeEdit), edit.GetType());
    }

    [Fact]
    public void Strong_runtime_contract_only_gets_capabilities_it_requested() {
        IEmployeeEdit edit = new Employee { Name = "A" }.ToTrackingItem<Employee, IEmployeeEdit>();
        Assert.False(edit is IRuntimeMemberAccess);
        Assert.False(edit is INotifyPropertyChanged);
    }

    [Fact]
    public void RuntimeValue_is_not_transactional_edit_state() {
        var original = new Employee { Name = "A" };
        IEmployeeEdit edit = original.ToTrackingItem<Employee, IEmployeeEdit>();

        edit.Selected = true;
        Assert.False(edit.IsEditing);

        edit.Name = "B";
        Assert.True(edit.IsEditing);
        Assert.Equal("A", original.Name);

        edit.CancelEdit();
        Assert.Equal("A", edit.Name);
        Assert.True(edit.Selected);
    }

    [Fact]
    public void Commit_applies_only_transactional_domain_state() {
        var original = new Employee { Name = "A" };
        IEmployeeEdit edit = original.ToTrackingItem<Employee, IEmployeeEdit>();
        edit.Selected = true;
        edit.Name = "B";

        Assert.True(edit.CommitEdit());
        Assert.Equal("B", original.Name);
        Assert.True(edit.Selected);
        Assert.False(edit.IsEditing);
    }

    [Fact]
    public void Alias_and_custom_writer_are_explicit_contract_configuration() {
        var original = new Employee { Name = "A" };
        IAliasEmployeeEdit edit = original.ToTrackingItem<Employee, IAliasEmployeeEdit>();
        Assert.Equal("A", edit.DisplayName);
        Assert.Equal("A", edit.Code);

        edit.DisplayName = "B";
        edit.Code = "C";
        edit.CommitEdit();

        Assert.Equal("B", original.Name);
        Assert.Equal("C", original.Code);
    }

    [Fact]
    public void Unmatched_abstract_contract_property_fails_instead_of_becoming_hidden_runtime_state() {
        MissingMemberException ex = Assert.Throws<MissingMemberException>(() =>
            new Employee().ToTrackingItem<Employee, IBadEmployeeEdit>());
        Assert.Contains("Nmae", ex.Message);
    }

    [Fact]
    public void Default_interface_methods_remain_authoritative() {
        IDefaultMethodEmployeeEdit edit = new Employee().ToTrackingItem<Employee, IDefaultMethodEmployeeEdit>();
        Assert.Equal(42, edit.Magic());
    }

    [Fact]
    public void Dynamic_access_is_an_independent_requested_capability() {
        IDynamicEmployeeEdit edit = new Employee { Name = "A" }.ToTrackingItem<Employee, IDynamicEmployeeEdit>();
        Assert.Equal("A", edit.Get<string?>("Name"));
        Assert.Equal(-1, edit.Mapper.GetIndex(nameof(Employee.Number)));
        Assert.True(edit.Set("Name", "B"));
        Assert.True(edit.IsEditing);
    }

    [Fact]
    public void Default_runtime_shape_is_dynamic_and_notifying() {
        var original = new Employee { Name = "A" };
        IRuntimeDynamicTrackingItem<Employee> edit = original.ToTrackingItem();
        string? changed = null;
        edit.PropertyChanged += (_, e) => changed = e.PropertyName;

        Assert.True(edit.Set("Name", "B"));
        Assert.Equal("Name", changed);
        Assert.True(edit.CommitEdit());
        Assert.Equal("B", original.Name);
    }

    [Fact]
    public void Notification_storage_and_IL_exist_only_when_requested() {
        INotifyEmployeeEdit edit = new Employee { Name = "A" }.ToTrackingItem<Employee, INotifyEmployeeEdit>();
        string? changed = null;
        edit.PropertyChanged += (_, e) => changed = e.PropertyName;
        edit.Name = "B";
        Assert.Equal("Name", changed);
    }

    [Fact]
    public void Options_freeze_after_first_generated_shape() {
        var options = new RuntimeTrackingOptions<Employee>();
        options.RuntimeValue<bool>("Selected");
        _ = new Employee().ToTrackingItem(options);

        Assert.True(options.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => options.RuntimeValue<int>("Later"));
        Assert.Throws<InvalidOperationException>(() => options.Member<string?>(nameof(Employee.Name)).ReadOnly());
    }

    [Fact]
    public void One_frozen_options_object_reuses_the_same_emitted_type() {
        var options = new RuntimeTrackingOptions<Employee>();
        object a = new Employee().ToTrackingItem(options);
        object b = new Employee().ToTrackingItem(options);
        Assert.Equal(a.GetType(), b.GetType());
    }

    [Fact]
    public void Contract_metadata_overrides_original_metadata() {
        IEmployeeEdit edit = new Employee().ToTrackingItem<Employee, IEmployeeEdit>();
        PropertyDescriptor property = TypeDescriptor.GetProperties(edit)[nameof(IEmployeeEdit.Name)]!;
        Assert.Equal("Interaction name", property.DisplayName);
    }

    [Fact]
    public void Unrelated_duplicate_contract_properties_are_rejected() {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new Employee().ToTrackingItem<Employee, IAmbiguousAliasEdit>());
        Assert.Contains("ambiguous", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Derived_redeclaration_resolves_duplicate_contract_property_intentionally() {
        var original = new Employee { Name = "A" };
        IResolvedAliasEdit edit = original.ToTrackingItem<Employee, IResolvedAliasEdit>();
        edit.Alias = "B";
        edit.CommitEdit();
        Assert.Equal("B", original.Name);
    }

    [Fact]
    public void Generated_list_accepts_another_implementation_of_the_same_contract() {
        TrackingList<IEmployeeEdit> list = new[] { new Employee { Name = "generated" } }.ToTrackingList<Employee, IEmployeeEdit>();
        var manual = new ManualEmployeeEdit(new Employee { Name = "manual" });
        list.Add(manual);

        Assert.Equal(2, list.Count);
        Assert.Same(manual, list[1]);
    }

    [Fact]
    public void Strong_list_binding_descriptors_work_with_alternate_contract_implementations() {
        TrackingList<IEmployeeEdit> list = new[] { new Employee { Name = "generated" } }.ToTrackingList<Employee, IEmployeeEdit>();
        var manual = new ManualEmployeeEdit(new Employee { Name = "manual" });
        list.Add(manual);

        PropertyDescriptor property = ((ITypedList)list).GetItemProperties(null)[nameof(IEmployeeEdit.Name)]!;
        Assert.Equal("manual", property.GetValue(manual));
        property.SetValue(manual, "changed");
        Assert.Equal("changed", manual.Name);
    }

    [Fact]
    public void Generated_AddNew_uses_materializer_factory_without_collection_type_coupling() {
        TrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToTrackingList<Employee, IEmployeeEdit>();
        IEmployeeEdit added = list.AddNew();

        Assert.True(added.IsEditing);
        Assert.Single(list.Added);
        list.CancelNew(0);
        Assert.Empty(list);
        Assert.Empty(list.Added);
    }

    [Fact]
    public void Original_type_attribute_can_change_the_generated_type_model() {
        var original = new TypeConfiguredEmployee { Number = 9, Name = "A" };
        ITypeConfiguredEmployeeEdit edit = original.ToTrackingItem<TypeConfiguredEmployee, ITypeConfiguredEmployeeEdit>();
        IRuntimeMemberAccess runtime = Assert.IsAssignableFrom<IRuntimeMemberAccess>(edit);

        Assert.Equal(9, runtime.Get<int>(nameof(TypeConfiguredEmployee.Number)));
        Assert.True(runtime.Set(nameof(TypeConfiguredEmployee.Number), 10));
        edit.CommitEdit();
        Assert.Equal(10, original.Number);
    }

    [Fact]
    public void Type_policy_interface_can_expand_runtime_shape_beyond_compile_time_contract() {
        var original = new Employee { Number = 7, Name = "A", Department = "Dev" };
        IOverlayEmployeeEdit edit = original.ToTrackingItem<Employee, IOverlayEmployeeEdit>();

        IRuntimeMemberAccess runtime = edit;
        Assert.IsAssignableFrom<INotifyPropertyChanged>(edit);

        Assert.Equal("Dev", runtime.Get<string?>(nameof(Employee.Department)));
        Assert.Equal(7, runtime.Get<int>(nameof(Employee.Number)));
        Assert.False(runtime.Set(nameof(Employee.Name), "B")); // interface overlaid read-only behavior
        Assert.True(runtime.Set(nameof(Employee.Department), "Platform"));
        Assert.True(edit.IsEditing);

        edit.CommitEdit();
        Assert.Equal("A", original.Name);
        Assert.Equal("Platform", original.Department);
    }

    [Fact]
    public void Raw_runtime_options_are_a_base_shape_and_the_strong_contract_still_overlays_them() {
        var options = new RuntimeTrackingOptions<Employee>(false);
        var original = new Employee { Number = 7, Name = "A", Department = "Dev" };

        IOverlayEmployeeEdit edit = original.ToTrackingItem<Employee, IOverlayEmployeeEdit>(options);
        Assert.True(options.IsFrozen);

        IRuntimeMemberAccess runtime = edit;
        Assert.Equal(7, runtime.Get<int>(nameof(Employee.Number)));
        Assert.Equal("Dev", runtime.Get<string?>(nameof(Employee.Department)));
        Assert.False(runtime.Set(nameof(Employee.Name), "B"));
    }

    [Fact]
    public void Type_policy_can_replace_the_contract_member_convention_for_the_whole_type() {
        var original = new Employee { Name = "A" };
        IConventionEmployeeEdit edit = original.ToTrackingItem<Employee, IConventionEmployeeEdit>();

        edit.Selected = true;
        Assert.True(edit.Selected);
        Assert.False(edit.IsEditing);

        edit.Name = "B";
        Assert.True(edit.IsEditing);
        edit.CommitEdit();
        Assert.Equal("B", original.Name);
    }

    [Fact]
    public void Ordered_type_attributes_can_change_members_created_by_an_earlier_type_policy() {
        var original = new Employee { Number = 7, Name = "A" };
        IFilteredRuntimeEmployeeEdit edit = original.ToTrackingItem<Employee, IFilteredRuntimeEmployeeEdit>();
        IRuntimeMemberAccess runtime = Assert.IsAssignableFrom<IRuntimeMemberAccess>(edit);

        Assert.Equal(-1, runtime.Mapper.GetIndex(nameof(Employee.Number)));
        Assert.True(runtime.Mapper.GetIndex(nameof(Employee.Name)) >= 0);
    }

    [Fact]
    public void Runtime_values_and_metadata_survive_cancel_and_commit() {
        var original = new Employee { Name = "A" };
        IStateEmployeeEdit edit = original.ToTrackingItem<Employee, IStateEmployeeEdit>();
        edit.Selected = true;
        edit.SetMetadata(true);
        edit.Name = "B";
        edit.CancelEdit();

        Assert.True(edit.Selected);
        Assert.True(edit.Metadata);
        Assert.Equal("A", edit.Name);

        edit.Name = "C";
        edit.CommitEdit();
        Assert.True(edit.Selected);
        Assert.True(edit.Metadata);
        Assert.Equal("C", original.Name);
    }
}
