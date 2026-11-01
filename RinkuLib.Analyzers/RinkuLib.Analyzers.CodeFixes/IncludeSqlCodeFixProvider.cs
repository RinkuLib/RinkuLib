using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IncludeSqlCodeFixProvider)), Shared]
public sealed class IncludeSqlCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [QueryCommandsAnalyzer.DiagnosticId];
    public override FixAllProvider? GetFixAllProvider() => null;
    public override Task RegisterCodeFixesAsync(CodeFixContext context) {
        string? project = context.Document.Project.FilePath;
        if (project is null)
            return Task.CompletedTask;
        foreach (var diagnostic in context.Diagnostics)
            if (diagnostic.Properties.TryGetValue("Path", out string? path) && path is not null && !string.IsNullOrWhiteSpace(path)) {
                bool file = path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rnksql", StringComparison.OrdinalIgnoreCase);
                if (file)
                    context.RegisterCodeFix(new IncludeSqlAction(project, path, "Include this SQL file"), diagnostic);
                else {
                    context.RegisterCodeFix(new IncludeSqlAction(project, path.TrimEnd('/', '\\') + "/*.sql", "Include SQL files in this folder"), diagnostic);
                    context.RegisterCodeFix(new IncludeSqlAction(project, path.TrimEnd('/', '\\') + "/**/*.sql", "Include SQL files in this folder recursively"), diagnostic);
                }
            }
        return Task.CompletedTask;
    }

    private sealed class IncludeSqlAction(string project, string include, string title) : CodeAction {
        public override string Title => title;
        public override string EquivalenceKey => "RinkuSql:" + include;
        protected override Task<IEnumerable<CodeActionOperation>> ComputeOperationsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<CodeActionOperation>>([new IncludeSqlOperation(project, include)]);
    }
    private sealed class IncludeSqlOperation(string project, string include) : CodeActionOperation {
        public override string Title => "Include SQL source in the project";
        public override void Apply(Workspace workspace, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = XDocument.Load(project, LoadOptions.PreserveWhitespace);
            XElement root = xml.Root ?? throw new InvalidDataException("The project has no root element.");
            XNamespace ns = root.Name.Namespace;
            string normalized = include.Replace('\\', '/');
            if (root.Descendants(ns + "RinkuSql").Any(item => ((string?)item.Attribute("Include"))?.Replace('\\', '/') == normalized))
                return;
            root.Add(new XElement(ns + "ItemGroup", new XElement(ns + "RinkuSql", new XAttribute("Include", normalized))));
            using var writer = new StreamWriter(project, false, new System.Text.UTF8Encoding(false));
            xml.Save(writer, SaveOptions.DisableFormatting);
        }
    }
}
