namespace GigLedger.Core;

/// <summary>Where a stored number came from (SRS FR-7).</summary>
public enum Grade
{
    /// <summary>Shown by the platform: pay, stated miles, estimated time.</summary>
    Stated,
    /// <summary>Typed in by the driver.</summary>
    Entered,
    /// <summary>Read from an instrument or a document: odometer, receipt.</summary>
    Measured,
    /// <summary>Computed by GigLedger.</summary>
    Derived,
}

/// <summary>A value that always travels with its grade.</summary>
public readonly record struct Graded<T>(T Value, Grade Grade);

/// <summary>A default or estimate a result depended on (FR-10, NFR-2).</summary>
public sealed record Assumption(string Input, string Source);

/// <summary>A computed number and every assumption behind it.</summary>
public sealed record Result(decimal Value, IReadOnlyList<Assumption> Assumptions)
{
    public bool RestsOnDefault => Assumptions.Count > 0;
}
