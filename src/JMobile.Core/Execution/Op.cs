namespace JMobile.Core.Execution;

/// <summary>
/// The normalised instruction set the interpreter dispatches on.
/// </summary>
/// <remarks>
/// This is not the class-file instruction set. <see cref="CodeCompiler"/> rewrites bytecode
/// into these opcodes once, and the rewrite does three things that pay off on every later
/// execution of the method:
/// <list type="bullet">
/// <item>Families collapse. The forty-odd <c>iload_0</c> / <c>fload</c> / <c>aload_3</c>
/// variants become three opcodes with an operand, and every integer push becomes one
/// <see cref="Const32"/>. A smaller switch means a smaller jump table and better branch
/// prediction in the dispatch loop.</item>
/// <item>Operands are pre-decoded. Branch targets are instruction indices, wide prefixes are
/// folded in, and constants are already unpacked, so the loop never re-parses operand bytes.</item>
/// <item>Field access is specialised by width. The declared type of a field is readable from
/// the constant pool without loading anything, so a <c>getfield</c> on a <c>long</c> becomes
/// <see cref="GetField64"/> at compile time. Only the storage slot is resolved lazily, and it
/// lands in the instruction's cache as a single reference write, so the steady state costs one
/// null check and no width dispatch — and no opcode is ever mutated while another thread might
/// be executing it.</item>
/// </list>
/// </remarks>
public enum Op : byte
{
    Nop = 0,

    // -- constants ------------------------------------------------------------------
    AConstNull,

    /// <summary>Push a 32-bit primitive held in operand A (covers iconst, bipush, sipush, ldc).</summary>
    Const32,

    /// <summary>Push a 64-bit primitive whose halves are in operands A (low) and B (high).</summary>
    Const64,

    /// <summary>Push a reference constant held in the instruction cache (string or class mirror).</summary>
    ConstRef,

    // -- local variables ------------------------------------------------------------
    Load32,
    Load64,
    LoadRef,
    Store32,
    Store64,
    StoreRef,
    IInc,

    // -- array access ---------------------------------------------------------------
    ArrayLoadInt,
    ArrayLoadLong,
    ArrayLoadFloat,
    ArrayLoadDouble,
    ArrayLoadRef,
    ArrayLoadByte,
    ArrayLoadChar,
    ArrayLoadShort,
    ArrayStoreInt,
    ArrayStoreLong,
    ArrayStoreFloat,
    ArrayStoreDouble,
    ArrayStoreRef,
    ArrayStoreByte,
    ArrayStoreChar,
    ArrayStoreShort,
    ArrayLength,

    // -- operand stack --------------------------------------------------------------
    Pop,
    Pop2,
    Dup,
    DupX1,
    DupX2,
    Dup2,
    Dup2X1,
    Dup2X2,
    Swap,

    // -- integer arithmetic ---------------------------------------------------------
    IAdd,
    ISub,
    IMul,
    IDiv,
    IRem,
    INeg,
    IShl,
    IShr,
    IUShr,
    IAnd,
    IOr,
    IXor,

    // -- long arithmetic ------------------------------------------------------------
    LAdd,
    LSub,
    LMul,
    LDiv,
    LRem,
    LNeg,
    LShl,
    LShr,
    LUShr,
    LAnd,
    LOr,
    LXor,

    // -- floating point -------------------------------------------------------------
    FAdd,
    FSub,
    FMul,
    FDiv,
    FRem,
    FNeg,
    DAdd,
    DSub,
    DMul,
    DDiv,
    DRem,
    DNeg,

    // -- conversions ----------------------------------------------------------------
    I2L,
    I2F,
    I2D,
    L2I,
    L2F,
    L2D,
    F2I,
    F2L,
    F2D,
    D2I,
    D2L,
    D2F,
    I2B,
    I2C,
    I2S,

    // -- comparisons ----------------------------------------------------------------
    LCmp,
    FCmpL,
    FCmpG,
    DCmpL,
    DCmpG,

    // -- control flow ---------------------------------------------------------------
    IfEq,
    IfNe,
    IfLt,
    IfGe,
    IfGt,
    IfLe,
    IfICmpEq,
    IfICmpNe,
    IfICmpLt,
    IfICmpGe,
    IfICmpGt,
    IfICmpLe,
    IfACmpEq,
    IfACmpNe,
    IfNull,
    IfNonNull,
    Goto,

    /// <summary>Jump to subroutine. Still emitted by the compilers that built most J2ME titles.</summary>
    Jsr,

    /// <summary>Return from subroutine, taking its target from a local variable.</summary>
    Ret,

    TableSwitch,
    LookupSwitch,

    // -- returns --------------------------------------------------------------------
    Return,
    Return32,
    Return64,
    ReturnRef,

    // -- field access, already specialised by width at compile time -----------------
    GetStatic32,
    GetStatic64,
    GetStaticRef,
    PutStatic32,
    PutStatic64,
    PutStaticRef,
    PutStaticBoolean,
    PutStaticByte,
    PutStaticChar,
    PutStaticShort,
    GetField32,
    GetField64,
    GetFieldRef,
    PutField32,
    PutField64,
    PutFieldRef,
    PutFieldBoolean,
    PutFieldByte,
    PutFieldChar,
    PutFieldShort,

    // -- invocation -----------------------------------------------------------------
    InvokeVirtual,
    InvokeSpecial,
    InvokeStatic,
    InvokeInterface,

    // -- allocation and type tests --------------------------------------------------
    New,
    NewArray,
    ANewArray,
    MultiANewArray,
    CheckCast,
    InstanceOf,

    // -- exceptions and monitors ----------------------------------------------------
    AThrow,
    MonitorEnter,
    MonitorExit,
}
