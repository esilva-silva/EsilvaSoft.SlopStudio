using EsilvaSoft.SlopStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

/// <summary>
/// Pure, bounded resolution using MongoLexer tokens. Supports top-level static chains and unique, earlier const
/// namespace aliases. Opaque scopes, dynamic arguments and ambiguous syntax return Unknown. It performs no I/O,
/// JavaScript evaluation or lookup of saved profiles. A ContextEngine may use ConnectionName to resolve an identity separately.
/// </summary>
public static class NamespaceTargetResolver
{
    public const int MaximumDocumentLength = 65_536;
    public const int MaximumTokens = 16_384;
    public const int MaximumDepth = 512;

    /// <summary>
    /// Resolves the top-level expression containing the UTF-16 caret, including arguments of known MongoDB methods.
    /// Only AggregationJson uses the tab collection as a default. Larger documents require a trusted statement window
    /// from the caller; this resolver never truncates a document and guesses its lexical state or distant aliases.
    /// </summary>
    public static NamespaceTarget Resolve(string document, int caret, NamespaceTarget? tabTarget = null,
        EditorDialects dialect = EditorDialects.Console, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, document.Length);
        cancellationToken.ThrowIfCancellationRequested();
        if (dialect == EditorDialects.AggregationJson)
            return tabTarget is null ? NamespaceTarget.Unknown : tabTarget with { Confidence = NamespaceTargetConfidence.TabDefault };
        if (dialect is not (EditorDialects.Console or EditorDialects.MongoshScript) || document.Length > MaximumDocumentLength)
            return NamespaceTarget.Unknown;

        var tokens = new List<MongoToken>();
        var state = default(MongoLexerState);
        for (var start = 0; start < document.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = MongoLexer.LineLength(document, start);
            var lexer = new MongoLexer(document.AsSpan(start, length), state, offset: start, cancellationToken: cancellationToken);
            while (lexer.TryRead(out var token))
            {
                // Do not interpret roots inside opaque lexical content, including an open comment at EOF.
                if (token.Start < caret && (caret < token.End || caret == token.End &&
                    (!token.IsTerminated || token.Kind == MongoTokenKind.LineComment)) &&
                    (token.IsComment || token.Kind is MongoTokenKind.Regex or MongoTokenKind.Template))
                    return NamespaceTarget.Unknown;
                if (!token.IsComment) tokens.Add(token);
                if (tokens.Count > MaximumTokens) return NamespaceTarget.Unknown;
            }
            state = lexer.State;
            start += length;
        }
        return new NamespaceChainReader(document, tokens, tabTarget, cancellationToken).Resolve(caret);
    }
}
