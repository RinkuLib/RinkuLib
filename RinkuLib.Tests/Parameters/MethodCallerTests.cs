using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Rinku;
using Rinku.Querying.Parameters;
using Xunit;

namespace Rinku.Querying.Tests;

public sealed class MethodCallerTests {
    private interface IEmployeeArgs {
        int Id { get; }
        string Name { get; }
    }

    private sealed class EmployeeArgs : IEmployeeArgs {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Hidden { get; init; }
    }

    private static string Format(int Id, string Name) => $"{Id}:{Name}";

    [Fact]
    public void Caller_maps_against_the_delegate_source_type_exactly() {
        MethodInfo method = typeof(MethodCallerTests).GetMethod(nameof(Format), BindingFlags.Static | BindingFlags.NonPublic)!;
        var caller = MethodCaller.Create<Func<IEmployeeArgs, string>>(method);
        IEmployeeArgs args = new EmployeeArgs { Id = 7, Name = "Ada", Hidden = 99 };
        Assert.Equal("7:Ada", caller(args));
    }

    private static Task<int> WithCancellation(int Id, CancellationToken cancellationToken)
        => Task.FromResult(cancellationToken.CanBeCanceled ? Id + 1 : Id);

    [Fact]
    public async Task Additional_delegate_arguments_match_target_parameters_by_type() {
        MethodInfo method = typeof(MethodCallerTests).GetMethod(nameof(WithCancellation), BindingFlags.Static | BindingFlags.NonPublic)!;
        var caller = MethodCaller.Create<Func<IEmployeeArgs, CancellationToken, Task<int>>>(method);
        using var cts = new CancellationTokenSource();
        Assert.Equal(8, await caller(new EmployeeArgs { Id = 7, Name = "Ada" }, cts.Token));
    }

    private static Task<int> WithoutCancellation(int Id) => Task.FromResult(Id);

    [Fact]
    public async Task Caller_argument_can_be_unused_when_target_method_does_not_want_it() {
        MethodInfo method = typeof(MethodCallerTests).GetMethod(nameof(WithoutCancellation), BindingFlags.Static | BindingFlags.NonPublic)!;
        var caller = MethodCaller.Create<Func<IEmployeeArgs, CancellationToken, Task<int>>>(method);
        Assert.Equal(7, await caller(new EmployeeArgs { Id = 7 }, CancellationToken.None));
    }

    private static int AmbiguousInts(int UserId, int CompanyId) => UserId * 100 + CompanyId;

    [Fact]
    public void Type_only_caller_binding_throws_when_more_than_one_target_parameter_matches() {
        MethodInfo method = typeof(MethodCallerTests).GetMethod(nameof(AmbiguousInts), BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Throws<InvalidOperationException>(() => MethodCaller.Create<Func<IEmployeeArgs, int, int>>(method));
    }

    private interface ICompanyArgs { int CompanyId { get; } }
    private sealed class CompanyArgs : ICompanyArgs { public int CompanyId { get; init; } }

    [Fact]
    public void Caller_parameter_can_select_its_target_by_name() {
        MethodInfo method = typeof(MethodCallerTests).GetMethod(nameof(AmbiguousInts), BindingFlags.Static | BindingFlags.NonPublic)!;
        var caller = MethodCaller.Create<Func<ICompanyArgs, int, int>>(method, CallerParameter<int>.Named("UserId"));
        Assert.Equal(1205, caller(new CompanyArgs { CompanyId = 5 }, 12));
    }

    private sealed class NestedEmployee { public int Id { get; init; } }
    private sealed class NestedArgs {
        [NestedParameters] public NestedEmployee Employee { get; init; } = null!;
        public string Name { get; init; } = string.Empty;
    }

    [Fact]
    public void Caller_reuses_nested_parameter_mapping() {
        MethodInfo method = typeof(MethodCallerTests).GetMethod(nameof(Format), BindingFlags.Static | BindingFlags.NonPublic)!;
        var caller = MethodCaller.Create<Func<NestedArgs, string>>(method);
        Assert.Equal("4:Grace", caller(new NestedArgs { Employee = new NestedEmployee { Id = 4 }, Name = "Grace" }));
    }

    private sealed class InstanceArgs {
        public int Offset { get; init; }
        public int Add(int Value) => Offset + Value;
        public int Value { get; init; }
    }

    [Fact]
    public void Instance_method_can_use_the_mapped_source_as_its_instance() {
        MethodInfo method = typeof(InstanceArgs).GetMethod(nameof(InstanceArgs.Add))!;
        var caller = MethodCaller.Create<Func<InstanceArgs, int>>(method);
        Assert.Equal(9, caller(new InstanceArgs { Offset = 2, Value = 7 }));
    }
}
