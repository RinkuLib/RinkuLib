using System.Data;
using System.Data.Common;

namespace Rinku.Querying.Defaults;
/// <summary>
/// Binds values as unnamed <c>?</c> parameters, preserving the order in which the slots are used.
/// Use this for providers whose SQL parameter markers are positional.
/// </summary>
public sealed class PositionalDbParamInfo : DbParamInfo {
    /// <summary>Creates a settled positional binding strategy.</summary>
    public PositionalDbParamInfo() : base(true) { }

    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        value = Add(cmd, value);
        return true;
    }

    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        Add(cmd, value);
        return true;
    }

    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        Add(cmd, value);
        return true;
    }

    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter parameter)
            return false;
        if (newValue is null) {
            cmd.Parameters.Remove(currentValue);
            currentValue = null;
        }
        else
            parameter.Value = newValue;
        return true;
    }

    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object currentValue)
        => cmd.Parameters.Remove(currentValue);

    private static IDbDataParameter Add(IDbCommand cmd, object value) {
        var parameter = (IDbDataParameter)cmd.CreateParameter();
        parameter.ParameterName = "?";
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
        return parameter;
    }
}

/// <summary>
/// Represents metadata for fixed-precision numeric database parameters (Decimal, Currency).
/// </summary>
public sealed class ScaledDbParamCache(DbType type, byte precision, byte scale) : DbParamInfo(true) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly byte Precision = precision;
    /// <inheritdoc/>
    public readonly byte Scale = scale;

    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        SetPrecisionScale(p, Precision, Scale);
        p.Value = value;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Precision = Precision;
        p.Scale = Scale;
        p.Value = value;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        SetPrecisionScale(p, Precision, Scale);
        p.Value = value;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;

        if (newValue is null) {
            cmd.Parameters.Remove(p);
            currentValue = null;
            return true;
        }

        p.Value = newValue;
        return true;
    }

    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object? currentValue)
        => cmd.Parameters.Remove(currentValue);

    internal static void SetPrecisionScale(IDbDataParameter p, byte precision, byte scale) {
        if (p is DbParameter dp) {
            dp.Precision = precision;
            dp.Scale = scale;
        }
        else {
            p.Precision = precision;
            p.Scale = scale;
        }
    }
}

/// <summary>
/// Represents metadata for directional fixed-precision numeric database parameters (Decimal, Currency).
/// </summary>
public sealed class DirectionalScaledDbParamCache(ParameterDirection direction, DbType type, byte precision, byte scale) : DbParamInfo(true, direction == ParameterDirection.Output) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly byte Precision = precision;
    /// <inheritdoc/>
    public readonly byte Scale = scale;
    /// <inheritdoc/>
    public readonly ParameterDirection Direction = direction;
    /// <inheritdoc/>
    public override object SetDefault(string paramName, IDbCommand cmd) {
        var p = cmd.CreateParameter(); p.ParameterName = paramName; p.DbType = Type;
        ScaledDbParamCache.SetPrecisionScale(p, Precision, Scale); p.Direction = Direction; cmd.Parameters.Add(p); return p;
    }

    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        ScaledDbParamCache.SetPrecisionScale(p, Precision, Scale);
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Precision = Precision;
        p.Scale = Scale;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        ScaledDbParamCache.SetPrecisionScale(p, Precision, Scale);
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;

        if (newValue is null) {
            cmd.Parameters.Remove(p);
            currentValue = null;
            return true;
        }

        p.Value = newValue;
        return true;
    }

    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object? currentValue)
        => cmd.Parameters.Remove(currentValue);
}
/// <summary>
/// Represents metadata for directional fixed-precision sized database parameters (e.g., Strings, Binary).
/// </summary>
public sealed class DirectionalSizedDbParamCache(ParameterDirection direction, DbType type, int size = -1) : DbParamInfo(true, direction == ParameterDirection.Output) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly int Size = size;
    /// <inheritdoc/>
    public readonly ParameterDirection Direction = direction;
    /// <inheritdoc/>
    public override object SetDefault(string paramName, IDbCommand cmd) {
        var p = cmd.CreateParameter(); p.ParameterName = paramName; p.DbType = Type; p.Size = Size; p.Direction = Direction; cmd.Parameters.Add(p); return p;
    }

    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Size = Size;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Size = Size;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Size = Size;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;

        if (newValue is null) {
            cmd.Parameters.Remove(p);
            currentValue = null;
            return true;
        }

        p.Value = newValue;
        return true;
    }

    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object? currentValue)
        => cmd.Parameters.Remove(currentValue);
}
/// <summary>
/// Represents metadata for directional fixed-type database parameters (e.g., Integers, Booleans) 
/// </summary>
public sealed class DirectionalDbParamCache(ParameterDirection direction, DbType type) : DbParamInfo(true, direction == ParameterDirection.Output) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly ParameterDirection Direction = direction;
    /// <inheritdoc/>
    public override object SetDefault(string paramName, IDbCommand cmd) {
        var p = cmd.CreateParameter(); p.ParameterName = paramName; p.DbType = Type; p.Direction = Direction; cmd.Parameters.Add(p); return p;
    }

    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        p.Direction = Direction;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;

        if (newValue is null) {
            cmd.Parameters.Remove(p);
            currentValue = null;
            return true;
        }

        p.Value = newValue;
        return true;
    }

    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object? currentValue)
        => cmd.Parameters.Remove(currentValue);
}
