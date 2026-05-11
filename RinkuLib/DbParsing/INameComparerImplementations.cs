namespace RinkuLib.DbParsing;
/// <summary>Matches when no name is provided (identity match).</summary>
public record NoNameComparer() : IMutatableNameComparer {
    /// <summary>The singleton instance of the <see cref="NoNameComparer"/>.</summary>
    public static readonly NoNameComparer Instance = new();

    /// <inheritdoc/>
    public string GetDefaultName() => "";
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers) => nameComparers.Length == 0 || nameComparers.MatchNext(colName);
    /// <inheritdoc/>
    public bool Contains(string name) => false;

    /// <inheritdoc/>
    public INameComparer? TryAdd(string name) => new NameComparer(name);
    /// <inheritdoc/>
    public INameComparer? TryAdd(INameComparer other) => other;
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) => null;
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other) => Instance;
}

/// <summary>Matches if the current segment ends with <see cref="Name"/> (Span 1).</summary>
public record NameComparer(string Name) : IMutatableNameComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers)
        => colName.EndsWith(Name, StringComparison.OrdinalIgnoreCase) && nameComparers.MatchNext(colName[..^Name.Length]);
    /// <inheritdoc/>
    public bool Contains(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public INameComparer? TryAdd(string name) => Contains(name) ? this : new NameTwo(Name, name);
    /// <inheritdoc/>
    public INameComparer? TryAdd(INameComparer other) => new JoinedNameComparer(this, other);
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) => Contains(name) ? NoNameComparer.Instance : null;
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other)
        => ReferenceEquals(other, this) ? NoNameComparer.Instance : null;
}

/// <summary>Matches if the current segment ends with <see cref="Name"/> or <see cref="AltName"/> (Span 1).</summary>
public record NameTwo(string Name, string AltName) : IMutatableNameComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers)
        => (colName.EndsWith(AltName, StringComparison.OrdinalIgnoreCase) && nameComparers.MatchNext(colName[..^AltName.Length]))
        || (colName.EndsWith(Name, StringComparison.OrdinalIgnoreCase) && nameComparers.MatchNext(colName[..^Name.Length]));
    /// <inheritdoc/>
    public bool Contains(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(AltName, name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public INameComparer? TryAdd(string name) => Contains(name) ? this : new NameArray([Name, AltName, name]);
    /// <inheritdoc/>
    public INameComparer? TryAdd(INameComparer other) => new JoinedNameComparer(this, other);
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) {
        if (string.Equals(Name, name, StringComparison.OrdinalIgnoreCase))
            return new NameComparer(AltName);
        if (string.Equals(AltName, name, StringComparison.OrdinalIgnoreCase))
            return new NameComparer(Name);
        return null;
    }
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other)
        => ReferenceEquals(other, this) ? NoNameComparer.Instance : null;
}

/// <summary>Matches if the current segment ends with any value in <see cref="Names"/> (Span 1).</summary>
public record NameArray(string[] Names) : IMutatableNameComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Names[0];
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers) {
        for (int i = Names.Length - 1; i >= 0; i--)
            if (colName.EndsWith(Names[i], StringComparison.OrdinalIgnoreCase) && nameComparers.MatchNext(colName[..^Names[i].Length]))
                return true;
        return false;
    }
    /// <inheritdoc/>
    public bool Contains(string name) => Names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public INameComparer? TryAdd(string name) => Contains(name) ? this : this with { Names = [.. Names, name] };
    /// <inheritdoc/>
    public INameComparer? TryAdd(INameComparer other) => null;
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) {
        int index = Array.FindIndex(Names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (index == -1)
            return null;

        if (Names.Length == 1)
            return NoNameComparer.Instance;

        if (Names.Length == 2)
            return new NameComparer(index == 0 ? Names[1] : Names[0]);

        if (Names.Length == 3) 
            return index switch {
                0 => new NameTwo(Names[1], Names[2]),
                1 => new NameTwo(Names[0], Names[2]),
                _ => new NameTwo(Names[0], Names[1])
            };

        var newNames = new string[Names.Length - 1];
        if (index > 0)
            Array.Copy(Names, 0, newNames, 0, index);
        if (index < Names.Length - 1)
            Array.Copy(Names, index + 1, newNames, index, Names.Length - index - 1);

        return this with { Names = newNames };
    }
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other)
        => ReferenceEquals(other, this) ? NoNameComparer.Instance : null;
}

/// <summary>Matches if the current segment ends with <see cref="Name"/> and bypasses <see cref="Span"/> preceding comparers.</summary>
public record NameMultiSpan(string Name, int Span) : INameComparer, INameComparerThatCanRemove, INameComparerThatCanRemoveAComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers)
        => colName.EndsWith(Name, StringComparison.OrdinalIgnoreCase) && nameComparers.MatchNext(colName[..^Name.Length], Span);
    /// <inheritdoc/>
    public bool Contains(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) => Contains(name) ? NoNameComparer.Instance : null;
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other)
        => ReferenceEquals(other, this) ? NoNameComparer.Instance : null;
}
/// <summary>Matches if the current segment ends with <see cref="Name"/> and bypasses preceding comparers up to a comparer with <see cref="UpToKey"/>.</summary>
public record NameMultiSpanKey(string Name, string UpToKey) : INameComparer, INameComparerThatCanRemove, INameComparerThatCanRemoveAComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers) {
        if (!colName.EndsWith(Name, StringComparison.OrdinalIgnoreCase))
            return false;
        colName = colName[..^Name.Length];
        for (int i = nameComparers.Length - 1; i >= 0; i--) {
            if (!nameComparers[i].Contains(UpToKey))
                continue;
            if (i == 0)
                return colName.Length == 0;
            return nameComparers[i - 1].Match(colName, nameComparers[..(i - 1)]);
        }
        return false;
    }
    /// <inheritdoc/>
    public bool Contains(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) => Contains(name) ? NoNameComparer.Instance : null;
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other)
        => other is NameMultiSpanKey m && Contains(m.Name) ? NoNameComparer.Instance : null;
}
/// <summary>Joins two comparers, prioritizing the <see cref="AltComparer"/> branch.</summary>
public record JoinedNameComparer(INameComparer Comparer, INameComparer AltComparer) : IMutatableNameComparer, INameComparerThatCanRemoveAComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Comparer.GetDefaultName();
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers) => Comparer.Match(colName, nameComparers) || AltComparer.Match(colName, nameComparers);
    /// <inheritdoc/>
    public bool Contains(string name) => Comparer.Contains(name) || AltComparer.Contains(name);

    /// <inheritdoc/>
    public INameComparer? TryAdd(string name) 
        => (AltComparer as INameComparerThatCanAdd)?.TryAdd(name)
            ?? (Comparer as INameComparerThatCanAdd)?.TryAdd(name)
            ?? new NameComparerGroup([Comparer, AltComparer, new NameComparer(name)]);
    /// <inheritdoc/>
    public INameComparer? TryAdd(INameComparer other) => new NameComparerGroup([Comparer, AltComparer, other]);
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) {
        var cmp = (Comparer as INameComparerThatCanRemove)?.TryRemove(name);
        var altCmp = (AltComparer as INameComparerThatCanRemove)?.TryRemove(name);
        if (cmp is null && altCmp is null)
            return null;
        cmp ??= Comparer;
        altCmp ??= AltComparer;
        return cmp is NoNameComparer 
            ? altCmp 
            : altCmp is NoNameComparer 
                ? cmp 
                : new JoinedNameComparer(cmp, altCmp);
    }
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other) {
        var cmp = (Comparer as INameComparerThatCanRemoveAComparer)?.TryRemove(other);
        var altCmp = (AltComparer as INameComparerThatCanRemoveAComparer)?.TryRemove(other);
        if (cmp is null && altCmp is null)
            return null;
        cmp ??= Comparer;
        altCmp ??= AltComparer;
        return cmp is NoNameComparer
            ? altCmp
            : altCmp is NoNameComparer
                ? cmp
                : new JoinedNameComparer(cmp, altCmp);
    }
}

/// <summary>Matches by evaluating children from right to left.</summary>
public record NameComparerGroup(INameComparer[] Children) : IMutatableNameComparer, INameComparerThatCanRemoveAComparer {
    /// <inheritdoc/>
    public string GetDefaultName() => Children[0].GetDefaultName();
    /// <inheritdoc/>
    public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers) {
        for (int i = Children.Length - 1; i >= 0; i--)
            if (Children[i].Match(colName, nameComparers))
                return true;
        return false;
    }
    /// <inheritdoc/>
    public bool Contains(string name) => Children.Any(c => c.Contains(name));

    /// <inheritdoc/>
    public INameComparer? TryAdd(string name) {
        for (int i = 0; i < Children.Length; i++) {
            if (Children[i] is not INameComparerThatCanAdd c)
                continue;
            var nc = c.TryAdd(name);
            if (nc is null)
                continue;
            Children[i] = nc;
            return this;
        }
        return new NameComparerGroup([.. Children, new NameComparer(name)]);
    }
    /// <inheritdoc/>
    public INameComparer? TryAdd(INameComparer other) => other is NameComparerGroup g 
        ? new NameComparerGroup([.. Children, .. g.Children])
        : other is JoinedNameComparer j 
            ? new NameComparerGroup([.. Children, j.Comparer, j.AltComparer])
            : new NameComparerGroup([.. Children, other]);
    /// <inheritdoc/>
    public INameComparer? TryRemove(string name) {
        for (int i = 0; i < Children.Length; i++) {
            if (Children[i] is not INameComparerThatCanAdd c)
                continue;
            var nc = c.TryAdd(name);
            if (nc is null)
                continue;
            if (nc is NoNameComparer) {
                var remainingChildren = new INameComparer[Children.Length - 1];
                Array.Copy(Children, 0, remainingChildren, 0, i);
                Array.Copy(Children, i + 1, remainingChildren, i, Children.Length - i - 1);
                return new NameComparerGroup(remainingChildren);
            }
            Children[i] = nc;
            return this;
        }
        return null;
    }
    /// <inheritdoc/>
    public INameComparer? TryRemove(INameComparer other) {
        for (int i = 0; i < Children.Length; i++) {
            if (Children[i] is not INameComparerThatCanRemoveAComparer c)
                continue;
            var nc = c.TryRemove(other);
            if (nc is null)
                continue;
            if (nc is NoNameComparer) {
                var remainingChildren = new INameComparer[Children.Length - 1];
                Array.Copy(Children, 0, remainingChildren, 0, i);
                Array.Copy(Children, i + 1, remainingChildren, i, Children.Length - i - 1);
                return new NameComparerGroup(remainingChildren);
            }
            Children[i] = nc;
            return this;
        }
        return null;
    }
}