using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rinku;
using Xunit;

namespace RinkuLib.Tests.Documentation;

public class XmlApiDocumentationStyleTests {
    private static readonly char[] FancyPunctuation = [';', ':', '\u2014', '\u2013', '\u201C', '\u201D'];
    private static readonly Regex FancyWords = new(
        @"\b(implementation|internally|engine|negotiation|lifecycle|seam|dispatch|retained|compose|ledger|hot path|cold path|backing field|state machine|interlocked|volatile)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ConsumerImplementationWords = new(
        @"\b(internal|compiled|optimized|high[ -]performance|road)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] ProseElements = ["summary", "remarks", "param", "typeparam", "returns", "value", "exception"];

    [Fact]
    public void Packaged_api_prose_stays_short_and_plain() {
        XDocument document = XDocument.Load(GetXmlPath());
        var failures = new List<string>();

        foreach (XElement member in document.Descendants("member")) {
            string name = (string?)member.Attribute("name") ?? "unknown member";
            foreach (XElement block in member.Elements().Where(element => ProseElements.Contains(element.Name.LocalName))) {
                string prose = VisibleProse(block);
                if (prose.Length == 0)
                    continue;

                foreach (char character in FancyPunctuation)
                    if (prose.Contains(character))
                        failures.Add($"{name} uses '{character}' in {block.Name.LocalName}\n{prose}");

                Match word = FancyWords.Match(prose);
                if (word.Success)
                    failures.Add($"{name} uses '{word.Value}' in {block.Name.LocalName}\n{prose}");

                if (prose.Length > 320)
                    failures.Add($"{name} has {prose.Length} characters in {block.Name.LocalName}\n{prose}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    [Fact]
    public void Every_public_type_has_api_documentation() {
        XDocument document = XDocument.Load(GetXmlPath());
        var members = document.Descendants("member").ToDictionary(
            member => (string)member.Attribute("name")!, StringComparer.Ordinal);

        var missing = typeof(QueryCommand).Assembly.GetExportedTypes()
            .Where(type => !type.FullName!.Contains('<'))
            .Select(type => $"T:{type.FullName!.Replace('+', '.')}")
            .Where(name => !HasDocumentation(members, name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, $"Public types without XML documentation\n{string.Join('\n', missing)}");
    }

    [Fact]
    public void Every_public_member_has_api_documentation() {
        XDocument document = XDocument.Load(GetXmlPath());
        var documented = document.Descendants("member").ToDictionary(
            member => (string)member.Attribute("name")!, StringComparer.Ordinal);

        var missing = typeof(QueryCommand).Assembly.GetExportedTypes()
            .Where(IsSourceNamed)
            .SelectMany(GetConsumerMembers)
            .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Where(member => member is not ConstructorInfo constructor || constructor.GetParameters().Length != 0)
            .Where(member => !IsPositionalRecordProperty(member))
            .Select(GetDocumentationId)
            .Where(name => !HasDocumentation(documented, name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, $"Public members without XML documentation\n{string.Join('\n', missing)}");
    }

    [Fact]
    public void Consumer_api_prose_does_not_describe_private_machinery() {
        XDocument document = XDocument.Load(GetXmlPath());
        HashSet<string> publicIds = GetPublicDocumentationIds();
        var failures = new List<string>();

        foreach (XElement member in document.Descendants("member")) {
            string name = (string)member.Attribute("name")!;
            if (!publicIds.Contains(name))
                continue;
            foreach (XElement block in member.Elements().Where(element => ProseElements.Contains(element.Name.LocalName))) {
                string prose = VisibleProse(block);
                Match word = ConsumerImplementationWords.Match(prose);
                if (word.Success)
                    failures.Add($"{name} uses '{word.Value}' in {block.Name.LocalName}\n{prose}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    [Fact]
    public void Generated_api_contains_only_consumer_members() {
        XDocument document = XDocument.Load(GetXmlPath());
        HashSet<string> publicIds = GetAllPublicDocumentationIds();
        string[] unexpected = document.Descendants("member")
            .Select(member => (string)member.Attribute("name")!)
            .Where(name => !publicIds.Contains(name) && !IsConsumerCompilerEntry(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(unexpected.Length == 0, $"Non-public symbols in XML documentation\n{string.Join('\n', unexpected)}");
    }

    private static bool IsConsumerCompilerEntry(string name)
        => name.StartsWith("M:Rinku.DBCommandExtensions.<G>$", StringComparison.Ordinal)
            || name.StartsWith("M:Rinku.DirectBuildExtensions.<G>$", StringComparison.Ordinal)
            || name.StartsWith("M:Rinku.QueryBuilderCommandExtensions.<G>$", StringComparison.Ordinal)
            || name.StartsWith("M:Rinku.QueryBuilderExtensions.<G>$", StringComparison.Ordinal)
            || name == "M:Rinku.Mapping.Parsers.ITypeParser.System#IDisposable#Dispose";

    private static bool HasDocumentation(IReadOnlyDictionary<string, XElement> members, string name)
        => members.TryGetValue(name, out XElement? member)
            && (!string.IsNullOrWhiteSpace(member.Element("summary")?.Value) || member.Element("inheritdoc") is not null);

    private static bool IsSourceNamed(Type type) => !type.FullName!.Contains('<');

    private static HashSet<string> GetPublicDocumentationIds() {
        Type[] types = typeof(QueryCommand).Assembly.GetExportedTypes().Where(IsSourceNamed).ToArray();
        return types.Select(type => $"T:{type.FullName!.Replace('+', '.')}")
            .Concat(types.SelectMany(GetConsumerMembers)
                .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), false))
                .Where(member => member is not ConstructorInfo constructor || constructor.GetParameters().Length != 0)
                .Where(member => !IsPositionalRecordProperty(member))
                .Select(GetDocumentationId))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> GetAllPublicDocumentationIds() {
        Type[] types = typeof(QueryCommand).Assembly.GetExportedTypes().Where(IsSourceNamed).ToArray();
        return types.Select(type => $"T:{type.FullName!.Replace('+', '.')}")
            .Concat(types.SelectMany(GetConsumerMembers).Select(GetDocumentationId))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<MemberInfo> GetConsumerMembers(Type type) {
        if (typeof(MulticastDelegate).IsAssignableFrom(type.BaseType))
            yield break;

        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static;

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
            if (IsVisible(constructor))
                yield return constructor;

        foreach (MethodInfo method in type.GetMethods(flags))
            if (IsVisible(method) && (!method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)))
                yield return method;

        foreach (PropertyInfo property in type.GetProperties(flags))
            if (property.GetAccessors(true).Any(IsVisible))
                yield return property;

        foreach (EventInfo item in type.GetEvents(flags))
            if (item.GetAddMethod(true) is MethodInfo add && IsVisible(add))
                yield return item;

        foreach (FieldInfo field in type.GetFields(flags))
            if (!field.IsSpecialName && IsVisible(field))
                yield return field;
    }

    private static bool IsVisible(MethodBase method)
        => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsVisible(FieldInfo field)
        => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool IsPositionalRecordProperty(MemberInfo member) {
        if (member is not PropertyInfo property)
            return false;
        Type type = property.DeclaringType!;
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance;
        if (type.GetMethod("<Clone>$", flags) is null)
            return false;
        return type.GetConstructors(flags).Any(constructor => constructor.GetParameters()
            .Any(parameter => parameter.Name == property.Name && parameter.ParameterType == property.PropertyType));
    }

    private static string GetDocumentationId(MemberInfo member) {
        string owner = TypeName(member.DeclaringType!, true);
        return member switch {
            ConstructorInfo constructor => $"M:{owner}.{(constructor.IsStatic ? "#cctor" : "#ctor")}{Parameters(constructor.GetParameters())}",
            MethodInfo method => $"M:{owner}.{method.Name}{(method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : "")}{Parameters(method.GetParameters())}{ConversionReturn(method)}",
            PropertyInfo property => $"P:{owner}.{property.Name}{Parameters(property.GetIndexParameters())}",
            EventInfo item => $"E:{owner}.{item.Name}",
            FieldInfo field => $"F:{owner}.{field.Name}",
            _ => throw new NotSupportedException(member.MemberType.ToString())
        };
    }

    private static string ConversionReturn(MethodInfo method)
        => method.Name is "op_Implicit" or "op_Explicit" ? $"~{TypeName(method.ReturnType)}" : "";

    private static string Parameters(ParameterInfo[] parameters)
        => parameters.Length == 0 ? "" : $"({string.Join(',', parameters.Select(parameter => TypeName(parameter.ParameterType)))})";

    private static string TypeName(Type type, bool declaration = false) {
        if (type.IsByRef)
            return $"{TypeName(type.GetElementType()!)}@";
        if (type.IsPointer)
            return $"{TypeName(type.GetElementType()!)}*";
        if (type.IsArray) {
            if (type.GetArrayRank() == 1)
                return $"{TypeName(type.GetElementType()!)}[]";
            return $"{TypeName(type.GetElementType()!)}[{string.Join(',', Enumerable.Repeat("0:", type.GetArrayRank()))}]";
        }
        if (type.IsGenericParameter)
            return $"{(type.DeclaringMethod is null ? "`" : "``")}{type.GenericParameterPosition}";
        if (type.IsGenericType && (!type.IsGenericTypeDefinition || !declaration)) {
            string definition = TypeName(type.GetGenericTypeDefinition(), true);
            int tick = definition.LastIndexOf('`');
            return $"{definition[..tick]}{{{string.Join(',', type.GetGenericArguments().Select(argument => TypeName(argument)))}}}";
        }
        return type.FullName!.Replace('+', '.');
    }

    private static string VisibleProse(XElement block) {
        XElement copy = new(block);
        copy.Descendants().Where(element => element.Name.LocalName is "code" or "c").Remove();
        return Regex.Replace(copy.Value, @"\s+", " ").Trim();
    }

    private static string GetXmlPath() {
        string path = Path.ChangeExtension(typeof(QueryCommand).Assembly.Location, ".xml");
        Assert.True(File.Exists(path), $"Missing generated XML documentation at {path}");
        return path;
    }
}
