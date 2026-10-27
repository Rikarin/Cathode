namespace Cathode.Server.Rig;

/// <summary>
///     A request the rig cannot carry out, mapped to an RFC 9457 problem response with <see cref="Status" />.
/// </summary>
public sealed class RigException : Exception {
    public int Status { get; }

    public IReadOnlyList<string> Errors { get; }

    public RigException() : this(500, "Rig error.") { }

    public RigException(string message) : this(400, message) { }

    public RigException(string message, Exception innerException) : base(message, innerException) {
        Status = 500;
        Errors = [];
    }

    public RigException(int status, string message, IReadOnlyList<string>? errors = null) : base(message) {
        Status = status;
        Errors = errors ?? [];
    }

    public static RigException NotFound(string message) => new(404, message);

    public static RigException Conflict(string message) => new(409, message);
}
