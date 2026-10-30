namespace Maash.Core;

/// <summary>A fully-resolved operator: the operator string following <c>@@</c>.</summary>
public sealed record LoadedOperator(
    string OperatorString, string Alias, string Suffix, bool IsShadowedByNonPrimary);
