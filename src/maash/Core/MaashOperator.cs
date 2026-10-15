namespace Maash.Core;

public class MaashOperator
{
    public string Suffix { get; }
    public string Description { get; }
    public bool Bracketed { get; }

    public MaashOperator(string suffix, string description = "", bool bracketed = false)
    {
        Suffix = suffix;
        Description = description;
        Bracketed = bracketed;
    }
}
