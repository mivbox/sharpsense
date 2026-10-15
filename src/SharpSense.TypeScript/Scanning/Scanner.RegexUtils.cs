namespace SharpSense.TypeScript.Scanning;

internal sealed partial class Scanner
{
    private static readonly Dictionary<int, RegularExpressionFlags> _charCodeToRegExpFlag = new()
    {
        [CharacterCodes.LowercaseD] = RegularExpressionFlags.HasIndices,
        [CharacterCodes.LowercaseG] = RegularExpressionFlags.Global,
        [CharacterCodes.LowercaseI] = RegularExpressionFlags.IgnoreCase,
        [CharacterCodes.LowercaseM] = RegularExpressionFlags.Multiline,
        [CharacterCodes.LowercaseS] = RegularExpressionFlags.DotAll,
        [CharacterCodes.LowercaseU] = RegularExpressionFlags.Unicode,
        [CharacterCodes.LowercaseV] = RegularExpressionFlags.UnicodeSets,
        [CharacterCodes.LowercaseY] = RegularExpressionFlags.Sticky
,
    };

    private RegularExpressionFlags? CharacterCodeToRegularExpressionFlag(int ch)
    {
        return _charCodeToRegExpFlag.TryGetValue(ch, out RegularExpressionFlags value) ? value : null;
    }

    private static readonly Dictionary<RegularExpressionFlags, LanguageFeatureMinimumTarget> _regExpFlagToFirstAvailableLanguageVersion = new()
    {
        [RegularExpressionFlags.HasIndices] = LanguageFeatureMinimumTarget.RegularExpressionFlagsHasIndices,
        [RegularExpressionFlags.DotAll] = LanguageFeatureMinimumTarget.RegularExpressionFlagsDotAll,
        [RegularExpressionFlags.Unicode] = LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicode,
        [RegularExpressionFlags.UnicodeSets] = LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicodeSets,
        [RegularExpressionFlags.Sticky] = LanguageFeatureMinimumTarget.RegularExpressionFlagsSticky
    };

    public class RegexGroupNameRef : TextRange
    {
        public string? Name { get; set; }
    }

    public class RegexDecimalEscape : TextRange
    {
        public decimal Value { get; set; }
    }
}
