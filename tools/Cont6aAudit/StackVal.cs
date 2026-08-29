using System.Globalization;

internal readonly struct StackVal
{
    public string? Path { get; init; }
    public string? Str { get; init; }
    public int? Int { get; init; }
    public static StackVal Obj(string path) => new() { Path = path, Str = path };
    public static StackVal Text(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return new StackVal { Str = s, Int = n };
        return new StackVal { Str = s };
    }
    public static StackVal Number(int n) => new() { Int = n, Str = n.ToString(CultureInfo.InvariantCulture) };
    public static StackVal Unk(string s) => new() { Str = s };
    public string? AsName() => Path ?? Str;
}
