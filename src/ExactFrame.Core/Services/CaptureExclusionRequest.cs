namespace ExactFrame.Core.Services;

public readonly record struct CaptureExclusionResult(bool Succeeded, int ErrorCode = 0);

/// <summary>Remembers only accepted native requests. A failed request must be retried.</summary>
public sealed class CaptureExclusionRequest
{
    public bool? AppliedValue { get; private set; }
    public CaptureExclusionResult LastResult { get; private set; }

    public CaptureExclusionResult Apply(bool exclude, Func<bool, CaptureExclusionResult> apply)
    {
        if (AppliedValue == exclude) return LastResult;
        LastResult = apply(exclude);
        AppliedValue = LastResult.Succeeded ? exclude : null;
        return LastResult;
    }
}
