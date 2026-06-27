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

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GenerateCtorCodeFixProvider)), Shared]
public sealed class GenerateCtorCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [BasedOnAnalyzer.DiagnosticId];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var document = context.Document;
        var ct = context.CancellationToken;

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        var diagnostic = context.Diagnostics.First();

        var declaration = root?
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (declaration is null || semanticModel is null)
            return;

        var targetType = semanticModel.GetDeclaredSymbol(declaration, ct);
        if (targetType is null)
            return;

        var basedOnSymbols = BasedOnHelper.GetBasedOnSymbols(targetType, semanticModel.Compilation, ct);

        foreach (var symbol in basedOnSymbols)
            foreach (var candidate in GetCandidates(symbol))
                RegisterCandidateFix(context, document, declaration, candidate);
    }

    private static void RegisterCandidateFix(CodeFixContext context, Document document, TypeDeclarationSyntax declaration, IMethodSymbol candidate) {
        var title = $"Generate constructor from {candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";

        var ctOnly = CodeAction.Create(
            title: "Ctor only",
            createChangedDocument: ct => GenerateAsync(document, declaration, candidate, false, ct),
            equivalenceKey: $"{title}-CtorOnly");

        var withProps = CodeAction.Create(
            title: "Ctor and properties",
            createChangedDocument: ct => GenerateAsync(document, declaration, candidate, true, ct),
            equivalenceKey: $"{title}-CtorProps");

        var group = CodeAction.Create(title, [ctOnly, withProps], isInlinable: false);
        context.RegisterCodeFix(group, context.Diagnostics.First());
    }

    private static IEnumerable<IMethodSymbol> GetCandidates(ISymbol symbol) {
        return symbol switch {
            INamedTypeSymbol type => type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared),
            IMethodSymbol method => [method],
            _ => []
        };
    }
    private static async Task<Document> GenerateAsync(Document document, TypeDeclarationSyntax targetDeclaration, IMethodSymbol candidate, bool includeProperties, CancellationToken ct) {
        var updatedDeclaration = BasedOnLastModifiedCodeFixProvider.WithUpdatedTimestamp(targetDeclaration);
        var ctor = CreateConstructor(updatedDeclaration.Identifier.Text, candidate.Parameters, includeProperties);
        updatedDeclaration = includeProperties
            ? updatedDeclaration.AddMembers([ctor, .. CreateProperties(candidate.Parameters)])
            : updatedDeclaration.AddMembers(ctor);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return document;
        var newRoot = root.ReplaceNode(targetDeclaration, updatedDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }
    private static ConstructorDeclarationSyntax CreateConstructor(string typeName, ImmutableArray<IParameterSymbol> parameters, bool includeAssignments) {
        var parameterSyntaxes = new List<ParameterSyntax>();

        foreach (var p in parameters) {
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                .WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

            var attributes = p.GetAttributes();
            if (attributes.Length > 0) {
                var attributeLists = new List<AttributeListSyntax>();
                foreach (var attr in attributes) {
                    var name = attr.AttributeClass!.Name.Replace("Attribute", "");

                    AttributeArgumentListSyntax? argList = null;
                    if (attr.ConstructorArguments.Length > 0) {
                        var args = new List<AttributeArgumentSyntax>();
                        foreach (var arg in attr.ConstructorArguments) {
                            args.Add(SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression(arg.ToCSharpString())));
                        }
                        argList = SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(args));
                    }

                    attributeLists.Add(SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(SyntaxFactory.ParseName(name), argList))));
                }
                parameter = parameter.WithAttributeLists(SyntaxFactory.List(attributeLists));
            }
            parameterSyntaxes.Add(parameter);
        }

        var statements = new List<StatementSyntax>();
        if (includeAssignments) {
            foreach (var p in parameters) {
                statements.Add(CreateAssignmentStatement(p));
            }
        }

        return SyntaxFactory.ConstructorDeclaration(typeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameterSyntaxes)))
            .WithBody(SyntaxFactory.Block(statements))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
    private static StatementSyntax CreateAssignmentStatement(IParameterSymbol p) {
        var propName = ToPropertyName(p.Name);
        var paramName = p.Name;

        ExpressionSyntax leftSide = (paramName == propName)
            ? SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ThisExpression(),
                SyntaxFactory.IdentifierName(propName))
            : SyntaxFactory.IdentifierName(propName);

        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, leftSide, SyntaxFactory.IdentifierName(paramName))
            );
    }

    private static IEnumerable<PropertyDeclarationSyntax> CreateProperties(ImmutableArray<IParameterSymbol> parameters) {
        return parameters.Select(p => {
            var name = p.Name;
            var propName = ToPropertyName(name);
            var typeSyntax = SyntaxFactory.ParseTypeName(p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

            return SyntaxFactory.PropertyDeclaration(typeSyntax, propName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List([
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        ])))
                .WithAdditionalAnnotations(Formatter.Annotation);
        });
    }

    private static string ToPropertyName(string name)
        => string.IsNullOrEmpty(name)
            ? name : $"{char.ToUpperInvariant(name[0])}{name.Substring(1)}";
}