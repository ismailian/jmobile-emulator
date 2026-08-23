namespace JMobile.Core.Execution;

/// <summary>
/// The Java bytecode instruction set exactly as it appears in a class file (JVMS chapter 6).
/// These values are consumed only by <see cref="CodeCompiler"/>, which rewrites them into the
/// normalised <see cref="Op"/> set the interpreter actually dispatches on.
/// </summary>
public static class RawOpcode
{
    public const byte Nop = 0x00;
    public const byte AConstNull = 0x01;
    public const byte IConstM1 = 0x02;
    public const byte IConst0 = 0x03;
    public const byte IConst1 = 0x04;
    public const byte IConst2 = 0x05;
    public const byte IConst3 = 0x06;
    public const byte IConst4 = 0x07;
    public const byte IConst5 = 0x08;
    public const byte LConst0 = 0x09;
    public const byte LConst1 = 0x0A;
    public const byte FConst0 = 0x0B;
    public const byte FConst1 = 0x0C;
    public const byte FConst2 = 0x0D;
    public const byte DConst0 = 0x0E;
    public const byte DConst1 = 0x0F;
    public const byte BiPush = 0x10;
    public const byte SiPush = 0x11;
    public const byte Ldc = 0x12;
    public const byte LdcW = 0x13;
    public const byte Ldc2W = 0x14;

    public const byte ILoad = 0x15;
    public const byte LLoad = 0x16;
    public const byte FLoad = 0x17;
    public const byte DLoad = 0x18;
    public const byte ALoad = 0x19;
    public const byte ILoad0 = 0x1A;
    public const byte ILoad1 = 0x1B;
    public const byte ILoad2 = 0x1C;
    public const byte ILoad3 = 0x1D;
    public const byte LLoad0 = 0x1E;
    public const byte LLoad1 = 0x1F;
    public const byte LLoad2 = 0x20;
    public const byte LLoad3 = 0x21;
    public const byte FLoad0 = 0x22;
    public const byte FLoad1 = 0x23;
    public const byte FLoad2 = 0x24;
    public const byte FLoad3 = 0x25;
    public const byte DLoad0 = 0x26;
    public const byte DLoad1 = 0x27;
    public const byte DLoad2 = 0x28;
    public const byte DLoad3 = 0x29;
    public const byte ALoad0 = 0x2A;
    public const byte ALoad1 = 0x2B;
    public const byte ALoad2 = 0x2C;
    public const byte ALoad3 = 0x2D;

    public const byte IALoad = 0x2E;
    public const byte LALoad = 0x2F;
    public const byte FALoad = 0x30;
    public const byte DALoad = 0x31;
    public const byte AALoad = 0x32;
    public const byte BALoad = 0x33;
    public const byte CALoad = 0x34;
    public const byte SALoad = 0x35;

    public const byte IStore = 0x36;
    public const byte LStore = 0x37;
    public const byte FStore = 0x38;
    public const byte DStore = 0x39;
    public const byte AStore = 0x3A;
    public const byte IStore0 = 0x3B;
    public const byte IStore1 = 0x3C;
    public const byte IStore2 = 0x3D;
    public const byte IStore3 = 0x3E;
    public const byte LStore0 = 0x3F;
    public const byte LStore1 = 0x40;
    public const byte LStore2 = 0x41;
    public const byte LStore3 = 0x42;
    public const byte FStore0 = 0x43;
    public const byte FStore1 = 0x44;
    public const byte FStore2 = 0x45;
    public const byte FStore3 = 0x46;
    public const byte DStore0 = 0x47;
    public const byte DStore1 = 0x48;
    public const byte DStore2 = 0x49;
    public const byte DStore3 = 0x4A;
    public const byte AStore0 = 0x4B;
    public const byte AStore1 = 0x4C;
    public const byte AStore2 = 0x4D;
    public const byte AStore3 = 0x4E;

    public const byte IAStore = 0x4F;
    public const byte LAStore = 0x50;
    public const byte FAStore = 0x51;
    public const byte DAStore = 0x52;
    public const byte AAStore = 0x53;
    public const byte BAStore = 0x54;
    public const byte CAStore = 0x55;
    public const byte SAStore = 0x56;

    public const byte Pop = 0x57;
    public const byte Pop2 = 0x58;
    public const byte Dup = 0x59;
    public const byte DupX1 = 0x5A;
    public const byte DupX2 = 0x5B;
    public const byte Dup2 = 0x5C;
    public const byte Dup2X1 = 0x5D;
    public const byte Dup2X2 = 0x5E;
    public const byte Swap = 0x5F;

    public const byte IAdd = 0x60;
    public const byte LAdd = 0x61;
    public const byte FAdd = 0x62;
    public const byte DAdd = 0x63;
    public const byte ISub = 0x64;
    public const byte LSub = 0x65;
    public const byte FSub = 0x66;
    public const byte DSub = 0x67;
    public const byte IMul = 0x68;
    public const byte LMul = 0x69;
    public const byte FMul = 0x6A;
    public const byte DMul = 0x6B;
    public const byte IDiv = 0x6C;
    public const byte LDiv = 0x6D;
    public const byte FDiv = 0x6E;
    public const byte DDiv = 0x6F;
    public const byte IRem = 0x70;
    public const byte LRem = 0x71;
    public const byte FRem = 0x72;
    public const byte DRem = 0x73;
    public const byte INeg = 0x74;
    public const byte LNeg = 0x75;
    public const byte FNeg = 0x76;
    public const byte DNeg = 0x77;
    public const byte IShl = 0x78;
    public const byte LShl = 0x79;
    public const byte IShr = 0x7A;
    public const byte LShr = 0x7B;
    public const byte IUShr = 0x7C;
    public const byte LUShr = 0x7D;
    public const byte IAnd = 0x7E;
    public const byte LAnd = 0x7F;
    public const byte IOr = 0x80;
    public const byte LOr = 0x81;
    public const byte IXor = 0x82;
    public const byte LXor = 0x83;
    public const byte IInc = 0x84;

    public const byte I2L = 0x85;
    public const byte I2F = 0x86;
    public const byte I2D = 0x87;
    public const byte L2I = 0x88;
    public const byte L2F = 0x89;
    public const byte L2D = 0x8A;
    public const byte F2I = 0x8B;
    public const byte F2L = 0x8C;
    public const byte F2D = 0x8D;
    public const byte D2I = 0x8E;
    public const byte D2L = 0x8F;
    public const byte D2F = 0x90;
    public const byte I2B = 0x91;
    public const byte I2C = 0x92;
    public const byte I2S = 0x93;

    public const byte LCmp = 0x94;
    public const byte FCmpL = 0x95;
    public const byte FCmpG = 0x96;
    public const byte DCmpL = 0x97;
    public const byte DCmpG = 0x98;

    public const byte IfEq = 0x99;
    public const byte IfNe = 0x9A;
    public const byte IfLt = 0x9B;
    public const byte IfGe = 0x9C;
    public const byte IfGt = 0x9D;
    public const byte IfLe = 0x9E;
    public const byte IfICmpEq = 0x9F;
    public const byte IfICmpNe = 0xA0;
    public const byte IfICmpLt = 0xA1;
    public const byte IfICmpGe = 0xA2;
    public const byte IfICmpGt = 0xA3;
    public const byte IfICmpLe = 0xA4;
    public const byte IfACmpEq = 0xA5;
    public const byte IfACmpNe = 0xA6;

    public const byte Goto = 0xA7;
    public const byte Jsr = 0xA8;
    public const byte Ret = 0xA9;
    public const byte TableSwitch = 0xAA;
    public const byte LookupSwitch = 0xAB;

    public const byte IReturn = 0xAC;
    public const byte LReturn = 0xAD;
    public const byte FReturn = 0xAE;
    public const byte DReturn = 0xAF;
    public const byte AReturn = 0xB0;
    public const byte Return = 0xB1;

    public const byte GetStatic = 0xB2;
    public const byte PutStatic = 0xB3;
    public const byte GetField = 0xB4;
    public const byte PutField = 0xB5;

    public const byte InvokeVirtual = 0xB6;
    public const byte InvokeSpecial = 0xB7;
    public const byte InvokeStatic = 0xB8;
    public const byte InvokeInterface = 0xB9;
    public const byte InvokeDynamic = 0xBA;

    public const byte New = 0xBB;
    public const byte NewArray = 0xBC;
    public const byte ANewArray = 0xBD;
    public const byte ArrayLength = 0xBE;
    public const byte AThrow = 0xBF;
    public const byte CheckCast = 0xC0;
    public const byte InstanceOf = 0xC1;
    public const byte MonitorEnter = 0xC2;
    public const byte MonitorExit = 0xC3;

    public const byte Wide = 0xC4;
    public const byte MultiANewArray = 0xC5;
    public const byte IfNull = 0xC6;
    public const byte IfNonNull = 0xC7;
    public const byte GotoW = 0xC8;
    public const byte JsrW = 0xC9;

    /// <summary>Type codes used by the <c>newarray</c> instruction (JVMS Table 6.5.newarray-A).</summary>
    public static class ArrayTypeCode
    {
        public const int Boolean = 4;
        public const int Char = 5;
        public const int Float = 6;
        public const int Double = 7;
        public const int Byte = 8;
        public const int Short = 9;
        public const int Int = 10;
        public const int Long = 11;
    }
}
