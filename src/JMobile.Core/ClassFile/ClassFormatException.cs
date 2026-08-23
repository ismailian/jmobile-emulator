namespace JMobile.Core.ClassFile;

/// <summary>
/// Raised when a class file cannot be parsed or violates a structural constraint.
/// This signals a defect in the input rather than in a running Java program, so it is a
/// host exception and never surfaces to Java code as a throwable.
/// </summary>
public sealed class ClassFormatException : Exception
{
    public ClassFormatException(string message) : base(message) { }

    public ClassFormatException(string message, Exception inner) : base(message, inner) { }
}
