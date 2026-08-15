using System;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BasedOnLastModifiedCodeFixProvider)), Shared]
public sealed class BasedOnLastModifiedCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [BasedOnAnalyzer.DiagnosticId];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(BasedOnAnalyzer.SchemaTimestampProperty, out var rawTimestamp)
            || !DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var tag = root is null
            ? null
            : DocumentationTags.FindTag(root, diagnostic.Location.SourceSpan, DocumentationTags.BasedOn);
        if (tag is null
            || DocumentationTags.GetTimestamp(tag) is { } current && current >= timestamp)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Acknowledge current schema",
                cancellationToken => ApplyAsync(
                    context.Document,
                    diagnostic.Location.SourceSpan,
                    timestamp,
                    cancellationToken),
                "AcknowledgeBasedOnSchema"),
            diagnostic);
    }

    internal static async Task<Document> ApplyAsync(
        Document document,
        Microsoft.CodeAnalysis.Text.TextSpan diagnosticSpan,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var tag = DocumentationTags.FindTag(root, diagnosticSpan, DocumentationTags.BasedOn);
        if (tag is null)
            return document;

        var updatedRoot = root.ReplaceNode(tag, WithTimestamp(tag, timestamp));
        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newLine = source.Lines.Count > 1
            ? source.ToString(TextSpan.FromBounds(source.Lines[0].End, source.Lines[1].Start))
            : Environment.NewLine;
        var updatedText = NormalizeLineEndings(updatedRoot.ToFullString(), newLine);
        return document.WithText(SourceText.From(updatedText, source.Encoding, source.ChecksumAlgorithm));
    }

    internal static XmlNodeSyntax WithTimestamp(XmlNodeSyntax tag, DateTimeOffset timestamp) {
        var attribute = CreateTimestampAttribute(timestamp);
        return tag switch {
            XmlEmptyElementSyntax empty => empty.WithAttributes(ReplaceOrAdd(empty.Attributes, attribute)),
            XmlElementSyntax element => element.WithStartTag(
                element.StartTag.WithAttributes(ReplaceOrAdd(element.StartTag.Attributes, attribute))),
            _ => tag
        };
    }

    internal static XmlTextAttributeSyntax CreateTimestampAttribute(DateTimeOffset timestamp) {
        var value = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture);
        return SyntaxFactory.XmlTextAttribute(
                SyntaxFactory.XmlName(DocumentationTags.LastUpdated),
                SyntaxFactory.Token(SyntaxKind.EqualsToken),
                SyntaxFactory.Token(SyntaxKind.DoubleQuoteToken),
                SyntaxFactory.TokenList(SyntaxFactory.XmlTextLiteral(value)),
                SyntaxFactory.Token(SyntaxKind.DoubleQuoteToken))
            .WithLeadingTrivia(SyntaxFactory.Whitespace(" "));
    }

    private static SyntaxList<XmlAttributeSyntax> ReplaceOrAdd(
        SyntaxList<XmlAttributeSyntax> attributes,
        XmlTextAttributeSyntax replacement) {
        var existing = attributes
            .OfType<XmlTextAttributeSyntax>()
            .FirstOrDefault(attribute => attribute.Name.LocalName.ValueText == DocumentationTags.LastUpdated);
        return existing is null
            ? attributes.Add(replacement)
            : attributes.Replace(existing, replacement);
    }

    private static string NormalizeLineEndings(string value, string newLine)
        => value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newLine);
}
