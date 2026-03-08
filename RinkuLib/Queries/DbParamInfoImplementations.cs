using System.Data;
using System.Data.Common;

namespace RinkuLib.Queries;
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
        p.Precision = Precision;
        p.Scale = Scale;
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
        p.Precision = Precision;
        p.Scale = Scale;
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
}

/// <summary>
/// Represents metadata for directional fixed-precision numeric database parameters (Decimal, Currency).
/// </summary>
public sealed class DirectionalScaledDbParamCache(ParameterDirection direction, DbType type, byte precision, byte scale) : DbParamInfo(true) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly byte Precision = precision;
    /// <inheritdoc/>
    public readonly byte Scale = scale;
    /// <inheritdoc/>
    public readonly ParameterDirection Direction = direction;

    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
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
        p.Precision = Precision;
        p.Scale = Scale;
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
public sealed class DirectionalSizedDbParamCache(ParameterDirection direction, DbType type, int size = -1) : DbParamInfo(true) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly int Size = size;
    /// <inheritdoc/>
    public readonly ParameterDirection Direction = direction;

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
public sealed class DirectionalDbParamCache(ParameterDirection direction, DbType type) : DbParamInfo(true) {
    /// <inheritdoc/>
    public readonly DbType Type = type;
    /// <inheritdoc/>
    public readonly ParameterDirection Direction = direction;

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