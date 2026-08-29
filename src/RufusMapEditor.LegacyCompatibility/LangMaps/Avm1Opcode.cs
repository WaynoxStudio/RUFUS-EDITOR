namespace RufusMapEditor.LegacyCompatibility.LangMaps;

internal static class Avm1Opcode
{
    public const byte End = 0x00;
    public const byte Pop = 0x17;
    public const byte GetVariable = 0x1C;
    public const byte SetVariable = 0x1D;
    public const byte CallFunction = 0x3D;
    public const byte NewObject = 0x40;
    public const byte InitArray = 0x42;
    public const byte InitObject = 0x43;
    public const byte Duplicate = 0x4C;
    public const byte Swap = 0x4D;
    public const byte GetMember = 0x4E;
    public const byte SetMember = 0x4F;
    public const byte CallMethod = 0x52;
    public const byte NewMethod = 0x53;
    public const byte ConstantPool = 0x88;
    public const byte Push = 0x96;
}

internal enum Avm1PushType : byte
{
    String = 0,
    Float = 1,
    Null = 2,
    Undefined = 3,
    Register = 4,
    Boolean = 5,
    Double = 6,
    Integer = 7,
    Constant8 = 8,
    Constant16 = 9,
}
