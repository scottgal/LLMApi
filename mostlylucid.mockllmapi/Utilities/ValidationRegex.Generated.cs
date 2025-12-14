using System.Text.RegularExpressions;

namespace mostlylucid.mockllmapi.Utilities;

/// <summary>
/// Source-generated regular expressions for validation performance
/// </summary>
public static partial class ValidationRegex
{
    /// <summary>
    /// Regex for safe names (letters, numbers, underscore, hyphen)
    /// </summary>
    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    public static partial Regex SafeNameRegex();

    /// <summary>
    /// Regex for path segments (letters, numbers, underscore, hyphen)
    /// </summary>
    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    public static partial Regex PathSegmentRegex();

    /// <summary>
    /// Regex for JSON structure validation
    /// </summary>
    [GeneratedRegex("^[{}\\[\\]\",\\s:a-zA-Z0-9._-]*$", RegexOptions.Compiled)]
    public static partial Regex JsonStructureRegex();

    /// <summary>
    /// Regex for dangerous script tags
    /// </summary>
    [GeneratedRegex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    public static partial Regex ScriptTagRegex();

    /// <summary>
    /// Regex for JavaScript protocol
    /// </summary>
    [GeneratedRegex(@"javascript:", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex JavaScriptProtocolRegex();

    /// <summary>
    /// Regex for VBScript protocol
    /// </summary>
    [GeneratedRegex(@"vbscript:", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex VBScriptProtocolRegex();

    /// <summary>
    /// Regex for event handlers
    /// </summary>
    [GeneratedRegex(@"on\w+\s*=", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex EventHandlerRegex();

    /// <summary>
    /// Regex for eval() function
    /// </summary>
    [GeneratedRegex(@"eval\s*\(", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex EvalFunctionRegex();

    /// <summary>
    /// Regex for CSS expression
    /// </summary>
    [GeneratedRegex(@"expression\s*\(", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex CssExpressionRegex();

    /// <summary>
    /// Regex for @import CSS directive
    /// </summary>
    [GeneratedRegex(@"@import", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex CssImportRegex();

    /// <summary>
    /// Regex for SQL UNION injection
    /// </summary>
    [GeneratedRegex(@"\s+UNION\s+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SqlUnionRegex();

    /// <summary>
    /// Regex for SQL OR injection
    /// </summary>
    [GeneratedRegex(@"\s+OR\s+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SqlOrRegex();

    /// <summary>
    /// Regex for SQL AND injection
    /// </summary>
    [GeneratedRegex(@"\s+AND\s+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SqlAndRegex();

    /// <summary>
    /// Regex for SQL comments
    /// </summary>
    [GeneratedRegex(@"--", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SqlCommentRegex();

    /// <summary>
    /// Regex for SQL block comments
    /// </summary>
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SqlBlockCommentRegex();

    /// <summary>
    /// Regex for SQL exec function
    /// </summary>
    [GeneratedRegex(@"exec\s*\(", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SqlExecRegex();

    /// <summary>
    /// Regex for system() calls
    /// </summary>
    [GeneratedRegex(@"system\s*\(", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex SystemCallRegex();

    /// <summary>
    /// Regex for environment variable substitution
    /// </summary>
    [GeneratedRegex(@"\$\{[^}]*\}", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex EnvVarSubstitutionRegex();

    /// <summary>
    /// Regex for control characters (except newline and tab)
    /// </summary>
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex ControlCharacterRegex();

    // ============================================
    // PROMPT INJECTION DETECTION PATTERNS
    // ============================================

    /// <summary>
    /// Regex for "ignore previous instructions" prompt injection attempts
    /// </summary>
    [GeneratedRegex(@"ignore\s+(all\s+)?(previous|prior|above|earlier)\s+(instructions?|prompts?|rules?|context)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionIgnoreRegex();

    /// <summary>
    /// Regex for "forget" prompt injection attempts
    /// </summary>
    [GeneratedRegex(@"forget\s+(all\s+)?(previous|prior|above|earlier|everything)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionForgetRegex();

    /// <summary>
    /// Regex for "disregard" prompt injection attempts
    /// </summary>
    [GeneratedRegex(@"disregard\s+(all\s+)?(previous|prior|above|earlier)\s+(instructions?|prompts?|rules?)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionDisregardRegex();

    /// <summary>
    /// Regex for "new instructions" prompt injection attempts
    /// </summary>
    [GeneratedRegex(@"(new|actual|real|true)\s+(instructions?|prompts?|task|objective)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionNewInstructionsRegex();

    /// <summary>
    /// Regex for "system prompt" leakage attempts
    /// </summary>
    [GeneratedRegex(@"(reveal|show|display|print|output|tell\s+me)\s+(the\s+)?(system\s+)?(prompt|instructions?|rules?)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionRevealRegex();

    /// <summary>
    /// Regex for "pretend/act as" jailbreak attempts
    /// </summary>
    [GeneratedRegex(@"(pretend|act|behave|roleplay|imagine)\s+(you\s+are|as|like)\s+(a\s+)?(different|another|new)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionPretendRegex();

    /// <summary>
    /// Regex for DAN (Do Anything Now) jailbreak attempts
    /// </summary>
    [GeneratedRegex(@"\bDAN\b|do\s+anything\s+now|jailbreak", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionDanRegex();

    /// <summary>
    /// Regex for delimiter escape attempts (trying to break out of user input sections)
    /// </summary>
    [GeneratedRegex(@"(```|---|\[\[|\]\]|<<|>>|END\s+OF\s+INPUT|BEGIN\s+SYSTEM)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    public static partial Regex PromptInjectionDelimiterRegex();

    /// <summary>
    /// Regex for whitespace normalization
    /// </summary>
    [GeneratedRegex(@"\s+", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex WhitespaceRegex();

    /// <summary>
    /// Array of all dangerous pattern regexes for easy iteration
    /// </summary>
    public static Regex[] DangerousPatterns =>
    [
        ScriptTagRegex(),
        JavaScriptProtocolRegex(),
        VBScriptProtocolRegex(),
        EventHandlerRegex(),
        EvalFunctionRegex(),
        CssExpressionRegex(),
        CssImportRegex(),
        SqlUnionRegex(),
        SqlOrRegex(),
        SqlAndRegex(),
        SqlCommentRegex(),
        SqlBlockCommentRegex(),
        SqlExecRegex(),
        SystemCallRegex(),
        EnvVarSubstitutionRegex()
    ];

    /// <summary>
    /// Array of prompt injection detection patterns
    /// </summary>
    public static Regex[] PromptInjectionPatterns =>
    [
        PromptInjectionIgnoreRegex(),
        PromptInjectionForgetRegex(),
        PromptInjectionDisregardRegex(),
        PromptInjectionNewInstructionsRegex(),
        PromptInjectionRevealRegex(),
        PromptInjectionPretendRegex(),
        PromptInjectionDanRegex(),
        PromptInjectionDelimiterRegex()
    ];

    // ============================================
    // JSON EXTRACTION PATTERNS
    // ============================================

    /// <summary>
    /// Regex for extracting JSON from markdown code blocks (```json ... ``` or ```graphql ... ```)
    /// </summary>
    [GeneratedRegex(@"```(?:json|graphql)?\s*(\{[\s\S]*?\}|\[[\s\S]*?\])\s*```", RegexOptions.Compiled, matchTimeoutMilliseconds: 5000)]
    public static partial Regex JsonMarkdownCodeBlockRegex();

    /// <summary>
    /// Regex for matching JSON objects (non-greedy) - use for fallback after balanced extraction
    /// </summary>
    [GeneratedRegex(@"\{[\s\S]*?\}", RegexOptions.Compiled, matchTimeoutMilliseconds: 5000)]
    public static partial Regex JsonObjectRegex();

    /// <summary>
    /// Regex for matching JSON arrays (non-greedy) - use for fallback after balanced extraction
    /// </summary>
    [GeneratedRegex(@"\[[\s\S]*?\]", RegexOptions.Compiled, matchTimeoutMilliseconds: 5000)]
    public static partial Regex JsonArrayRegex();

    // ============================================
    // LLM ARTIFACT CLEANUP PATTERNS
    // ============================================

    /// <summary>
    /// Regex for ellipsis in quoted strings ("...")
    /// </summary>
    [GeneratedRegex(@"""\.\.\.""", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmEllipsisQuotedRegex();

    /// <summary>
    /// Regex for ellipsis after colon (: ...)
    /// </summary>
    [GeneratedRegex(@":\s*\.\.\.(?=[,\}\]])", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmEllipsisAfterColonRegex();

    /// <summary>
    /// Regex for ellipsis before closing brackets (, ...)
    /// </summary>
    [GeneratedRegex(@",\s*\.\.\.(?=[\}\]])", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmEllipsisBeforeCloseRegex();

    /// <summary>
    /// Regex for entire lines with just ellipsis
    /// </summary>
    [GeneratedRegex(@"^\s*\.\.\..*$", RegexOptions.Multiline | RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmEllipsisLineRegex();

    /// <summary>
    /// Regex for C-style comments (// ...)
    /// </summary>
    [GeneratedRegex(@"//[^\n]*", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmCStyleCommentRegex();

    /// <summary>
    /// Regex for trailing commas before closing brackets
    /// </summary>
    [GeneratedRegex(@",(\s*[\}\]])", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmTrailingCommaRegex();

    /// <summary>
    /// Regex for leading commas after opening brackets ([, ...)
    /// </summary>
    [GeneratedRegex(@"\[\s*,\s*", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmLeadingCommaRegex();

    /// <summary>
    /// Regex for consecutive commas (,,)
    /// </summary>
    [GeneratedRegex(@",\s*,\s*", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex LlmConsecutiveCommaRegex();

    // ============================================
    // ERROR MESSAGE SANITIZATION PATTERNS
    // ============================================

    /// <summary>
    /// Regex for HTTP/HTTPS URLs
    /// </summary>
    [GeneratedRegex(@"https?://\S+", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex HttpUrlRegex();

    /// <summary>
    /// Regex for file:// URLs
    /// </summary>
    [GeneratedRegex(@"file://\S+", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex FileUrlRegex();

    /// <summary>
    /// Regex for Windows file paths
    /// </summary>
    [GeneratedRegex(@"[a-zA-Z]:\\[^\\]+", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    public static partial Regex WindowsPathRegex();
}