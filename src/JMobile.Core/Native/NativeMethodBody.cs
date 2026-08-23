using JMobile.Core.Execution;

namespace JMobile.Core.Native;

/// <summary>
/// The implementation of a runtime-library method written in C# instead of bytecode.
/// </summary>
/// <remarks>
/// The entire CLDC and MIDP class library is built from these. Nothing about the design
/// requires it: the interpreter would happily run a bytecode implementation of
/// <c>java.util.Vector</c>. Implementing the library natively simply makes every library
/// call run at CLR speed, which matters because game inner loops spend most of their time
/// in <c>Graphics</c>, <c>Vector</c>, <c>Math</c> and <c>System.arraycopy</c>.
/// </remarks>
/// <param name="thread">The calling thread, giving access to the VM and to exception throwing.</param>
/// <param name="args">The argument window projected onto the caller's stack.</param>
/// <returns>The return value, or <see cref="JavaValue.Void"/> for a void method.</returns>
public delegate JavaValue NativeMethodBody(JavaThread thread, JavaArgs args);
