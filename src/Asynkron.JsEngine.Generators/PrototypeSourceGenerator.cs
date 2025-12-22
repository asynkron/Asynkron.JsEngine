using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
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
            .FirstOrDefault(attr => string.Equals(attr.AttributeClass?.ToDisplayString(), "Asynkron.JsEngine.Runtime.Prototypes.JsPrototypeAttribute", StringComparison.Ordinal));
        if (prototypeAttr is null)
        {
            return null;
        }

        var getters = ImmutableArray.CreateBuilder<GetterInfo>();
        var setters = ImmutableArray.CreateBuilder<SetterInfo>();
        var methods = ImmutableArray.CreateBuilder<MethodInfo>();
        var symbolMethods = ImmutableArray.CreateBuilder<SymbolMethodInfo>();
        var symbolGetters = ImmutableArray.CreateBuilder<SymbolGetterInfo>();
        var symbolAliases = ImmutableArray.CreateBuilder<SymbolAliasInfo>();
        var jsValueType = context.SemanticModel.Compilation.GetTypeByMetadataName("Asynkron.JsEngine.JsTypes.JsValue");
        var readOnlyListType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");

        // Collect class-level symbol aliases
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(),
                "Asynkron.JsEngine.Runtime.Prototypes.JsSymbolAliasAttribute", StringComparison.Ordinal))
            {
                var symbolName = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                    : string.Empty;
                var targetProperty = attr.ConstructorArguments.Length > 1
                    ? attr.ConstructorArguments[1].Value as string ?? string.Empty
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(symbolName) && !string.IsNullOrWhiteSpace(targetProperty))
                {
                    var enumerable = GetNamedBool(attr, "Enumerable");
                    var writable = GetNamedBool(attr, "Writable", true);
                    var configurable = GetNamedBool(attr, "Configurable", true);
                    symbolAliases.Add(new SymbolAliasInfo(symbolName, targetProperty, enumerable, writable, configurable));
                }
            }
        }

        foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                switch (attrName)
                {
                    case "Asynkron.JsEngine.Runtime.Prototypes.JsHostGetterAttribute":
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

                        getters.Add(new GetterInfo(member, propertyName, displayName, enumerable, configurable, member.IsStatic));
                        break;
                    }
                    case "Asynkron.JsEngine.Runtime.Prototypes.JsHostSetterAttribute":
                    {
                        var propertyName = attr.ConstructorArguments.Length > 0
                            ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                            : string.Empty;
                        if (string.IsNullOrWhiteSpace(propertyName))
                        {
                            continue;
                        }

                        var displayName = GetNamedValue(attr, "DisplayName") ?? $"set {propertyName}";
                        var enumerable = GetNamedBool(attr, "Enumerable");
                        var configurable = GetNamedBool(attr, "Configurable", defaultValue: true);

                        setters.Add(new SetterInfo(member, propertyName, displayName, enumerable, configurable, member.IsStatic));
                        break;
                    }
                    case "Asynkron.JsEngine.Runtime.Prototypes.JsHostMethodAttribute":
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

                        var signature = GetHostMethodSignature(member, jsValueType, readOnlyListType);
                        methods.Add(new MethodInfo(member, propertyName, displayName, lengthLiteral, enumerable,
                            configurable, writable, signature, member.IsStatic));
                        break;
                    }
                    case "Asynkron.JsEngine.Runtime.Prototypes.JsSymbolMethodAttribute":
                    {
                        var symbolName = attr.ConstructorArguments.Length > 0
                            ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                            : string.Empty;
                        if (string.IsNullOrWhiteSpace(symbolName))
                        {
                            continue;
                        }

                        var lengthLiteral = GetNamedDouble(attr, "Length");
                        var displayName = GetNamedValue(attr, "DisplayName") ?? $"[Symbol.{symbolName}]";
                        var enumerable = GetNamedBool(attr, "Enumerable");
                        var configurable = GetNamedBool(attr, "Configurable", true);
                        var writable = GetNamedBool(attr, "Writable", true);

                        var signature = GetHostMethodSignature(member, jsValueType, readOnlyListType);
                        symbolMethods.Add(new SymbolMethodInfo(member, symbolName, displayName, lengthLiteral, enumerable,
                            configurable, writable, signature, member.IsStatic));
                        break;
                    }
                    case "Asynkron.JsEngine.Runtime.Prototypes.JsSymbolGetterAttribute":
                    {
                        var symbolName = attr.ConstructorArguments.Length > 0
                            ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                            : string.Empty;
                        if (string.IsNullOrWhiteSpace(symbolName))
                        {
                            continue;
                        }

                        var displayName = GetNamedValue(attr, "DisplayName") ?? $"get [Symbol.{symbolName}]";
                        var enumerable = GetNamedBool(attr, "Enumerable");
                        var configurable = GetNamedBool(attr, "Configurable", true);

                        symbolGetters.Add(new SymbolGetterInfo(member, symbolName, displayName, enumerable, configurable, member.IsStatic));
                        break;
                    }
                }
            }
        }

        var toStringTag = GetNamedValue(prototypeAttr, "ToStringTag");
        var objectKind = TryGetPrototypeObjectKind(prototypeAttr);
        var useArrayInstance = objectKind == PrototypeObjectKind.Array;
        var useFunctionInstance = objectKind == PrototypeObjectKind.Function;
        return new PrototypeInfo(typeSymbol, getters.ToImmutable(), setters.ToImmutable(), methods.ToImmutable(),
            symbolMethods.ToImmutable(), symbolGetters.ToImmutable(), symbolAliases.ToImmutable(),
            toStringTag, useArrayInstance, useFunctionInstance);
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
            .FirstOrDefault(attr => string.Equals(attr.AttributeClass?.ToDisplayString(), "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorAttribute", StringComparison.Ordinal));
        if (constructorAttr is null)
        {
            return null;
        }

        if (!InheritsFrom(typeSymbol, "Asynkron.JsEngine.Runtime.Prototypes.JsConstructor"))
        {
            return null;
        }

        var prototypeTypeSymbol = constructorAttr.NamedArguments
            .Where(pair => string.Equals(pair.Key, "PrototypeType", StringComparison.Ordinal))
            .Select(pair => pair.Value.Value)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

        if (prototypeTypeSymbol is null)
        {
            return null;
        }

        var lengthLiteral = GetNamedDouble(constructorAttr, "Length");
        var displayName = GetNamedValue(constructorAttr, "DisplayName") ?? typeSymbol.Name;

        // Scan for static methods with JsConstructorMethodAttribute
        var staticMethods = ImmutableArray.CreateBuilder<ConstructorMethodInfo>();
        var jsValueType = context.SemanticModel.Compilation.GetTypeByMetadataName("Asynkron.JsEngine.JsTypes.JsValue");
        var readOnlyListType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
        var realmStateType = context.SemanticModel.Compilation.GetTypeByMetadataName("Asynkron.JsEngine.Runtime.RealmState");

        foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (!member.IsStatic)
            {
                continue;
            }

            foreach (var attr in member.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (!string.Equals(attrName, "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorMethodAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                var propertyName = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                var methodLengthLiteral = GetNamedDouble(attr, "Length");
                var methodDisplayName = GetNamedValue(attr, "DisplayName") ?? propertyName;
                var enumerable = GetNamedBool(attr, "Enumerable");
                var configurable = GetNamedBool(attr, "Configurable", true);
                var writable = GetNamedBool(attr, "Writable", true);

                var signature = GetConstructorMethodSignature(member, jsValueType, readOnlyListType, realmStateType);
                var returnsJsValue = jsValueType is not null && IsJsValue(member.ReturnType, jsValueType);
                staticMethods.Add(new ConstructorMethodInfo(member, propertyName, methodDisplayName, methodLengthLiteral,
                    enumerable, configurable, writable, signature, returnsJsValue));
            }
        }

        return new ConstructorInfo(typeSymbol, prototypeTypeSymbol, lengthLiteral, displayName, staticMethods.ToImmutable());
    }

    private static ConstructorMethodSignature GetConstructorMethodSignature(
        IMethodSymbol method,
        INamedTypeSymbol? jsValueType,
        INamedTypeSymbol? readOnlyListType,
        INamedTypeSymbol? realmStateType)
    {
        var parameters = method.Parameters;

        if (parameters.Length == 0)
        {
            return ConstructorMethodSignature.NoArgs;
        }

        if (parameters.Length == 1 &&
            readOnlyListType is not null &&
            jsValueType is not null &&
            IsReadOnlyListOfJsValue(parameters[0].Type, readOnlyListType, jsValueType))
        {
            return ConstructorMethodSignature.ArgsOnly;
        }

        if (parameters.Length == 2 &&
            readOnlyListType is not null &&
            jsValueType is not null &&
            realmStateType is not null &&
            IsReadOnlyListOfJsValue(parameters[0].Type, readOnlyListType, jsValueType) &&
            IsNullableRealmState(parameters[1].Type, realmStateType))
        {
            return ConstructorMethodSignature.ArgsRealm;
        }

        // Default: (object?, IReadOnlyList<JsValue>, RealmState?)
        return ConstructorMethodSignature.ThisArgsRealm;
    }

    private static bool IsNullableRealmState(ITypeSymbol type, INamedTypeSymbol realmStateType)
    {
        // Handle both RealmState and RealmState?
        if (SymbolEqualityComparer.Default.Equals(type, realmStateType))
        {
            return true;
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated &&
            type is INamedTypeSymbol namedType &&
            SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, realmStateType))
        {
            return true;
        }

        return false;
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
        source.AppendLine("using Asynkron.JsEngine.Runtime.Prototypes;");
        source.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            source.Append("namespace ").Append(ns).AppendLine(";");
            source.AppendLine();
        }

        source.Append("public sealed partial class ").Append(info.Symbol.Name).AppendLine(" : JsPrototype");
        source.AppendLine("{");
        source.Append("    public ").Append(info.Symbol.Name).AppendLine("(IJsObjectLike prototype, RealmState realm) : base(prototype, realm)");
        source.AppendLine("    {");
        source.AppendLine("        InitializePrototype();");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    partial void InitializePrototype();");
        source.AppendLine();
        source.AppendLine("    public static IJsObjectLike CreatePrototype(RealmState realm)");
        source.AppendLine("    {");
        var prototypeExpr = info.UseArrayInstance
            ? "new JsArray(realm)"
            : info.UseFunctionInstance
                ? "new HostFunction((_, _) => JsValue.Undefined, realm, isConstructor: false)"
                : "new JsObject()";
        source.Append("        var prototype = ").Append(prototypeExpr).AppendLine(";");
        source.Append("        var typed = new ").Append(info.Symbol.Name)
            .AppendLine("(prototype, realm);");

        // Group getters and setters by property name to emit combined accessor properties
        var gettersByProp = info.Getters.ToDictionary(g => g.PropertyName);
        var settersByProp = info.Setters.ToDictionary(s => s.PropertyName);
        var allAccessorProps = gettersByProp.Keys.Union(settersByProp.Keys).ToList();

        foreach (var propName in allAccessorProps)
        {
            var hasGetter = gettersByProp.TryGetValue(propName, out var getter);
            var hasSetter = settersByProp.TryGetValue(propName, out var setter);
            var sanitizedProp = Sanitize(propName);
            var getterVar = $"getter_{sanitizedProp}";
            var setterVar = $"setter_{sanitizedProp}";

            // Emit getter function if exists
            if (hasGetter)
            {
                var getterTarget = getter!.IsStatic ? info.Symbol.Name : "typed";
                source.Append("        var ").Append(getterVar)
                    .Append(" = new HostFunction((thisValue, _) => ").Append(getterTarget).Append(".")
                    .Append(getter.MethodSymbol.Name)
                    .AppendLine("(thisValue), realm, isConstructor: false);");
                source.Append("        ").Append(getterVar)
                    .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                    .Append(getter.DisplayName.Replace("\"", "\\\""))
                    .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            }

            // Emit setter function if exists
            if (hasSetter)
            {
                var setterTarget = setter!.IsStatic ? info.Symbol.Name : "typed";
                source.Append("        var ").Append(setterVar)
                    .Append(" = new HostFunction((thisValue, args) => ").Append(setterTarget).Append(".")
                    .Append(setter.MethodSymbol.Name)
                    .AppendLine("(thisValue, args), realm, isConstructor: false);");
                source.Append("        ").Append(setterVar)
                    .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                    .Append(setter.DisplayName.Replace("\"", "\\\""))
                    .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            }

            // Determine enumerable/configurable from getter if present, else from setter
            var enumerable = hasGetter ? getter!.Enumerable : setter!.Enumerable;
            var configurable = hasGetter ? getter!.Configurable : setter!.Configurable;

            // Emit combined property descriptor
            source.Append("        prototype.DefineProperty(\"").Append(propName).Append("\", new PropertyDescriptor { ");
            if (hasGetter)
            {
                source.Append("Get = ").Append(getterVar);
                if (hasSetter)
                {
                    source.Append(", Set = ").Append(setterVar);
                }
            }
            else if (hasSetter)
            {
                source.Append("Set = ").Append(setterVar);
            }
            source.Append(", Enumerable = ").Append(enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(configurable ? "true" : "false")
                .AppendLine(" });");
        }

        foreach (var method in info.Methods)
        {
            var methodVar = $"method_{Sanitize(method.PropertyName)}";
            source.Append("        var ").Append(methodVar).Append(" = new HostFunction(");

            // Determine the target: static uses ClassName, instance uses typed
            var target = method.IsStatic
                ? info.Symbol.Name
                : "typed";

            switch (method.Signature)
            {
                case HostMethodSignature.NoArgs:
                    source.Append("(_, _) => ").Append(target).Append(".").Append(method.MethodSymbol.Name)
                        .AppendLine("(), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ArgsOnly:
                    source.Append("args => ").Append(target).Append(".").Append(method.MethodSymbol.Name)
                        .AppendLine("(args), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ThisOnly:
                    source.Append("(thisValue, _) => ").Append(target).Append(".").Append(method.MethodSymbol.Name)
                        .AppendLine("(thisValue), realm, isConstructor: false);");
                    break;
                default:
                    source.Append("(thisValue, args) => ").Append(target).Append(".").Append(method.MethodSymbol.Name)
                        .AppendLine("(thisValue, args), realm, isConstructor: false);");
                    break;
            }
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
                .AppendLine(" });");
        }

        // Emit symbol-keyed methods
        foreach (var symMethod in info.SymbolMethods)
        {
            var methodVar = $"symbolMethod_{Sanitize(symMethod.SymbolName)}";
            source.Append("        var ").Append(methodVar).Append(" = new HostFunction(");

            var target = symMethod.IsStatic ? info.Symbol.Name : "typed";

            switch (symMethod.Signature)
            {
                case HostMethodSignature.NoArgs:
                    source.Append("(_, _) => ").Append(target).Append(".").Append(symMethod.MethodSymbol.Name)
                        .AppendLine("(), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ArgsOnly:
                    source.Append("args => ").Append(target).Append(".").Append(symMethod.MethodSymbol.Name)
                        .AppendLine("(args), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ThisOnly:
                    source.Append("(thisValue, _) => ").Append(target).Append(".").Append(symMethod.MethodSymbol.Name)
                        .AppendLine("(thisValue), realm, isConstructor: false);");
                    break;
                default:
                    source.Append("(thisValue, args) => ").Append(target).Append(".").Append(symMethod.MethodSymbol.Name)
                        .AppendLine("(thisValue, args), realm, isConstructor: false);");
                    break;
            }
            source.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(symMethod.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            source.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(symMethod.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });").AppendLine();
            source.Append("        prototype.DefineProperty($\"@@symbol:{TypedAstSymbol.For(\"Symbol.")
                .Append(symMethod.SymbolName).Append("\").GetHashCode()}\", new PropertyDescriptor { Value = ").Append(methodVar)
                .Append(", Writable = ").Append(symMethod.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(symMethod.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(symMethod.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

        // Emit symbol-keyed getters
        foreach (var symGetter in info.SymbolGetters)
        {
            var getterVar = $"symbolGetter_{Sanitize(symGetter.SymbolName)}";
            var target = symGetter.IsStatic ? info.Symbol.Name : "typed";
            source.Append("        var ").Append(getterVar)
                .Append(" = new HostFunction((thisValue, _) => ").Append(target).Append(".")
                .Append(symGetter.MethodSymbol.Name)
                .AppendLine("(thisValue), realm, isConstructor: false);");
            source.Append("        ").Append(getterVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(symGetter.DisplayName.Replace("\"", "\\\""))
                .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            source.Append("        prototype.DefineProperty($\"@@symbol:{TypedAstSymbol.For(\"Symbol.")
                .Append(symGetter.SymbolName).Append("\").GetHashCode()}\", new PropertyDescriptor { Get = ").Append(getterVar)
                .Append(", Enumerable = ").Append(symGetter.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(symGetter.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

        // Emit symbol aliases (e.g., [Symbol.iterator] -> values)
        foreach (var alias in info.SymbolAliases)
        {
            source.Append("        if (prototype.TryGetProperty(\"").Append(alias.TargetPropertyName).AppendLine("\", out var aliasTarget))");
            source.AppendLine("        {");
            source.Append("            prototype.DefineProperty($\"@@symbol:{TypedAstSymbol.For(\"Symbol.")
                .Append(alias.SymbolName).AppendLine("\").GetHashCode()}\",");
            source.AppendLine("                new PropertyDescriptor");
            source.AppendLine("                {");
            source.Append("                    Value = aliasTarget, Writable = ").Append(alias.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(alias.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(alias.Configurable ? "true" : "false")
                .AppendLine();
            source.AppendLine("                });");
            source.AppendLine("        }");
        }

        if (!string.IsNullOrEmpty(info.ToStringTag))
        {
            source.AppendLine("        prototype.DefineProperty($\"@@symbol:{TypedAstSymbol.For(\"Symbol.toStringTag\").GetHashCode()}\",");
            source.AppendLine("            new PropertyDescriptor");
            source.AppendLine("            {");
            source.Append("                Value = \"").Append(info.ToStringTag?.Replace("\"", "\\\""))
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

        // Generate static method registrations
        foreach (var method in info.StaticMethods)
        {
            var methodVar = $"method_{Sanitize(method.PropertyName)}";
            source.Append("        var ").Append(methodVar).Append(" = new HostFunction(");

            // If method returns JsValue directly, no wrapping needed
            var wrapOpen = method.ReturnsJsValue ? "" : "JsValue.FromObjectUnsafe(";
            var wrapClose = method.ReturnsJsValue ? "" : ")";

            switch (method.Signature)
            {
                case ConstructorMethodSignature.NoArgs:
                    source.Append("(_, _) => ").Append(wrapOpen).Append(info.Symbol.Name).Append(".")
                        .Append(method.MethodSymbol.Name).Append("()").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case ConstructorMethodSignature.ArgsOnly:
                    source.Append("args => ").Append(wrapOpen).Append(info.Symbol.Name).Append(".")
                        .Append(method.MethodSymbol.Name).Append("(args)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case ConstructorMethodSignature.ArgsRealm:
                    source.Append("args => ").Append(wrapOpen).Append(info.Symbol.Name).Append(".")
                        .Append(method.MethodSymbol.Name).Append("(args, realm)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                default: // ThisArgsRealm
                    source.Append("(thisValue, args) => ").Append(wrapOpen).Append(info.Symbol.Name).Append(".")
                        .Append(method.MethodSymbol.Name).Append("(thisValue, args, realm)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
            }

            source.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(method.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            source.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(method.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });").AppendLine();
            source.Append("        constructor.DefineProperty(\"").Append(method.PropertyName)
                .Append("\", new PropertyDescriptor { Value = ").Append(methodVar)
                .Append(", Writable = ").Append(method.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(method.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(method.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

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
            if (string.Equals(arg.Key, name, StringComparison.Ordinal) && arg.Value.Value is string s && !string.IsNullOrEmpty(s))
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
            if (string.Equals(arg.Key, name, StringComparison.Ordinal) && arg.Value.Value is bool b)
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
            if (string.Equals(arg.Key, name, StringComparison.Ordinal) && arg.Value.Value is double d)
            {
                return d.ToString("0.############", CultureInfo.InvariantCulture);
            }
        }

        return "0";
    }

    private static bool InheritsFrom(INamedTypeSymbol typeSymbol, string baseTypeMetadataName)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (string.Equals(current.ToDisplayString(), baseTypeMetadataName, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private sealed record PrototypeInfo(
        INamedTypeSymbol Symbol,
        ImmutableArray<GetterInfo> Getters,
        ImmutableArray<SetterInfo> Setters,
        ImmutableArray<MethodInfo> Methods,
        ImmutableArray<SymbolMethodInfo> SymbolMethods,
        ImmutableArray<SymbolGetterInfo> SymbolGetters,
        ImmutableArray<SymbolAliasInfo> SymbolAliases,
        string? ToStringTag,
        bool UseArrayInstance,
        bool UseFunctionInstance);

    private sealed record GetterInfo(IMethodSymbol MethodSymbol, string PropertyName, string DisplayName, bool Enumerable,
        bool Configurable, bool IsStatic);

    private sealed record SetterInfo(IMethodSymbol MethodSymbol, string PropertyName, string DisplayName, bool Enumerable,
        bool Configurable, bool IsStatic);

    private sealed record MethodInfo(IMethodSymbol MethodSymbol, string PropertyName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, HostMethodSignature Signature, bool IsStatic);

    private sealed record SymbolMethodInfo(IMethodSymbol MethodSymbol, string SymbolName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, HostMethodSignature Signature, bool IsStatic);

    private sealed record SymbolGetterInfo(IMethodSymbol MethodSymbol, string SymbolName, string DisplayName,
        bool Enumerable, bool Configurable, bool IsStatic);

    private sealed record SymbolAliasInfo(string SymbolName, string TargetPropertyName,
        bool Enumerable, bool Writable, bool Configurable);

    private sealed record ConstructorInfo(INamedTypeSymbol Symbol, INamedTypeSymbol PrototypeType, string LengthLiteral,
        string DisplayName, ImmutableArray<ConstructorMethodInfo> StaticMethods);

    private sealed record ConstructorMethodInfo(IMethodSymbol MethodSymbol, string PropertyName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, ConstructorMethodSignature Signature, bool ReturnsJsValue);

    private enum ConstructorMethodSignature
    {
        ThisArgsRealm = 0,  // (object?, IReadOnlyList<JsValue>, RealmState?)
        ArgsRealm = 1,      // (IReadOnlyList<JsValue>, RealmState?)
        ArgsOnly = 2,       // (IReadOnlyList<JsValue>)
        NoArgs = 3          // ()
    }

    private enum HostMethodSignature
    {
        ThisAndArgs = 0,
        ThisOnly = 1,
        ArgsOnly = 2,
        NoArgs = 3
    }

    private enum PrototypeObjectKind
    {
        Object = 0,
        Array = 1,
        Function = 2
    }

    private static PrototypeObjectKind TryGetPrototypeObjectKind(AttributeData attr)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (string.Equals(arg.Key, "ObjectKind", StringComparison.Ordinal))
            {
                var value = arg.Value.Value;
                if (value is int intValue)
                {
                    return (PrototypeObjectKind)intValue;
                }

                if (value is IConvertible convertible)
                {
                    return (PrototypeObjectKind)convertible.ToInt32(CultureInfo.InvariantCulture);
                }
            }
        }

        return PrototypeObjectKind.Object;
    }

    private static HostMethodSignature GetHostMethodSignature(
        IMethodSymbol method,
        INamedTypeSymbol? jsValueType,
        INamedTypeSymbol? readOnlyListType)
    {
        if (jsValueType is null || readOnlyListType is null)
        {
            return HostMethodSignature.ThisAndArgs;
        }

        var parameters = method.Parameters;

        if (parameters.Length == 0)
        {
            return HostMethodSignature.NoArgs;
        }

        if (parameters.Length == 2 &&
            IsJsValue(parameters[0].Type, jsValueType) &&
            IsReadOnlyListOfJsValue(parameters[1].Type, readOnlyListType, jsValueType))
        {
            return HostMethodSignature.ThisAndArgs;
        }

        if (parameters.Length == 1)
        {
            if (IsJsValue(parameters[0].Type, jsValueType))
            {
                return HostMethodSignature.ThisOnly;
            }

            if (IsReadOnlyListOfJsValue(parameters[0].Type, readOnlyListType, jsValueType))
            {
                return HostMethodSignature.ArgsOnly;
            }
        }

        return HostMethodSignature.ThisAndArgs;
    }

    private static bool IsJsValue(ITypeSymbol type, INamedTypeSymbol jsValueType)
        => SymbolEqualityComparer.Default.Equals(type, jsValueType);

    private static bool IsReadOnlyListOfJsValue(
        ITypeSymbol type,
        INamedTypeSymbol readOnlyListType,
        INamedTypeSymbol jsValueType)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readOnlyListType))
        {
            return false;
        }

        return namedType.TypeArguments.Length == 1 &&
               SymbolEqualityComparer.Default.Equals(namedType.TypeArguments[0], jsValueType);
    }
}
