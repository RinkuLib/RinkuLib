namespace Rinku;

/// <summary>An optional invocation capability with its current availability. It names no input gesture or display technology.</summary>
public interface IInvokable
{
    bool Enabled
    {
        get;
    }
    void Invoke();
}
