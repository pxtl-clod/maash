namespace Maash.Core;

/// <summary>General maash user-facing exception.</summary>
public class MaashException : Exception {
    public MaashException(string message) : base(message) { }
}
