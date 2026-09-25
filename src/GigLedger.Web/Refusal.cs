using GigLedger.Core;

namespace GigLedger.Web;

/// <summary>Turns a service's refusal into a sentence a screen can show.</summary>
public static class Refusal
{
    public static bool IsRefusal(Exception e) =>
        e is NotFoundException or InvalidOperationException or ArgumentException;

    /// <summary>The reason, without .NET's "(Parameter ...)" and "Actual value" tail.</summary>
    public static string Reason(Exception e) =>
        e is ArgumentException ? e.Message.Split(" (Parameter")[0].Split(Environment.NewLine)[0] : e.Message;
}
