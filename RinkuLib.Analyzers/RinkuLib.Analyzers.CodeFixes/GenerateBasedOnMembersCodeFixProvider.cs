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

namespace RinkuLib.Analyzers {
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GenerateBasedOnMembersCodeFixProvider)), Shared]
    public sealed class GenerateBasedOnMembersCodeFixProvider : CodeFixProvider {
        public override ImmutableArray<string> FixableDiagnosticIds
            => [BasedOnAnalyzer.DiagnosticId];

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var declaration = root?.FindToken(diagnosticSpan.Start)
                .Parent?
                .AncestorsAndSelf()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();

            if (declaration == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Generate missing members from based-on type",
                    c => GenerateMembersAsync(context.Document, declaration, c),
                    "GenerateBasedOnMembers"),
                diagnostic);
        }

        private async Task<Document> GenerateMembersAsync(Document document,  TypeDeclarationSyntax declaration, CancellationToken cancellationToken) {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (semanticModel == null)
                return document;

            var targetType = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);

            if (targetType == null)
                return document;

            if (!BasedOnHelper.TryGetBasedOnType(targetType, semanticModel.Compilation, cancellationToken, out var basedOnType))
                return document;


            if (basedOnType!.DeclaringSyntaxReferences.FirstOrDefault()?
                .GetSyntax(cancellationToken) is not TypeDeclarationSyntax sourceDeclaration)
                return document;

            var properties = ExtractProperties(sourceDeclaration);

            var membersToAdd = new List<MemberDeclarationSyntax>();

            foreach (var property in properties) {
                if (ContainsMember(targetType, property.Name))
                    continue;

                membersToAdd.Add(CreateProperty(property));
            }

            if (membersToAdd.Count == 0)
                return document;

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
                return document;
            var trackedRoot = root.TrackNodes(declaration);
            var currentDeclaration = trackedRoot.GetCurrentNode(declaration);
            if (currentDeclaration is null)
                return document;
            var updatedDeclaration = currentDeclaration.AddMembers(membersToAdd.ToArray());
            var updatedRoot = trackedRoot.ReplaceNode(currentDeclaration, updatedDeclaration);

            return document.WithSyntaxRoot(updatedRoot);
        }

        private static bool ContainsMember(INamedTypeSymbol type, string name) {
            foreach (var member in type.GetMembers())
                if (member.Name == name)
                    return true;
            return false;
        }

        private static List<PropertyInfo> ExtractProperties(TypeDeclarationSyntax source) {
            var properties = new List<PropertyInfo>();

            foreach (var member in source.Members) {
                if (member is not PropertyDeclarationSyntax property)
                    continue;

                properties.Add(new PropertyInfo(property.Identifier.Text, property.Type, TryGetTrueName(property.AttributeLists)));
            }

            if (source is RecordDeclarationSyntax record && record.ParameterList != null) {

                foreach (var parameter in record.ParameterList.Parameters) {
                    if (parameter.Type == null)
                        continue;

                    properties.Add(new PropertyInfo(parameter.Identifier.Text, parameter.Type, TryGetTrueName(parameter.AttributeLists)));
                }
            }

            return properties;
        }

        private static string? TryGetTrueName(SyntaxList<AttributeListSyntax> attributeLists) {

            foreach (var list in attributeLists) {
                foreach (var attribute in list.Attributes) {
                    var name = attribute.Name.ToString();

                    if (name != "TrueName"
                        && name != "TrueNameAttribute"
                        && !name.EndsWith(".TrueName")
                        && !name.EndsWith(".TrueNameAttribute"))
                        continue;

                    var arguments = attribute.ArgumentList?.Arguments;

                    if (arguments == null || arguments.Value.Count == 0)
                        return null;

                    var expression = arguments.Value[0].Expression;

                    if (expression is LiteralExpressionSyntax literal)
                        return literal.Token.ValueText;
                }
            }

            return null;
        }

        private static PropertyDeclarationSyntax CreateProperty(PropertyInfo property) {
            var declaration =
                SyntaxFactory.PropertyDeclaration(property.Type, property.Name)
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                    .WithAccessorList(
                        SyntaxFactory.AccessorList(
                            SyntaxFactory.List(new[] {
                                SyntaxFactory.AccessorDeclaration(
                                        SyntaxKind.GetAccessorDeclaration)
                                    .WithSemicolonToken(
                                        SyntaxFactory.Token(SyntaxKind.SemicolonToken)),

                                SyntaxFactory.AccessorDeclaration(
                                        SyntaxKind.SetAccessorDeclaration)
                                    .WithSemicolonToken(
                                        SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                            })))
                    .WithAdditionalAnnotations(Formatter.Annotation);

            if (property.TrueName != null) {
                declaration = declaration.AddAttributeLists(
                    SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("TrueName"),
                                SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                                            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(property.TrueName)
                    )))))))
                );
            }

            return declaration;
        }

        private sealed class PropertyInfo(string name, TypeSyntax type, string? trueName) {
            public string Name { get; } = name;
            public TypeSyntax Type { get; } = type;
            public string? TrueName { get; } = trueName;
        }
    }
}