# Rinku.More

`Rinku.More` is the optional companion package for [Rinku](https://www.nuget.org/packages/Rinku).
It contains globalization/localization helpers, bindable cached lists, executor plans, and
incremental `BindingTree` projections without adding those features to the core Rinku package.

Install it with:

```text
dotnet add package Rinku.More
```

The package depends on `Rinku` and registers its `Word` mapping support automatically. See the
[Rinku.More documentation](https://rinkulib.github.io/RinkuLib/articles/more/) for globalization,
localization catalogs, cached lists, binding trees, and executor plans.

## Generated methods and retained values

`DefineMethod<TContract>` records another interface method on the same generated type. Each named method has its own input signature and instructions. `Field<T>` identifies shared instance storage by name; conflicting types fail compilation. `Callback<TDelegate>` embeds a scope as a stable generated delegate. Plans and generated factories are reusable; executor instances retain their own runtime captures. Cloning and merging preserve the additional method plans.
