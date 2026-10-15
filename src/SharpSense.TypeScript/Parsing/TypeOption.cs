namespace SharpSense.TypeScript.Parsing;

internal struct TypeOption<Type1, Type2>
{
    private Type1 _value1 = default!;
    private Type2 _value2 = default!;
    private bool _hasValue;
    private bool _isType2;

    public TypeOption()
    {
    }

    public TypeOption(Type1 value1)
    {
        this._value1 = value1;
        _hasValue = true;
    }

    public TypeOption(Type2 value2)
    {
        this._value2 = value2;
        _isType2 = true;
        _hasValue = true;
    }

    public readonly bool HasValue => _hasValue;

    public readonly bool HasValue1 => _hasValue && _isType2;

    public Type1 Value1
    {
        readonly get => _hasValue ? _value1 : default!;
        set { _value1 = value; _value2 = default!; _hasValue = true; _isType2 = false; }
    }

    public readonly bool HasValue2 => _hasValue && _isType2 == true;

    public Type2 Value2
    {
        readonly get => _value2;
        set { _value1 = default!; _value2 = value; _hasValue = true; _isType2 = true; }
    }

    public void Clear()
    {
        _hasValue = false;
        _value1 = default!;
        _value2 = default!;
    }
}
