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
    public override ImmutableArray<string> FixableDiagnosticIds => [BasedOnAnalyzer.DiagnosticId, SyncBasedOnAnalyzer.DiagnosticId];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(BasedOnAnalyzer.SchemaTimestampProperty, out var rawTimestamp)
            || !DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var attribute = root is null ? null : DocumentationTags.FindTag(root, diagnostic.Location.SourceSpan, DocumentationTags.BasedOn);
        if (attribute is null || GetTimestamp(attribute) is { } current && current >= timestamp)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Acknowledge current schema",
                cancellationToken => ApplyAsync(context.Document, diagnostic.Location.SourceSpan, timestamp, cancellationToken),
                "AcknowledgeBasedOnSchema"),
            diagnostic);
    }

    internal static async Task<Document> ApplyAsync(Document document, TextSpan diagnosticSpan, DateTimeOffset timestamp, CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || DocumentationTags.FindTag(root, diagnosticSpan, DocumentationTags.BasedOn) is not { } attribute)
            return document;

        var updatedRoot = root.ReplaceNode(attribute, WithTimestamp(attribute, timestamp));
        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newLine = source.Lines.Count > 1 ? source.ToString(TextSpan.FromBounds(source.Lines[0].End, source.Lines[1].Start)) : Environment.NewLine;
        var updatedText = updatedRoot.ToFullString().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newLine);
        return document.WithText(SourceText.From(updatedText, source.Encoding, source.ChecksumAlgorithm));
    }

    internal static AttributeSyntax WithTimestamp(AttributeSyntax attribute, DateTimeOffset timestamp) {
        string value = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var replacement = SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value)));
        var arguments = attribute.ArgumentList?.Arguments ?? default;
        var values = new AttributeArgumentSyntax?[3];
        int position = 0;
        foreach (var argument in arguments) {
            if (argument.NameEquals is not null)
                continue;
            int index = argument.NameColon?.Name.Identifier.ValueText switch {
                DocumentationTags.DeclaringTypeParameter => 0,
                DocumentationTags.LastUpdatedParameter => 1,
                DocumentationTags.MethodNameParameter => 2,
                _ => position
            };
            values[index] = argument.WithNameColon(null);
            position++;
        }
        values[1] = replacement;
        var ordered = values.Where(argument => argument is not null).Select((argument, index) => argument!.WithLeadingTrivia(index == 0 ? default(SyntaxTriviaList) : SyntaxFactory.TriviaList(SyntaxFactory.Space)));
        arguments = SyntaxFactory.SeparatedList(ordered.Concat(arguments.Where(argument => argument.NameEquals is not null)));
        return attribute.WithArgumentList(SyntaxFactory.AttributeArgumentList(arguments));
    }

    private static DateTimeOffset? GetTimestamp(AttributeSyntax attribute) {
        var argument = DocumentationTags.GetTimestampArgument(attribute);
        return argument?.Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            && DateTimeOffset.TryParse(literal.Token.ValueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)
                ? timestamp
                : null;
    }
}
