namespace Cathode.Engine.Graph;

/// <summary>Thrown when a <see cref="GraphDefinition"/> cannot be compiled. <see cref="Errors"/> lists every problem found.</summary>
public sealed class GraphValidationException : Exception
{
    public GraphValidationException()
        : this([])
    {
    }

    public GraphValidationException(string message)
        : this([message])
    {
    }

    public GraphValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public GraphValidationException(IReadOnlyList<string> errors)
        : base(errors.Count == 0 ? "Invalid graph." : "Invalid graph: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
