using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GenerateCtorCodeFixProvider)), Shared]
public sealed class GenerateCtorCodeFixProvider : CodeFixProvider {
    private static readonly SymbolDisplayFormat TypeDisplayFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.MinimallyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public override ImmutableArray<string> FixableDiagnosticIds =>
        [BasedOnAnalyzer.DiagnosticId, MatchConstructorAnalyzer.DiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var diagnostic = context.Diagnostics.First();
        var tagName = diagnostic.Id == MatchConstructorAnalyzer.DiagnosticId
            ? DocumentationTags.MatchConstructor
            : DocumentationTags.BasedOn;
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
            return;

        var declaration = DocumentationTags.FindContainingType(root, diagnostic.Location.SourceSpan);
        var tag = DocumentationTags.FindTag(root, diagnostic.Location.SourceSpan, tagName);
        if (declaration is null
            || tag is null
            || semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol targetType)
            return;

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        foreach (var symbol in DocumentationTags.ResolveCref(tag, semanticModel.Compilation, context.CancellationToken)) {
            foreach (var candidate in ConstructorContract.GetCandidates(symbol)) {
                if (ConstructorContract.MatchesAny(targetType, candidate)
                    || ConstructorContract.HasConflictingSignature(targetType, candidate))
                    continue;

                var display = candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                actions.Add(CodeAction.Create(
                    $"Add constructor from {display}",
                    cancellationToken => GenerateAsync(
                        context.Document,
                        declaration,
                        diagnostic.Location.SourceSpan,
                        tagName,
                        candidate,
                        includeProperties: false,
                        cancellationToken),
                    $"AddConstructor-{display}"));

                if (CanAddProperties(targetType, candidate.Parameters)) {
                    actions.Add(CodeAction.Create(
                        $"Add constructor and properties from {display}",
                        cancellationToken => GenerateAsync(
                            context.Document,
                            declaration,
                            diagnostic.Location.SourceSpan,
                            tagName,
                            candidate,
                            includeProperties: true,
                            cancellationToken),
                        $"AddConstructorAndProperties-{display}"));
                }
            }
        }

        if (actions.Count == 0)
            return;

        foreach (var action in actions)
            context.RegisterCodeFix(action, diagnostic);
    }

    private static async Task<Document> GenerateAsync(
        Document document,
        TypeDeclarationSyntax declaration,
        TextSpan diagnosticSpan,
        string tagName,
        IMethodSymbol candidate,
        bool includeProperties,
        CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var updated = includeProperties
            ? declaration.AddMembers([CreateConstructor(declaration.Identifier.ValueText, candidate.Parameters, true), .. CreateProperties(candidate.Parameters)])
            : declaration.AddMembers(CreateConstructor(declaration.Identifier.ValueText, candidate.Parameters, false));

        if (tagName == DocumentationTags.BasedOn) {
            var tag = DocumentationTags.FindTag(updated, diagnosticSpan, DocumentationTags.BasedOn);
            var schemaOwner = candidate.MethodKind == MethodKind.Constructor
                ? candidate.ContainingType
                : (ISymbol)candidate;
            var timestamp = DocumentationTags.GetLatestSchemaTimestamp(schemaOwner, cancellationToken);
            if (tag is not null && timestamp.HasValue)
                updated = updated.ReplaceNode(tag, BasedOnLastModifiedCodeFixProvider.WithTimestamp(tag, timestamp.Value));
        }

        var changed = document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newLine = source.Lines.Count > 1
            ? source.ToString(TextSpan.FromBounds(source.Lines[0].End, source.Lines[1].Start))
            : Environment.NewLine;
        var options = (await changed.GetOptionsAsync(cancellationToken).ConfigureAwait(false))
            .WithChangedOption(FormattingOptions.NewLine, newLine);
        var formatted = await Formatter.FormatAsync(
            changed,
            Formatter.Annotation,
            options,
            cancellationToken).ConfigureAwait(false);
        var formattedRoot = await formatted.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return formattedRoot is null
            ? formatted
            : formatted.WithSyntaxRoot(formattedRoot.WithoutAnnotations(Formatter.Annotation));
    }

    private static ConstructorDeclarationSyntax CreateConstructor(
        string typeName,
        ImmutableArray<IParameterSymbol> parameters,
        bool includeAssignments) {
        var parameterSyntaxes = parameters.Select(CreateParameter);
        var statements = includeAssignments
            ? parameters.Select(CreateAssignment)
            : Enumerable.Empty<StatementSyntax>();

        return SyntaxFactory.ConstructorDeclaration(typeName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameterSyntaxes)))
            .WithBody(SyntaxFactory.Block(statements))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static ParameterSyntax CreateParameter(IParameterSymbol parameter) {
        var syntax = SyntaxFactory.Parameter(CreateIdentifier(parameter.Name))
            .WithType(SyntaxFactory.ParseTypeName(parameter.Type.ToDisplayString(TypeDisplayFormat)));

        var modifier = parameter.RefKind switch {
            RefKind.Out => SyntaxKind.OutKeyword,
            RefKind.Ref => SyntaxKind.RefKeyword,
            RefKind.In => SyntaxKind.InKeyword,
            _ when parameter.IsParams => SyntaxKind.ParamsKeyword,
            _ => SyntaxKind.None
        };
        if (modifier != SyntaxKind.None)
            syntax = syntax.AddModifiers(SyntaxFactory.Token(modifier));

        var attributes = CreateAttributes(parameter.GetAttributes());
        return attributes.Count == 0 ? syntax : syntax.WithAttributeLists(attributes);
    }

    private static SyntaxList<AttributeListSyntax> CreateAttributes(ImmutableArray<AttributeData> attributes) {
        var lists = new List<AttributeListSyntax>();
        foreach (var attribute in attributes) {
            if (attribute.AttributeClass is not { TypeKind: not TypeKind.Error } attributeType)
                continue;

            var arguments = new List<AttributeArgumentSyntax>();
            var valid = true;
            foreach (var value in attribute.ConstructorArguments) {
                if (value.Kind == TypedConstantKind.Error) {
                    valid = false;
                    break;
                }
                arguments.Add(SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression(value.ToCSharpString())));
            }
            if (!valid)
                continue;

            foreach (var pair in attribute.NamedArguments) {
                if (pair.Value.Kind == TypedConstantKind.Error) {
                    valid = false;
                    break;
                }
                arguments.Add(
                    SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression(pair.Value.ToCSharpString()))
                        .WithNameEquals(SyntaxFactory.NameEquals(pair.Key)));
            }
            if (!valid)
                continue;

            var name = attributeType.ToDisplayString(TypeDisplayFormat);
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Attribute".Length);
            var attributeSyntax = SyntaxFactory.Attribute(SyntaxFactory.ParseName(name));
            if (arguments.Count > 0)
                attributeSyntax = attributeSyntax.WithArgumentList(
                    SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(arguments)));
            lists.Add(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attributeSyntax)));
        }
        return SyntaxFactory.List(lists);
    }

    private static StatementSyntax CreateAssignment(IParameterSymbol parameter) {
        var propertyName = ToPropertyName(parameter.Name);
        var left = propertyName == parameter.Name
            ? (ExpressionSyntax)SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ThisExpression(),
                SyntaxFactory.IdentifierName(CreateIdentifier(propertyName)))
            : SyntaxFactory.IdentifierName(CreateIdentifier(propertyName));
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                left,
                SyntaxFactory.IdentifierName(CreateIdentifier(parameter.Name))));
    }

    private static IEnumerable<PropertyDeclarationSyntax> CreateProperties(ImmutableArray<IParameterSymbol> parameters) {
        foreach (var parameter in parameters) {
            yield return SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.ParseTypeName(parameter.Type.ToDisplayString(TypeDisplayFormat)),
                    CreateIdentifier(ToPropertyName(parameter.Name)))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List([
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                ])))
                .WithAdditionalAnnotations(Formatter.Annotation);
        }
    }

    private static bool CanAddProperties(INamedTypeSymbol target, ImmutableArray<IParameterSymbol> parameters) {
        foreach (var parameter in parameters) {
            if (parameter.RefKind == RefKind.Out
                || target.GetMembers(ToPropertyName(parameter.Name)).Length > 0)
                return false;
        }
        return true;
    }

    private static string ToPropertyName(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    private static SyntaxToken CreateIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.Identifier("@" + name);
}
