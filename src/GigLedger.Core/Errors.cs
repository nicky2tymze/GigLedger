namespace GigLedger.Core;

/// <summary>A shift or trip id that does not exist. The API answers 404 for this, 409 for other refusals.</summary>
public sealed class NotFoundException(string message) : Exception(message);
