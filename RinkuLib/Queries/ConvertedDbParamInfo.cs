using System.Data;
using System.Data.Common;

namespace RinkuLib.Queries;

/// <summary>
/// Implements the reusable parameter lifecycle for a CLR value that must be converted before it is sent to
/// the database.
/// </summary>
/// <typeparam name="T">The CLR type accepted by the parameter.</typeparam>
/// <remarks>
/// Override <see cref="ConvertValue(T)"/> for the conversion. Override <see cref="ConfigureParameter"/> when
/// the created parameter needs a type, size, precision, scale, or provider-specific setting. Inherit directly
/// from <see cref="DbParamInfo"/> when the parameter needs a different lifecycle.
/// </remarks>
public abstract class ConvertedDbParamInfo<T> : DbParamInfo {
    /// <summary>Creates a cached conversion strategy.</summary>
    protected ConvertedDbParamInfo() : base(true) { }

    /// <summary>Converts one CLR value to the value stored in the database parameter.</summary>
    protected abstract object? ConvertValue(T value);

    /// <summary>Configures a newly created parameter after its name and value are set.</summary>
    protected virtual void ConfigureParameter(IDbDataParameter parameter) { }

    /// <inheritdoc/>
    public sealed override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        if (value is not T typed)
            return false;
        value = Add(paramName, cmd, typed);
        return true;
    }

    /// <inheritdoc/>
    public sealed override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter parameter)
            return false;
        if (newValue is null) {
            cmd.Parameters.Remove(currentValue);
            currentValue = null;
            return true;
        }
        if (newValue is not T typed)
            return false;
        parameter.Value = ConvertValue(typed) ?? DBNull.Value;
        return true;
    }

    /// <inheritdoc/>
    public sealed override bool Use(string paramName, IDbCommand cmd, object value) {
        if (value is not T typed)
            return false;
        Add(paramName, cmd, typed);
        return true;
    }

    /// <inheritdoc/>
    public sealed override bool Use(string paramName, DbCommand cmd, object value) {
        if (value is not T typed)
            return false;
        Add(paramName, cmd, typed);
        return true;
    }

    /// <inheritdoc/>
    public sealed override void Remove(IDbCommand cmd, object currentValue)
        => DbParamInfo.RemoveSingle(((IDbDataParameter)currentValue).ParameterName, cmd);

    private IDbDataParameter Add(string paramName, IDbCommand cmd, T value) {
        var parameter = (IDbDataParameter)cmd.CreateParameter();
        parameter.ParameterName = paramName;
        parameter.Value = ConvertValue(value) ?? DBNull.Value;
        ConfigureParameter(parameter);
        cmd.Parameters.Add(parameter);
        return parameter;
    }
}
