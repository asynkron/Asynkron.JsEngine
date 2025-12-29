using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asynkron.JsEngine.Generators;

[Generator]
public sealed class PrototypeSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var wellKnownTypes = context.CompilationProvider.Select(static (compilation, _) => WellKnownTypes.From(compilation));

        var prototypeCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Asynkron.JsEngine.Runtime.Prototypes.JsPrototypeAttribute",
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (ctx, _) => new PrototypeTarget((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0]));

        var constructorCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorAttribute",
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (ctx, _) => new ConstructorTarget((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0]));

        var prototypes = prototypeCandidates
            .Combine(wellKnownTypes)
            .Select(static (data, _) => TransformPrototype(data.Left, data.Right))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .WithComparer(PrototypeCacheKeyComparer.Instance);

        var constructors = constructorCandidates
            .Combine(wellKnownTypes)
            .Select(static (data, _) => TransformConstructor(data.Left, data.Right))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .WithComparer(ConstructorCacheKeyComparer.Instance);

        var orderedPrototypes = prototypes
            .Collect()
            .SelectMany(static (items, _) => items.OrderBy(static p => p.CacheKey, StringComparer.Ordinal));

        var orderedConstructors = constructors
            .Collect()
            .SelectMany(static (items, _) => items.OrderBy(static c => c.CacheKey, StringComparer.Ordinal));

        context.RegisterSourceOutput(orderedPrototypes, static (spc, info) => Emit(spc, info));
        context.RegisterSourceOutput(orderedConstructors, static (spc, info) => EmitConstructor(spc, info));
    }

    private static PrototypeInfo? TransformPrototype(PrototypeTarget target, WellKnownTypes wellKnown)
    {
        var typeSymbol = target.TypeSymbol;
        var prototypeAttr = target.Attribute;
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
        var methodAliases = ImmutableArray.CreateBuilder<MethodAliasInfo>();
        var jsValueType = wellKnown.JsValueType;
        var readOnlyListType = wellKnown.ReadOnlyListType;

        // Collect class-level symbol aliases and method aliases
        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrDisplayString = attr.AttributeClass?.ToDisplayString();
            if (string.Equals(attrDisplayString,
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
            else if (string.Equals(attrDisplayString,
                "Asynkron.JsEngine.Runtime.Prototypes.JsMethodAliasAttribute", StringComparison.Ordinal))
            {
                var aliasName = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                    : string.Empty;
                var targetProperty = attr.ConstructorArguments.Length > 1
                    ? attr.ConstructorArguments[1].Value as string ?? string.Empty
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(aliasName) && !string.IsNullOrWhiteSpace(targetProperty))
                {
                    var enumerable = GetNamedBool(attr, "Enumerable");
                    var writable = GetNamedBool(attr, "Writable", true);
                    var configurable = GetNamedBool(attr, "Configurable", true);
                    methodAliases.Add(new MethodAliasInfo(aliasName, targetProperty, enumerable, writable, configurable));
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

                        getters.Add(new GetterInfo(member.Name, propertyName, displayName, enumerable, configurable, member.IsStatic));
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

                        setters.Add(new SetterInfo(member.Name, propertyName, displayName, enumerable, configurable, member.IsStatic));
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
                        methods.Add(new MethodInfo(member.Name, propertyName, displayName, lengthLiteral, enumerable,
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
                        symbolMethods.Add(new SymbolMethodInfo(member.Name, symbolName, displayName, lengthLiteral, enumerable,
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

                        symbolGetters.Add(new SymbolGetterInfo(member.Name, symbolName, displayName, enumerable, configurable, member.IsStatic));
                        break;
                    }
                }
            }
        }

        var toStringTag = GetNamedValue(prototypeAttr, "ToStringTag");
        var objectKind = TryGetPrototypeObjectKind(prototypeAttr);
        var useArrayInstance = objectKind == PrototypeObjectKind.Array;
        var useFunctionInstance = objectKind == PrototypeObjectKind.Function;
        var instanceTypeSymbol = GetNamedTypeValue(prototypeAttr, "InstanceType");
        var tryGetMethod = GetNamedValue(prototypeAttr, "TryGetMethod");

        // Extract string data from symbols for caching
        var className = typeSymbol.Name;
        var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();

        string? instanceTypeName = null;
        string? instanceTypeSimpleName = null;
        string? intrinsicName = null;
        if (instanceTypeSymbol is not null)
        {
            instanceTypeName = instanceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            instanceTypeSimpleName = instanceTypeSymbol.Name;
            intrinsicName = GetNamedValue(prototypeAttr, "IntrinsicName") ?? instanceTypeSimpleName;
        }

        var orderedGetters = OrderGetters(getters.ToImmutable());
        var orderedSetters = OrderSetters(setters.ToImmutable());
        var orderedMethods = OrderMethods(methods.ToImmutable());
        var orderedSymbolMethods = OrderSymbolMethods(symbolMethods.ToImmutable());
        var orderedSymbolGetters = OrderSymbolGetters(symbolGetters.ToImmutable());
        var orderedSymbolAliases = OrderSymbolAliases(symbolAliases.ToImmutable());
        var orderedMethodAliases = OrderMethodAliases(methodAliases.ToImmutable());

        var cacheKey = BuildPrototypeCacheKey(className, namespaceName, orderedGetters, orderedSetters, orderedMethods,
            orderedSymbolMethods, orderedSymbolGetters, orderedSymbolAliases, orderedMethodAliases, toStringTag,
            useArrayInstance, useFunctionInstance, instanceTypeName, intrinsicName, tryGetMethod);

        return new PrototypeInfo(
            className,
            namespaceName,
            orderedGetters,
            orderedSetters,
            orderedMethods,
            orderedSymbolMethods,
            orderedSymbolGetters,
            orderedSymbolAliases,
            orderedMethodAliases,
            toStringTag,
            useArrayInstance,
            useFunctionInstance,
            instanceTypeName,
            instanceTypeSimpleName,
            intrinsicName,
            tryGetMethod,
            cacheKey);
    }

    private static ConstructorInfo? TransformConstructor(ConstructorTarget target, WellKnownTypes wellKnown)
    {
        var typeSymbol = target.TypeSymbol;
        var constructorAttr = target.Attribute;
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
        var jsValueType = wellKnown.JsValueType;
        var readOnlyListType = wellKnown.ReadOnlyListType;
        var realmStateType = wellKnown.RealmStateType;

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
                staticMethods.Add(new ConstructorMethodInfo(member.Name, propertyName, methodDisplayName, methodLengthLiteral,
                    enumerable, configurable, writable, signature, returnsJsValue));
            }
        }

        // Extract string data from symbols for caching
        var className = typeSymbol.Name;
        var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();
        var prototypeTypeName = prototypeTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var orderedStaticMethods = OrderConstructorMethods(staticMethods.ToImmutable());
        var cacheKey = BuildConstructorCacheKey(className, namespaceName, prototypeTypeName, lengthLiteral, displayName, orderedStaticMethods);

        return new ConstructorInfo(className, namespaceName, prototypeTypeName, lengthLiteral, displayName, orderedStaticMethods, cacheKey);
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
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using Asynkron.JsEngine.Ast;");
        source.AppendLine("using Asynkron.JsEngine.JsTypes;");
        source.AppendLine("using Asynkron.JsEngine.Runtime;");
        source.AppendLine("using Asynkron.JsEngine.Runtime.Prototypes;");
        source.AppendLine("using Asynkron.JsEngine.StdLib;");
        source.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            source.Append("namespace ").Append(info.Namespace).AppendLine(";");
            source.AppendLine();
        }

        source.Append("public sealed partial class ").Append(info.ClassName).AppendLine(" : JsPrototype");
        source.AppendLine("{");
        source.Append("    public ").Append(info.ClassName).AppendLine("(IJsObjectLike prototype, RealmState realm) : base(prototype, realm)");
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
        source.Append("        var typed = new ").Append(info.ClassName)
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
                var getterTarget = getter!.IsStatic ? info.ClassName : "typed";
                source.Append("        var ").Append(getterVar)
                    .Append(" = new HostFunction((thisValue, _) => ").Append(getterTarget).Append(".")
                    .Append(getter.MethodName)
                    .AppendLine("(thisValue), realm, isConstructor: false);");
                source.Append("        ").Append(getterVar)
                    .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                    .Append(getter.DisplayName.Replace("\"", "\\\""))
                    .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            }

            // Emit setter function if exists
            if (hasSetter)
            {
                var setterTarget = setter!.IsStatic ? info.ClassName : "typed";
                source.Append("        var ").Append(setterVar)
                    .Append(" = new HostFunction((thisValue, args) => ").Append(setterTarget).Append(".")
                    .Append(setter.MethodName)
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
                ? info.ClassName
                : "typed";

            switch (method.Signature)
            {
                case HostMethodSignature.NoArgs:
                    source.Append("(_, _) => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ArgsOnly:
                    source.Append("args => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(args), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ThisOnly:
                    source.Append("(thisValue, _) => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(thisValue), realm, isConstructor: false);");
                    break;
                default:
                    source.Append("(thisValue, args) => ").Append(target).Append(".").Append(method.MethodName)
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

            var target = symMethod.IsStatic ? info.ClassName : "typed";

            switch (symMethod.Signature)
            {
                case HostMethodSignature.NoArgs:
                    source.Append("(_, _) => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ArgsOnly:
                    source.Append("args => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(args), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ThisOnly:
                    source.Append("(thisValue, _) => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(thisValue), realm, isConstructor: false);");
                    break;
                default:
                    source.Append("(thisValue, args) => ").Append(target).Append(".").Append(symMethod.MethodName)
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
            var target = symGetter.IsStatic ? info.ClassName : "typed";
            source.Append("        var ").Append(getterVar)
                .Append(" = new HostFunction((thisValue, _) => ").Append(target).Append(".")
                .Append(symGetter.MethodName)
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

        // Emit method aliases (e.g., toGMTString -> toUTCString)
        var methodAliasVarIndex = 0;
        foreach (var alias in info.MethodAliases)
        {
            var varName = $"methodAliasTarget{methodAliasVarIndex++}";
            source.Append("        if (prototype.TryGetProperty(\"").Append(alias.TargetPropertyName).Append("\", out var ").Append(varName).AppendLine("))");
            source.AppendLine("        {");
            source.Append("            prototype.DefineProperty(\"").Append(alias.AliasName).AppendLine("\",");
            source.AppendLine("                new PropertyDescriptor");
            source.AppendLine("                {");
            source.Append("                    Value = ").Append(varName).Append(", Writable = ").Append(alias.Writable ? "true" : "false")
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

        // Generate RequireInstance method if InstanceType is specified
        if (info.InstanceTypeName is not null)
        {
            source.AppendLine();
            source.Append("    private ").Append(info.InstanceTypeName).AppendLine(" RequireInstance(JsValue receiver)");
            source.AppendLine("    {");

            if (!string.IsNullOrEmpty(info.TryGetMethod))
            {
                // Use custom TryGet method (e.g., JsPromise.TryGetInternalPromise)
                source.Append("        if (").Append(info.InstanceTypeName).Append(".").Append(info.TryGetMethod)
                    .AppendLine("(receiver, out var instance) && instance is not null)");
            }
            else
            {
                // Use standard TryGetObject<T>
                source.Append("        if (receiver.TryGetObject<").Append(info.InstanceTypeName).AppendLine(">(out var instance))");
            }

            source.AppendLine("        {");
            source.AppendLine("            return instance;");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        throw StandardLibrary.ThrowTypeError(\"").Append(info.IntrinsicName)
                .AppendLine(" method called on incompatible receiver\", realm: Realm);");
            source.AppendLine("    }");
        }

        source.AppendLine("}");

        context.AddSource($"{info.ClassName}.Prototype.g.cs", source.ToString());
    }

    private static void EmitConstructor(SourceProductionContext context, ConstructorInfo info)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using Asynkron.JsEngine.Ast;");
        source.AppendLine("using Asynkron.JsEngine.JsTypes;");
        source.AppendLine("using Asynkron.JsEngine.Runtime;");
        source.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            source.Append("namespace ").Append(info.Namespace).AppendLine(";");
            source.AppendLine();
        }

        source.Append("public sealed partial class ").Append(info.ClassName).AppendLine();
        source.AppendLine("{");
        source.AppendLine("    public static HostFunction CreateConstructor(RealmState realm)");
        source.AppendLine("    {");
        source.Append("        var prototype = ").Append(info.PrototypeTypeName).AppendLine(".CreatePrototype(realm);");
        source.Append("        var typed = new ").Append(info.ClassName).AppendLine("(prototype, realm);");
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
                    source.Append("(_, _) => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("()").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case ConstructorMethodSignature.ArgsOnly:
                    source.Append("args => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("(args)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case ConstructorMethodSignature.ArgsRealm:
                    source.Append("args => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("(args, realm)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                default: // ThisArgsRealm
                    source.Append("(thisValue, args) => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("(thisValue, args, realm)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
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

        context.AddSource($"{info.ClassName}.Constructor.g.cs", source.ToString());
    }

    private static ImmutableArray<GetterInfo> OrderGetters(ImmutableArray<GetterInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static g => g.PropertyName, StringComparer.Ordinal)
                .ThenBy(static g => g.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<SetterInfo> OrderSetters(ImmutableArray<SetterInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static s => s.PropertyName, StringComparer.Ordinal)
                .ThenBy(static s => s.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<MethodInfo> OrderMethods(ImmutableArray<MethodInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static m => m.PropertyName, StringComparer.Ordinal)
                .ThenBy(static m => m.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<SymbolMethodInfo> OrderSymbolMethods(ImmutableArray<SymbolMethodInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static m => m.SymbolName, StringComparer.Ordinal)
                .ThenBy(static m => m.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<SymbolGetterInfo> OrderSymbolGetters(ImmutableArray<SymbolGetterInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static g => g.SymbolName, StringComparer.Ordinal)
                .ThenBy(static g => g.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<SymbolAliasInfo> OrderSymbolAliases(ImmutableArray<SymbolAliasInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static s => s.SymbolName, StringComparer.Ordinal)
                .ThenBy(static s => s.TargetPropertyName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<MethodAliasInfo> OrderMethodAliases(ImmutableArray<MethodAliasInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static m => m.AliasName, StringComparer.Ordinal)
                .ThenBy(static m => m.TargetPropertyName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<ConstructorMethodInfo> OrderConstructorMethods(ImmutableArray<ConstructorMethodInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static m => m.PropertyName, StringComparer.Ordinal)
                .ThenBy(static m => m.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static string BuildPrototypeCacheKey(
        string className,
        string? namespaceName,
        ImmutableArray<GetterInfo> getters,
        ImmutableArray<SetterInfo> setters,
        ImmutableArray<MethodInfo> methods,
        ImmutableArray<SymbolMethodInfo> symbolMethods,
        ImmutableArray<SymbolGetterInfo> symbolGetters,
        ImmutableArray<SymbolAliasInfo> symbolAliases,
        ImmutableArray<MethodAliasInfo> methodAliases,
        string? toStringTag,
        bool useArrayInstance,
        bool useFunctionInstance,
        string? instanceTypeName,
        string? intrinsicName,
        string? tryGetMethod)
    {
        var builder = new StringBuilder();
        AppendWithLength(builder, namespaceName ?? string.Empty);
        AppendWithLength(builder, className);
        AppendBool(builder, useArrayInstance);
        AppendBool(builder, useFunctionInstance);
        AppendWithLength(builder, instanceTypeName ?? string.Empty);
        AppendWithLength(builder, intrinsicName ?? string.Empty);
        AppendWithLength(builder, tryGetMethod ?? string.Empty);
        AppendWithLength(builder, toStringTag ?? string.Empty);

        AppendWithLength(builder, getters.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var getter in getters)
        {
            AppendWithLength(builder, getter.PropertyName);
            AppendWithLength(builder, getter.MethodName);
            AppendWithLength(builder, getter.DisplayName);
            AppendBool(builder, getter.Enumerable);
            AppendBool(builder, getter.Configurable);
            AppendBool(builder, getter.IsStatic);
        }

        AppendWithLength(builder, setters.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var setter in setters)
        {
            AppendWithLength(builder, setter.PropertyName);
            AppendWithLength(builder, setter.MethodName);
            AppendWithLength(builder, setter.DisplayName);
            AppendBool(builder, setter.Enumerable);
            AppendBool(builder, setter.Configurable);
            AppendBool(builder, setter.IsStatic);
        }

        AppendWithLength(builder, methods.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var method in methods)
        {
            AppendWithLength(builder, method.PropertyName);
            AppendWithLength(builder, method.MethodName);
            AppendWithLength(builder, method.DisplayName);
            AppendWithLength(builder, method.LengthLiteral);
            AppendBool(builder, method.Enumerable);
            AppendBool(builder, method.Configurable);
            AppendBool(builder, method.Writable);
            AppendWithLength(builder, ((int)method.Signature).ToString(CultureInfo.InvariantCulture));
            AppendBool(builder, method.IsStatic);
        }

        AppendWithLength(builder, symbolMethods.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var method in symbolMethods)
        {
            AppendWithLength(builder, method.SymbolName);
            AppendWithLength(builder, method.MethodName);
            AppendWithLength(builder, method.DisplayName);
            AppendWithLength(builder, method.LengthLiteral);
            AppendBool(builder, method.Enumerable);
            AppendBool(builder, method.Configurable);
            AppendBool(builder, method.Writable);
            AppendWithLength(builder, ((int)method.Signature).ToString(CultureInfo.InvariantCulture));
            AppendBool(builder, method.IsStatic);
        }

        AppendWithLength(builder, symbolGetters.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var getter in symbolGetters)
        {
            AppendWithLength(builder, getter.SymbolName);
            AppendWithLength(builder, getter.MethodName);
            AppendWithLength(builder, getter.DisplayName);
            AppendBool(builder, getter.Enumerable);
            AppendBool(builder, getter.Configurable);
            AppendBool(builder, getter.IsStatic);
        }

        AppendWithLength(builder, symbolAliases.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var alias in symbolAliases)
        {
            AppendWithLength(builder, alias.SymbolName);
            AppendWithLength(builder, alias.TargetPropertyName);
            AppendBool(builder, alias.Enumerable);
            AppendBool(builder, alias.Writable);
            AppendBool(builder, alias.Configurable);
        }

        AppendWithLength(builder, methodAliases.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var alias in methodAliases)
        {
            AppendWithLength(builder, alias.AliasName);
            AppendWithLength(builder, alias.TargetPropertyName);
            AppendBool(builder, alias.Enumerable);
            AppendBool(builder, alias.Writable);
            AppendBool(builder, alias.Configurable);
        }

        return builder.ToString();
    }

    private static string BuildConstructorCacheKey(
        string className,
        string? namespaceName,
        string prototypeTypeName,
        string lengthLiteral,
        string displayName,
        ImmutableArray<ConstructorMethodInfo> staticMethods)
    {
        var builder = new StringBuilder();
        AppendWithLength(builder, namespaceName ?? string.Empty);
        AppendWithLength(builder, className);
        AppendWithLength(builder, prototypeTypeName);
        AppendWithLength(builder, lengthLiteral);
        AppendWithLength(builder, displayName);
        AppendWithLength(builder, staticMethods.Length.ToString(CultureInfo.InvariantCulture));

        foreach (var method in staticMethods)
        {
            AppendWithLength(builder, method.PropertyName);
            AppendWithLength(builder, method.MethodName);
            AppendWithLength(builder, method.DisplayName);
            AppendWithLength(builder, method.LengthLiteral);
            AppendBool(builder, method.Enumerable);
            AppendBool(builder, method.Configurable);
            AppendBool(builder, method.Writable);
            AppendWithLength(builder, ((int)method.Signature).ToString(CultureInfo.InvariantCulture));
            AppendBool(builder, method.ReturnsJsValue);
        }

        return builder.ToString();
    }

    private static void AppendWithLength(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static void AppendBool(StringBuilder builder, bool value)
    {
        builder.Append(value ? '1' : '0').Append(';');
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

    private static INamedTypeSymbol? GetNamedTypeValue(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (string.Equals(arg.Key, name, StringComparison.Ordinal) && arg.Value.Value is INamedTypeSymbol typeSymbol)
            {
                return typeSymbol;
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

    private sealed record PrototypeTarget(INamedTypeSymbol TypeSymbol, AttributeData Attribute);

    private sealed record ConstructorTarget(INamedTypeSymbol TypeSymbol, AttributeData Attribute);

    // All info records now use only primitive/string types for proper incremental caching
    private sealed record PrototypeInfo(
        string ClassName,
        string? Namespace,
        ImmutableArray<GetterInfo> Getters,
        ImmutableArray<SetterInfo> Setters,
        ImmutableArray<MethodInfo> Methods,
        ImmutableArray<SymbolMethodInfo> SymbolMethods,
        ImmutableArray<SymbolGetterInfo> SymbolGetters,
        ImmutableArray<SymbolAliasInfo> SymbolAliases,
        ImmutableArray<MethodAliasInfo> MethodAliases,
        string? ToStringTag,
        bool UseArrayInstance,
        bool UseFunctionInstance,
        string? InstanceTypeName,
        string? InstanceTypeSimpleName,
        string? IntrinsicName,
        string? TryGetMethod,
        string CacheKey);

    private sealed record GetterInfo(string MethodName, string PropertyName, string DisplayName, bool Enumerable,
        bool Configurable, bool IsStatic);

    private sealed record SetterInfo(string MethodName, string PropertyName, string DisplayName, bool Enumerable,
        bool Configurable, bool IsStatic);

    private sealed record MethodInfo(string MethodName, string PropertyName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, HostMethodSignature Signature, bool IsStatic);

    private sealed record SymbolMethodInfo(string MethodName, string SymbolName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, HostMethodSignature Signature, bool IsStatic);

    private sealed record SymbolGetterInfo(string MethodName, string SymbolName, string DisplayName,
        bool Enumerable, bool Configurable, bool IsStatic);

    private sealed record SymbolAliasInfo(string SymbolName, string TargetPropertyName,
        bool Enumerable, bool Writable, bool Configurable);

    private sealed record MethodAliasInfo(string AliasName, string TargetPropertyName,
        bool Enumerable, bool Writable, bool Configurable);

    private sealed record ConstructorInfo(string ClassName, string? Namespace, string PrototypeTypeName, string LengthLiteral,
        string DisplayName, ImmutableArray<ConstructorMethodInfo> StaticMethods, string CacheKey);

    private sealed record ConstructorMethodInfo(string MethodName, string PropertyName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, ConstructorMethodSignature Signature, bool ReturnsJsValue);

    private sealed class PrototypeCacheKeyComparer : IEqualityComparer<PrototypeInfo>
    {
        public static readonly PrototypeCacheKeyComparer Instance = new();

        public bool Equals(PrototypeInfo? x, PrototypeInfo? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.CacheKey, y.CacheKey, StringComparison.Ordinal);
        }

        public int GetHashCode(PrototypeInfo obj)
            => StringComparer.Ordinal.GetHashCode(obj.CacheKey);
    }

    private sealed class ConstructorCacheKeyComparer : IEqualityComparer<ConstructorInfo>
    {
        public static readonly ConstructorCacheKeyComparer Instance = new();

        public bool Equals(ConstructorInfo? x, ConstructorInfo? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.CacheKey, y.CacheKey, StringComparison.Ordinal);
        }

        public int GetHashCode(ConstructorInfo obj)
            => StringComparer.Ordinal.GetHashCode(obj.CacheKey);
    }

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

    private sealed record WellKnownTypes(
        INamedTypeSymbol? JsValueType,
        INamedTypeSymbol? ReadOnlyListType,
        INamedTypeSymbol? RealmStateType)
    {
        public static WellKnownTypes From(Compilation compilation)
        {
            var jsValueType = compilation.GetTypeByMetadataName("Asynkron.JsEngine.JsTypes.JsValue");
            var readOnlyListType = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
            var realmStateType = compilation.GetTypeByMetadataName("Asynkron.JsEngine.Runtime.RealmState");
            return new WellKnownTypes(jsValueType, readOnlyListType, realmStateType);
        }
    }
}
