using JMobile.Core.Model;

namespace JMobile.Core.Execution;

/// <summary>
/// One entry of a method's exception table, with bytecode offsets already translated into
/// instruction indices so that dispatching a throw needs no offset arithmetic.
/// </summary>
public sealed class ExceptionHandler
{
    private JavaClass? _catchType;
    private bool _catchTypeResolved;

    private ExceptionHandler(int startIndex, int endIndex, int handlerIndex, string? catchTypeName)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
        HandlerIndex = handlerIndex;
        CatchTypeName = catchTypeName;
    }

    /// <summary>Inclusive instruction index at which the guarded region starts.</summary>
    public int StartIndex { get; }

    /// <summary>Exclusive instruction index at which the guarded region ends.</summary>
    public int EndIndex { get; }

    /// <summary>Instruction index of the handler body.</summary>
    public int HandlerIndex { get; }

    /// <summary>Internal name of the caught class, or null for a catch-all (a <c>finally</c> block).</summary>
    public string? CatchTypeName { get; }

    public bool IsCatchAll => CatchTypeName is null;

    /// <summary>
    /// Decides whether this handler claims a throwable of the given class.
    /// </summary>
    /// <remarks>
    /// The caught class is resolved lazily. A method may guard a region against an exception
    /// type that is never actually thrown there, and loading that class eagerly would drag in
    /// classes a game never touches, so resolution waits until a throw genuinely reaches here.
    /// </remarks>
    public bool Matches(VirtualMachine vm, JavaClass thrownClass)
    {
        if (IsCatchAll) return true;

        if (!_catchTypeResolved)
        {
            // A handler naming a class we cannot load simply never matches, which is strictly
            // better than failing the throw and losing the original exception.
            _catchType = vm.TryLoadClass(CatchTypeName!);
            _catchTypeResolved = true;
        }

        return _catchType is not null && _catchType.IsAssignableFrom(thrownClass);
    }

    /// <summary>Translates a method's raw exception table into instruction space.</summary>
    internal static ExceptionHandler[] Build(JavaMethod method, VirtualMachine vm)
    {
        if (method.RawExceptionTable.Length == 0) return [];

        // Compilation populates the offset map the translation below depends on.
        method.EnsureCompiled(vm);
        int[] map = method.BytecodeToInstruction;

        // The last map entry is the instruction count, which is the correct end for a guarded
        // region that runs to the end of the method.
        int instructionCount = map.Length == 0 ? 0 : map[^1];

        var handlers = new ExceptionHandler[method.RawExceptionTable.Length];
        for (int i = 0; i < handlers.Length; i++)
        {
            ClassFile.ExceptionTableEntry entry = method.RawExceptionTable[i];

            string? catchTypeName = entry.CatchTypeIndex == 0
                ? null
                : method.DeclaringClass.Pool!.ClassName(entry.CatchTypeIndex);

            handlers[i] = new ExceptionHandler(
                startIndex: MapOffset(map, entry.StartPc, fallback: 0),
                endIndex: MapOffset(map, entry.EndPc, fallback: instructionCount),
                handlerIndex: MapOffset(map, entry.HandlerPc, fallback: 0),
                catchTypeName: catchTypeName);
        }

        return handlers;
    }

    private static int MapOffset(int[] map, int bytecodeOffset, int fallback)
    {
        if (bytecodeOffset < 0) return fallback;
        if (bytecodeOffset >= map.Length) return fallback;

        int index = map[bytecodeOffset];
        return index >= 0 ? index : fallback;
    }
}
