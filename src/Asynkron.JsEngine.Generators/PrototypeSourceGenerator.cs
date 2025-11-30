using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asynkron.JsEngine.Generators;

[Generator]
public sealed class PrototypeSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var prototypes = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => Transform(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(prototypes, static (spc, info) => Emit(spc, info));

        var constructors = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => TransformConstructor(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(constructors, static (spc, info) => EmitConstructor(spc, info));
    }

    private static PrototypeInfo? Transform(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        var prototypeAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() ==
                                    "Asynkron.JsEngine.Runtime.Prototypes.JsPrototypeAttribute");
        if (prototypeAttr is null)
        {
            return null;
        }

        var getters = ImmutableArray.CreateBuilder<GetterInfo>();
        var methods = ImmutableArray.CreateBuilder<MethodInfo>();

        foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (attrName == "Asynkron.JsEngine.Runtime.Prototypes.JsHostGetterAttribute")
                {
                    var propertyName = attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        continue;
                    }

                    var displayName = GetNamedValue(attr, "DisplayName") ?? $"get {propertyName}";
                    var enumerable = GetNamedBool(attr, "Enumerable");
                    var configurable = GetNamedBool(attr, "Configurable", defaultValue: true);

                    getters.Add(new GetterInfo(member, propertyName, displayName, enumerable, configurable));
                }
                else if (attrName == "Asynkron.JsEngine.Runtime.Prototypes.JsHostMethodAttribute")
                {
                    var propertyName = attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        continue;
                    }

                    var lengthLiteral = GetNamedDouble(attr, "Length");
                    var displayName = GetNamedValue(attr, "DisplayName") ?? propertyName;
                    var enumerable = GetNamedBool(attr, "Enumerable");
                    var configurable = GetNamedBool(attr, "Configurable", true);
                    var writable = GetNamedBool(attr, "Writable", true);

                    methods.Add(new MethodInfo(member, propertyName, displayName, lengthLiteral, enumerable,
                        configurable, writable));
                }
            }
        }

        var toStringTag = GetNamedValue(prototypeAttr, "ToStringTag");
        return new PrototypeInfo(typeSymbol, getters.ToImmutable(), methods.ToImmutable(), toStringTag);
    }

    private static ConstructorInfo? TransformConstructor(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        var constructorAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() ==
                                    "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorAttribute");
        if (constructorAttr is null)
        {
            return null;
        }

        if (!InheritsFrom(typeSymbol, "Asynkron.JsEngine.Runtime.Prototypes.JsConstructor"))
        {
            return null;
        }

        var prototypeTypeSymbol = constructorAttr.NamedArguments
            .Where(pair => pair.Key == "PrototypeType")
            .Select(pair => pair.Value.Value)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

        if (prototypeTypeSymbol is null)
        {
            return null;
        }

        var lengthLiteral = GetNamedDouble(constructorAttr, "Length");
        var displayName = GetNamedValue(constructorAttr, "DisplayName") ?? typeSymbol.Name;

        return new ConstructorInfo(typeSymbol, prototypeTypeSymbol, lengthLiteral, displayName);
    }

    private static void Emit(SourceProductionContext context, PrototypeInfo info)
    {
        var ns = info.Symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : info.Symbol.ContainingNamespace.ToDisplayString();

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using Asynkron.JsEngine.Ast;");
        source.AppendLine("using Asynkron.JsEngine.JsTypes;");
        source.AppendLine("using Asynkron.JsEngine.Runtime;");
        source.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            source.Append("namespace ").Append(ns).AppendLine(";");
            source.AppendLine();
        }

        source.Append("public sealed partial class ").Append(info.Symbol.Name).AppendLine();
        source.AppendLine("{");
        source.AppendLine("    public static JsObject CreatePrototype(RealmState realm)");
        source.AppendLine("    {");
        source.AppendLine("        var prototype = new JsObject();");
        source.Append("        var typed = new ").Append(info.Symbol.Name)
            .AppendLine("(prototype, realm);");

        foreach (var getter in info.Getters)
        {
            var getterVar = $"getter_{Sanitize(getter.PropertyName)}";
            source.Append("        var ").Append(getterVar)
                .Append(" = new HostFunction((thisValue, _) => typed.")
                .Append(getter.MethodSymbol.Name)
                .AppendLine("(thisValue)) { IsConstructor = false };");
            source.Append("        ").Append(getterVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(getter.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            source.Append("        prototype.DefineProperty(\"").Append(getter.PropertyName)
                .Append("\", new PropertyDescriptor { Get = ").Append(getterVar)
                .Append(", Enumerable = ").Append(getter.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(getter.Configurable ? "true" : "false")
                .Append(" });").AppendLine();
        }

        foreach (var method in info.Methods)
        {
            var methodVar = $"method_{Sanitize(method.PropertyName)}";
            source.Append("        var ").Append(methodVar)
                .Append(" = new HostFunction((thisValue, args) => typed.")
                .Append(method.MethodSymbol.Name)
                .AppendLine("(thisValue, args)) { IsConstructor = false };");
            source.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(method.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            source.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(method.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });").AppendLine();
            source.Append("        prototype.DefineProperty(\"").Append(method.PropertyName)
                .Append("\", new PropertyDescriptor { Value = ").Append(methodVar)
                .Append(", Writable = ").Append(method.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(method.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(method.Configurable ? "true" : "false")
                .Append(" });").AppendLine();
        }

        if (!string.IsNullOrEmpty(info.ToStringTag))
        {
            source.AppendLine("        prototype.DefineProperty($\"@@symbol:{TypedAstSymbol.For(\"Symbol.toStringTag\").GetHashCode()}\",");
            source.AppendLine("            new PropertyDescriptor");
            source.AppendLine("            {");
            source.Append("                Value = \"").Append(info.ToStringTag.Replace("\"", "\\\""))
                .AppendLine("\", Writable = false, Enumerable = false, Configurable = true");
            source.AppendLine("            });");
        }

        source.AppendLine("        typed.ConfigurePrototype();");
        source.AppendLine("        return prototype;");
        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource($"{info.Symbol.Name}.Prototype.g.cs", source.ToString());
    }

    private static void EmitConstructor(SourceProductionContext context, ConstructorInfo info)
    {
        var ns = info.Symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : info.Symbol.ContainingNamespace.ToDisplayString();

        var prototypeTypeName = info.PrototypeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using Asynkron.JsEngine.Ast;");
        source.AppendLine("using Asynkron.JsEngine.JsTypes;");
        source.AppendLine("using Asynkron.JsEngine.Runtime;");
        source.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            source.Append("namespace ").Append(ns).AppendLine(";");
            source.AppendLine();
        }

        source.Append("public sealed partial class ").Append(info.Symbol.Name).AppendLine();
        source.AppendLine("{");
        source.AppendLine("    public static HostFunction CreateConstructor(RealmState realm)");
        source.AppendLine("    {");
        source.Append("        var prototype = ").Append(prototypeTypeName).AppendLine(".CreatePrototype(realm);");
        source.Append("        var typed = new ").Append(info.Symbol.Name).AppendLine("(prototype, realm);");
        source.AppendLine("        var constructor = new HostFunction((thisValue, args) => typed.ConstructInstance(thisValue, args), realm)");
        source.AppendLine("        {");
        source.AppendLine("            IsConstructor = true");
        source.AppendLine("        };");
        source.Append("        constructor.DefineProperty(\"length\", new PropertyDescriptor { Value = ")
            .Append(info.LengthLiteral)
            .AppendLine("d, Writable = false, Enumerable = false, Configurable = true });");
        source.Append("        constructor.DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
            .Append(info.DisplayName.Replace("\"", "\\\""))
            .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
        source.AppendLine("        constructor.DefineProperty(\"prototype\",");
        source.AppendLine("            new PropertyDescriptor");
        source.AppendLine("            {");
        source.AppendLine("                Value = prototype, Writable = false, Enumerable = false, Configurable = false");
        source.AppendLine("            });");
        source.AppendLine("        prototype.DefineProperty(\"constructor\",");
        source.AppendLine("            new PropertyDescriptor");
        source.AppendLine("            {");
        source.AppendLine("                Value = constructor, Writable = true, Enumerable = false, Configurable = true");
        source.AppendLine("            });");
        source.AppendLine("        typed.ConfigureConstructor(constructor);");
        source.AppendLine("        return constructor;");
        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource($"{info.Symbol.Name}.Constructor.g.cs", source.ToString());
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private static string? GetNamedValue(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }

        return null;
    }

    private static bool GetNamedBool(AttributeData attr, string name, bool defaultValue = false)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is bool b)
            {
                return b;
            }
        }

        return defaultValue;
    }

    private static string GetNamedDouble(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is double d)
            {
                return d.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return "0";
    }

    private static bool InheritsFrom(INamedTypeSymbol typeSymbol, string baseTypeMetadataName)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.ToDisplayString() == baseTypeMetadataName)
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private sealed record PrototypeInfo(INamedTypeSymbol Symbol, ImmutableArray<GetterInfo> Getters,
        ImmutableArray<MethodInfo> Methods, string? ToStringTag);

    private sealed record GetterInfo(IMethodSymbol MethodSymbol, string PropertyName, string DisplayName, bool Enumerable,
        bool Configurable);

    private sealed record MethodInfo(IMethodSymbol MethodSymbol, string PropertyName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable);

    private sealed record ConstructorInfo(INamedTypeSymbol Symbol, INamedTypeSymbol PrototypeType, string LengthLiteral,
        string DisplayName);
}
