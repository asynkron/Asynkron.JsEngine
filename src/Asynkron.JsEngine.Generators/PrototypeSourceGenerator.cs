using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Asynkron.JsEngine.Generators;

[Generator]
public sealed class PrototypeSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var wellKnownTypes = context.CompilationProvider.Select<Compilation, WellKnownTypes>(
            (compilation, _) => WellKnownTypes.From(compilation));

        var generatorRoot = context.AnalyzerConfigOptionsProvider.Select<AnalyzerConfigOptionsProvider, string?>(
            (options, _) => options.GlobalOptions.TryGetValue("build_property.PrototypeGeneratorRoot", out var value) ? value : null);

        var prototypeCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Asynkron.JsEngine.Runtime.Prototypes.JsPrototypeAttribute",
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (ctx, _) => new PrototypeTarget((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0], ctx.TargetNode.SyntaxTree.FilePath ?? string.Empty));

        var constructorCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorAttribute",
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (ctx, _) => new ConstructorTarget((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0], ctx.TargetNode.SyntaxTree.FilePath ?? string.Empty));

        var hostFunctionCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Asynkron.JsEngine.Runtime.Prototypes.JsHostFunctionAttribute",
            static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
            static (ctx, _) => new HostFunctionCandidate((IMethodSymbol)ctx.TargetSymbol, ctx.Attributes[0], ctx.TargetNode.SyntaxTree.FilePath ?? string.Empty));

        var prototypes = prototypeCandidates
            .Combine(generatorRoot)
            .Where(pair => ShouldInclude(pair.Left.FilePath, pair.Right))
            .Select<(PrototypeTarget Left, string? Right), PrototypeTarget>((pair, _) => pair.Left)
            .Collect()
            .SelectMany<ImmutableArray<PrototypeTarget>, PrototypeTarget>((items, _) => items.Distinct(PrototypeTargetComparer.Instance))
            .Combine(wellKnownTypes)
            .Select<(PrototypeTarget, WellKnownTypes), PrototypeInfo?>((data, _) => TransformPrototype(data.Item1, data.Item2))
            .Where(info => info is not null)
            .Select<PrototypeInfo?, PrototypeInfo>((info, _) => info!)
            .WithComparer(PrototypeCacheKeyComparer.Instance);

        var constructors = constructorCandidates
            .Combine(generatorRoot)
            .Where(pair => ShouldInclude(pair.Left.FilePath, pair.Right))
            .Select<(ConstructorTarget Left, string? Right), ConstructorTarget>((pair, _) => pair.Left)
            .Collect()
            .SelectMany<ImmutableArray<ConstructorTarget>, ConstructorTarget>((items, _) => items.Distinct(ConstructorTargetComparer.Instance))
            .Combine(wellKnownTypes)
            .Select<(ConstructorTarget, WellKnownTypes), ConstructorInfo?>((data, _) => TransformConstructor(data.Item1, data.Item2))
            .Where(info => info is not null)
            .Select<ConstructorInfo?, ConstructorInfo>((info, _) => info!)
            .WithComparer(ConstructorCacheKeyComparer.Instance);

        var orderedPrototypes = prototypes
            .Collect()
            .SelectMany<ImmutableArray<PrototypeInfo>, PrototypeInfo>((items, _) => items.OrderBy(p => p.CacheKey, StringComparer.Ordinal));

        var orderedConstructors = constructors
            .Collect()
            .SelectMany<ImmutableArray<ConstructorInfo>, ConstructorInfo>((items, _) => items.OrderBy(c => c.CacheKey, StringComparer.Ordinal));

        var hostFunctionContainers = hostFunctionCandidates
            .Combine(generatorRoot)
            .Where(pair => ShouldInclude(pair.Left.FilePath, pair.Right))
            .Select<(HostFunctionCandidate Left, string? Right), HostFunctionCandidate>((pair, _) => pair.Left)
            .Collect()
            .SelectMany((items, _) => items
                .GroupBy(item => item.MethodSymbol.ContainingType, SymbolEqualityComparer.Default)
                .Select(group => new HostFunctionContainerTarget((INamedTypeSymbol)group.Key, ImmutableArray.CreateRange(group))))
            .Combine(wellKnownTypes)
            .Select<(HostFunctionContainerTarget, WellKnownTypes), HostFunctionContainerInfo?>((data, _) =>
                TransformHostFunctionContainer(data.Item1, data.Item2))
            .Where(info => info is not null)
            .Select<HostFunctionContainerInfo?, HostFunctionContainerInfo>((info, _) => info!)
            .WithComparer(HostFunctionContainerCacheKeyComparer.Instance);

        context.RegisterSourceOutput(orderedPrototypes, Emit);
        context.RegisterSourceOutput(orderedConstructors, EmitConstructor);
        context.RegisterSourceOutput(hostFunctionContainers, EmitHostFunctions);
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
        var className = typeSymbol.Name;

        // Scan for static methods with JsConstructorMethodAttribute or JsConstructorSymbolGetterAttribute
        var staticMethods = ImmutableArray.CreateBuilder<ConstructorMethodInfo>();
        var symbolGetters = ImmutableArray.CreateBuilder<ConstructorSymbolGetterInfo>();
        var hostFunctions = ImmutableArray.CreateBuilder<HostFunctionInfo>();
        var jsValueType = wellKnown.JsValueType;
        var readOnlyListType = wellKnown.ReadOnlyListType;
        var realmStateType = wellKnown.RealmStateType;
        var evaluationContextType = wellKnown.EvaluationContextType;

        foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (string.Equals(attrName, "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorMethodAttribute", StringComparison.Ordinal))
                {
                    if (!member.IsStatic)
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
                else if (string.Equals(attrName, "Asynkron.JsEngine.Runtime.Prototypes.JsConstructorSymbolGetterAttribute", StringComparison.Ordinal))
                {
                    if (!member.IsStatic)
                    {
                        continue;
                    }

                    var symbolName = attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(symbolName))
                    {
                        continue;
                    }

                    var getterDisplayName = GetNamedValue(attr, "DisplayName") ?? $"get [Symbol.{symbolName}]";
                    var enumerable = GetNamedBool(attr, "Enumerable");
                    var configurable = GetNamedBool(attr, "Configurable", true);

                    // Detect if the method takes a thisValue parameter
                    var takesThisValue = member.Parameters.Length > 0 &&
                        jsValueType is not null &&
                        IsJsValue(member.Parameters[0].Type, jsValueType);

                    symbolGetters.Add(new ConstructorSymbolGetterInfo(member.Name, symbolName, getterDisplayName, enumerable, configurable, takesThisValue));
                }
                else if (string.Equals(attrName, "Asynkron.JsEngine.Runtime.Prototypes.JsHostFunctionAttribute", StringComparison.Ordinal))
                {
                    var functionName = attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(functionName))
                    {
                        continue;
                    }

                    var hostTarget = GetHostFunctionTarget(attr);
                    if (hostTarget != HostFunctionTarget.Constructor)
                    {
                        continue;
                    }

                    var targetName = GetNamedValue(attr, "TargetName");
                    if (!IsMatchingHostFunctionTarget(targetName, className, "Constructor"))
                    {
                        continue;
                    }

                    var signature = GetHostFunctionSignature(member, jsValueType, readOnlyListType, realmStateType, evaluationContextType);
                    var hostDisplayName = GetNamedValue(attr, "DisplayName") ?? functionName;
                    var length = GetNamedDouble(attr, "Length");
                    var enumerable = GetNamedBool(attr, "Enumerable");
                    var configurable = GetNamedBool(attr, "Configurable", true);
                    var writable = GetNamedBool(attr, "Writable", true);
                    var deletePrototype = GetNamedBool(attr, "DeletePrototype");
                    var throwOnMissingTarget = GetNamedBool(attr, "ThrowOnMissingTarget");
                    var returnsJsValue = jsValueType is not null && IsJsValue(member.ReturnType, jsValueType);

                    hostFunctions.Add(new HostFunctionInfo(member.Name, functionName, hostDisplayName, length, enumerable, configurable,
                        writable, deletePrototype, signature, returnsJsValue, member.IsStatic, UsesContext(signature), hostTarget,
                        targetName, throwOnMissingTarget));
                }
            }
        }

        // Extract string data from symbols for caching
        var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();
        var prototypeTypeName = prototypeTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var orderedStaticMethods = OrderConstructorMethods(staticMethods.ToImmutable());
        var orderedSymbolGetters = OrderConstructorSymbolGetters(symbolGetters.ToImmutable());
        var orderedHostFunctions = OrderHostFunctions(hostFunctions.ToImmutable());
        var cacheKey = BuildConstructorCacheKey(className, namespaceName, prototypeTypeName, lengthLiteral, displayName,
            orderedStaticMethods, orderedSymbolGetters, orderedHostFunctions);

        return new ConstructorInfo(className, namespaceName, prototypeTypeName, lengthLiteral, displayName, orderedStaticMethods,
            orderedSymbolGetters, orderedHostFunctions, cacheKey);
    }

    private static HostFunctionContainerInfo? TransformHostFunctionContainer(HostFunctionContainerTarget target, WellKnownTypes wellKnown)
    {
        var typeSymbol = target.TypeSymbol;
        var hostFunctions = ImmutableArray.CreateBuilder<HostFunctionInfo>();
        var jsValueType = wellKnown.JsValueType;
        var readOnlyListType = wellKnown.ReadOnlyListType;
        var realmStateType = wellKnown.RealmStateType;
        var evaluationContextType = wellKnown.EvaluationContextType;

        foreach (var candidate in target.Methods)
        {
            var method = candidate.MethodSymbol;
            if (!method.IsStatic)
            {
                continue;
            }

            var attr = candidate.Attribute;
            var functionName = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(functionName))
            {
                continue;
            }

            var targetKind = GetHostFunctionTarget(attr);
            var targetName = GetNamedValue(attr, "TargetName");
            if ((targetKind == HostFunctionTarget.Constructor || targetKind == HostFunctionTarget.Prototype || targetKind == HostFunctionTarget.Custom) &&
                string.IsNullOrWhiteSpace(targetName))
            {
                continue;
            }

            if (targetKind == HostFunctionTarget.Constructor && !IsMatchingHostFunctionTarget(targetName, typeSymbol.Name, "Constructor"))
            {
                continue;
            }

            if (targetKind == HostFunctionTarget.Prototype && !IsMatchingHostFunctionTarget(targetName, typeSymbol.Name, "Prototype"))
            {
                continue;
            }

            if (targetKind == HostFunctionTarget.Constructor || targetKind == HostFunctionTarget.Prototype ||
                targetKind == HostFunctionTarget.Global || targetKind == HostFunctionTarget.Custom)
            {
                var signature = GetHostFunctionSignature(method, jsValueType, readOnlyListType, realmStateType, evaluationContextType);
                var displayName = GetNamedValue(attr, "DisplayName") ?? functionName;
                var length = GetNamedDouble(attr, "Length");
                var enumerable = GetNamedBool(attr, "Enumerable");
                var configurable = GetNamedBool(attr, "Configurable", true);
                var writable = GetNamedBool(attr, "Writable", true);
                var deletePrototype = GetNamedBool(attr, "DeletePrototype");
                var throwOnMissingTarget = GetNamedBool(attr, "ThrowOnMissingTarget");
                var returnsJsValue = jsValueType is not null && IsJsValue(method.ReturnType, jsValueType);

                hostFunctions.Add(new HostFunctionInfo(method.Name, functionName, displayName, length, enumerable, configurable, writable,
                    deletePrototype, signature, returnsJsValue, method.IsStatic, UsesContext(signature), targetKind, targetName,
                    throwOnMissingTarget));
            }
        }

        if (hostFunctions.Count == 0)
        {
            return null;
        }

        var orderedHostFunctions = OrderHostFunctions(hostFunctions.ToImmutable());
        var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();
        var isStatic = typeSymbol.IsStatic;
        var cacheKey = BuildHostFunctionContainerCacheKey(typeSymbol.Name, namespaceName, isStatic, orderedHostFunctions);

        return new HostFunctionContainerInfo(typeSymbol.Name, namespaceName, isStatic, orderedHostFunctions, cacheKey);
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
        var baseSource = new StringBuilder();
        baseSource.AppendLine("// <auto-generated />");
        baseSource.AppendLine("using Asynkron.JsEngine.Ast;");
        baseSource.AppendLine("using Asynkron.JsEngine.JsTypes;");
        baseSource.AppendLine("using Asynkron.JsEngine.Runtime;");
        baseSource.AppendLine("using Asynkron.JsEngine.Runtime.Prototypes;");
        baseSource.AppendLine("using Asynkron.JsEngine.StdLib;");
        baseSource.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            baseSource.Append("namespace ").Append(info.Namespace).AppendLine(";");
            baseSource.AppendLine();
        }

        baseSource.Append("public sealed partial class ").Append(info.ClassName).AppendLine(" : JsPrototype");
        baseSource.AppendLine("{");
        baseSource.Append("    public ").Append(info.ClassName).AppendLine("(IJsObjectLike prototype, RealmState realm) : base(prototype, realm)");
        baseSource.AppendLine("    {");
        baseSource.AppendLine("        InitializePrototype();");
        baseSource.AppendLine("    }");
        baseSource.AppendLine();
        baseSource.AppendLine("    partial void InitializePrototype();");
        baseSource.AppendLine("    partial void DefinePrototypeMembers(IJsObjectLike prototype, RealmState realm);");
        baseSource.AppendLine();
        baseSource.AppendLine("    public static IJsObjectLike CreatePrototype(RealmState realm)");
        baseSource.AppendLine("    {");
        var prototypeExpr = info.UseArrayInstance
            ? "new JsArray(realm)"
            : info.UseFunctionInstance
                ? "new HostFunction((_, _) => JsValue.Undefined, realm, isConstructor: false)"
                : "new JsObject()";
        baseSource.Append("        var prototype = ").Append(prototypeExpr).AppendLine(";");
        baseSource.Append("        var typed = new ").Append(info.ClassName).AppendLine("(prototype, realm);");
        baseSource.AppendLine("        typed.DefinePrototypeMembers(prototype, realm);");
        baseSource.AppendLine("        typed.ConfigurePrototype();");
        baseSource.AppendLine("        return prototype;");
        baseSource.AppendLine("    }");
        baseSource.AppendLine("}");

        var membersSource = new StringBuilder();
        membersSource.AppendLine("// <auto-generated />");
        membersSource.AppendLine("using Asynkron.JsEngine.Ast;");
        membersSource.AppendLine("using Asynkron.JsEngine.JsTypes;");
        membersSource.AppendLine("using Asynkron.JsEngine.Runtime;");
        membersSource.AppendLine("using Asynkron.JsEngine.Runtime.Prototypes;");
        membersSource.AppendLine("using Asynkron.JsEngine.StdLib;");
        membersSource.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            membersSource.Append("namespace ").Append(info.Namespace).AppendLine(";");
            membersSource.AppendLine();
        }

        membersSource.Append("public sealed partial class ").Append(info.ClassName).AppendLine(" : JsPrototype");
        membersSource.AppendLine("{");
        membersSource.AppendLine("    partial void DefinePrototypeMembers(IJsObjectLike prototype, RealmState realm)");
        membersSource.AppendLine("    {");

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
                var getterTarget = getter!.IsStatic ? info.ClassName : "this";
                membersSource.Append("        var ").Append(getterVar)
                    .Append(" = new HostFunction((thisValue, _) => ").Append(getterTarget).Append(".")
                    .Append(getter.MethodName)
                    .AppendLine("(thisValue), realm, isConstructor: false);");
                membersSource.Append("        ").Append(getterVar)
                    .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                    .Append(getter.DisplayName.Replace("\"", "\\\""))
                    .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            }

            // Emit setter function if exists
            if (hasSetter)
            {
                var setterTarget = setter!.IsStatic ? info.ClassName : "this";
                membersSource.Append("        var ").Append(setterVar)
                    .Append(" = new HostFunction((thisValue, args) => ").Append(setterTarget).Append(".")
                    .Append(setter.MethodName)
                    .AppendLine("(thisValue, args), realm, isConstructor: false);");
                membersSource.Append("        ").Append(setterVar)
                    .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                    .Append(setter.DisplayName.Replace("\"", "\\\""))
                    .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            }

            // Determine enumerable/configurable from getter if present, else from setter
            var enumerable = hasGetter ? getter!.Enumerable : setter!.Enumerable;
            var configurable = hasGetter ? getter!.Configurable : setter!.Configurable;

            // Emit combined property descriptor
            membersSource.Append("        prototype.DefineProperty(\"").Append(propName).Append("\", new PropertyDescriptor { ");
            if (hasGetter)
            {
                membersSource.Append("Get = ").Append(getterVar);
                if (hasSetter)
                {
                    membersSource.Append(", Set = ").Append(setterVar);
                }
            }
            else if (hasSetter)
            {
                membersSource.Append("Set = ").Append(setterVar);
            }
            membersSource.Append(", Enumerable = ").Append(enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(configurable ? "true" : "false")
                .AppendLine(" });");
        }

        foreach (var method in info.Methods)
        {
            var methodVar = $"method_{Sanitize(method.PropertyName)}";
            membersSource.Append("        var ").Append(methodVar).Append(" = new HostFunction(");

            // Determine the target: static uses ClassName, instance uses typed
            var target = method.IsStatic
                ? info.ClassName
                : "this";

            switch (method.Signature)
            {
                case HostMethodSignature.NoArgs:
                    membersSource.Append("(_, _) => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ArgsOnly:
                    membersSource.Append("args => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(args), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ThisOnly:
                    membersSource.Append("(thisValue, _) => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(thisValue), realm, isConstructor: false);");
                    break;
                default:
                    membersSource.Append("(thisValue, args) => ").Append(target).Append(".").Append(method.MethodName)
                        .AppendLine("(thisValue, args), realm, isConstructor: false);");
                    break;
            }
            membersSource.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(method.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            membersSource.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(method.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });").AppendLine();
            membersSource.Append("        prototype.DefineProperty(\"").Append(method.PropertyName)
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
            membersSource.Append("        var ").Append(methodVar).Append(" = new HostFunction(");

            var target = symMethod.IsStatic ? info.ClassName : "this";

            switch (symMethod.Signature)
            {
                case HostMethodSignature.NoArgs:
                    membersSource.Append("(_, _) => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ArgsOnly:
                    membersSource.Append("args => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(args), realm, isConstructor: false);");
                    break;
                case HostMethodSignature.ThisOnly:
                    membersSource.Append("(thisValue, _) => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(thisValue), realm, isConstructor: false);");
                    break;
                default:
                    membersSource.Append("(thisValue, args) => ").Append(target).Append(".").Append(symMethod.MethodName)
                        .AppendLine("(thisValue, args), realm, isConstructor: false);");
                    break;
            }
            membersSource.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(symMethod.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            membersSource.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(symMethod.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });").AppendLine();
            membersSource.Append("        prototype.DefineProperty($\"@@symbol:{JsSymbol.For(\"Symbol.")
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
            var target = symGetter.IsStatic ? info.ClassName : "this";
            membersSource.Append("        var ").Append(getterVar)
                .Append(" = new HostFunction((thisValue, _) => ").Append(target).Append(".")
                .Append(symGetter.MethodName)
                .AppendLine("(thisValue), realm, isConstructor: false);");
            membersSource.Append("        ").Append(getterVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(symGetter.DisplayName.Replace("\"", "\\\""))
                .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            membersSource.Append("        prototype.DefineProperty($\"@@symbol:{JsSymbol.For(\"Symbol.")
                .Append(symGetter.SymbolName).Append("\").GetHashCode()}\", new PropertyDescriptor { Get = ").Append(getterVar)
                .Append(", Enumerable = ").Append(symGetter.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(symGetter.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

        // Emit symbol aliases (e.g., [Symbol.iterator] -> values)
        foreach (var alias in info.SymbolAliases)
        {
            membersSource.Append("        if (prototype.TryGetProperty(\"").Append(alias.TargetPropertyName).AppendLine("\", out var aliasTarget))");
            membersSource.AppendLine("        {");
            membersSource.Append("            prototype.DefineProperty($\"@@symbol:{JsSymbol.For(\"Symbol.")
                .Append(alias.SymbolName).AppendLine("\").GetHashCode()}\",");
            membersSource.AppendLine("                new PropertyDescriptor");
            membersSource.AppendLine("                {");
            membersSource.Append("                    Value = aliasTarget, Writable = ").Append(alias.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(alias.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(alias.Configurable ? "true" : "false")
                .AppendLine();
            membersSource.AppendLine("                });");
            membersSource.AppendLine("        }");
        }

        // Emit method aliases (e.g., toGMTString -> toUTCString)
        var methodAliasVarIndex = 0;
        foreach (var alias in info.MethodAliases)
        {
            var varName = $"methodAliasTarget{methodAliasVarIndex++}";
            membersSource.Append("        if (prototype.TryGetProperty(\"").Append(alias.TargetPropertyName).Append("\", out var ").Append(varName).AppendLine("))");
            membersSource.AppendLine("        {");
            membersSource.Append("            prototype.DefineProperty(\"").Append(alias.AliasName).AppendLine("\",");
            membersSource.AppendLine("                new PropertyDescriptor");
            membersSource.AppendLine("                {");
            membersSource.Append("                    Value = ").Append(varName).Append(", Writable = ").Append(alias.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(alias.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(alias.Configurable ? "true" : "false")
                .AppendLine();
            membersSource.AppendLine("                });");
            membersSource.AppendLine("        }");
        }

        if (!string.IsNullOrEmpty(info.ToStringTag))
        {
            membersSource.AppendLine("        prototype.DefineProperty($\"@@symbol:{JsSymbol.For(\"Symbol.toStringTag\").GetHashCode()}\",");
            membersSource.AppendLine("            new PropertyDescriptor");
            membersSource.AppendLine("            {");
            membersSource.Append("                Value = \"").Append(info.ToStringTag?.Replace("\"", "\\\""))
                .AppendLine("\", Writable = false, Enumerable = false, Configurable = true");
            membersSource.AppendLine("            });");
        }

        membersSource.AppendLine("    }");

        // Generate RequireInstance method if InstanceType is specified
        if (info.InstanceTypeName is not null)
        {
            membersSource.AppendLine();
            membersSource.Append("    private ").Append(info.InstanceTypeName).AppendLine(" RequireInstance(JsValue receiver)");
            membersSource.AppendLine("    {");

            if (!string.IsNullOrEmpty(info.TryGetMethod))
            {
                // Use custom TryGet method (e.g., JsPromise.TryGetInternalPromise)
                membersSource.Append("        if (").Append(info.InstanceTypeName).Append(".").Append(info.TryGetMethod)
                    .AppendLine("(receiver, out var instance) && instance is not null)");
            }
            else
            {
                // Use standard TryGetObject<T>
                membersSource.Append("        if (receiver.TryGetObject<").Append(info.InstanceTypeName).AppendLine(">(out var instance))");
            }

            membersSource.AppendLine("        {");
            membersSource.AppendLine("            return instance;");
            membersSource.AppendLine("        }");
            membersSource.AppendLine();
            membersSource.Append("        throw StandardLibrary.ThrowTypeError(\"").Append(info.IntrinsicName)
                .AppendLine(" method called on incompatible receiver\", realm: Realm);");
            membersSource.AppendLine("    }");
        }

        membersSource.AppendLine("}");

        context.AddSource($"{info.ClassName}.Prototype.g.cs", baseSource.ToString());
        context.AddSource($"{info.ClassName}.Prototype.Members.g.cs", membersSource.ToString());
    }

    private static void EmitConstructor(SourceProductionContext context, ConstructorInfo info)
    {
        var baseSource = new StringBuilder();
        baseSource.AppendLine("// <auto-generated />");
        baseSource.AppendLine("using Asynkron.JsEngine.Ast;");
        baseSource.AppendLine("using Asynkron.JsEngine.JsTypes;");
        baseSource.AppendLine("using Asynkron.JsEngine.Runtime;");
        baseSource.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            baseSource.Append("namespace ").Append(info.Namespace).AppendLine(";");
            baseSource.AppendLine();
        }

        baseSource.Append("public sealed partial class ").Append(info.ClassName).AppendLine();
        baseSource.AppendLine("{");
        baseSource.AppendLine("    partial void DefineConstructorStaticMethods(HostFunction constructor, RealmState realm);");
        baseSource.AppendLine();
        baseSource.AppendLine("    public static HostFunction CreateConstructor(RealmState realm)");
        baseSource.AppendLine("    {");
        baseSource.Append("        var prototype = ").Append(info.PrototypeTypeName).AppendLine(".CreatePrototype(realm);");
        baseSource.Append("        var typed = new ").Append(info.ClassName).AppendLine("(prototype, realm);");
        baseSource.AppendLine("        var constructor = new HostFunction((thisValue, args) => typed.ConstructInstance(thisValue, args), realm)");
        baseSource.AppendLine("        {");
        baseSource.AppendLine("            IsConstructor = true");
        baseSource.AppendLine("        };");
        baseSource.Append("        constructor.DefineProperty(\"length\", new PropertyDescriptor { Value = ")
            .Append(info.LengthLiteral)
            .AppendLine("d, Writable = false, Enumerable = false, Configurable = true });");
        baseSource.Append("        constructor.DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
            .Append(info.DisplayName.Replace("\"", "\\\""))
            .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
        baseSource.AppendLine("        constructor.DefineProperty(\"prototype\",");
        baseSource.AppendLine("            new PropertyDescriptor");
        baseSource.AppendLine("            {");
        baseSource.AppendLine("                Value = prototype, Writable = false, Enumerable = false, Configurable = false");
        baseSource.AppendLine("            });");
        baseSource.AppendLine("        prototype.DefineProperty(\"constructor\",");
        baseSource.AppendLine("            new PropertyDescriptor");
        baseSource.AppendLine("            {");
        baseSource.AppendLine("                Value = constructor, Writable = true, Enumerable = false, Configurable = true");
        baseSource.AppendLine("            });");
        baseSource.AppendLine("        typed.DefineConstructorStaticMethods(constructor, realm);");
        baseSource.AppendLine("        typed.ConfigureConstructor(constructor);");
        baseSource.AppendLine("        return constructor;");
        baseSource.AppendLine("    }");
        baseSource.AppendLine("}");

        var membersSource = new StringBuilder();
        membersSource.AppendLine("// <auto-generated />");
        membersSource.AppendLine("using Asynkron.JsEngine.Ast;");
        membersSource.AppendLine("using Asynkron.JsEngine.JsTypes;");
        membersSource.AppendLine("using Asynkron.JsEngine.Runtime;");
        membersSource.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            membersSource.Append("namespace ").Append(info.Namespace).AppendLine(";");
            membersSource.AppendLine();
        }

        membersSource.Append("public sealed partial class ").Append(info.ClassName).AppendLine();
        membersSource.AppendLine("{");
        membersSource.AppendLine("    partial void DefineConstructorStaticMethods(HostFunction constructor, RealmState realm)");
        membersSource.AppendLine("    {");

        // Generate static method registrations
        foreach (var method in info.StaticMethods)
        {
            var methodVar = $"method_{Sanitize(method.PropertyName)}";
            membersSource.Append("        var ").Append(methodVar).Append(" = new HostFunction(");

            var wrapOpen = method.ReturnsJsValue ? string.Empty : "JsValue.FromObjectUnsafe(";
            var wrapClose = method.ReturnsJsValue ? string.Empty : ")";

            switch (method.Signature)
            {
                case ConstructorMethodSignature.NoArgs:
                    membersSource.Append("(_, _) => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("()").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case ConstructorMethodSignature.ArgsOnly:
                    membersSource.Append("args => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("(args)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case ConstructorMethodSignature.ArgsRealm:
                    membersSource.Append("args => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("(args, realm)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                default: // ThisArgsRealm
                    membersSource.Append("(thisValue, args) => ").Append(wrapOpen).Append(info.ClassName).Append(".")
                        .Append(method.MethodName).Append("(thisValue, args, realm)").Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
            }

            membersSource.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(method.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            membersSource.Append("        ").Append(methodVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(method.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });").AppendLine();
            membersSource.Append("        constructor.DefineProperty(\"").Append(method.PropertyName)
                .Append("\", new PropertyDescriptor { Value = ").Append(methodVar)
                .Append(", Writable = ").Append(method.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(method.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(method.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

        var hostFunctionIndex = 0;
        foreach (var hostFunction in info.HostFunctions)
        {
            var functionVar = $"hostFunction_{Sanitize(hostFunction.Name)}_{hostFunctionIndex++}";
            membersSource.Append("        var ").Append(functionVar).Append(" = new HostFunction(");

            var target = hostFunction.IsStatic
                ? info.ClassName
                : "this";

            var wrapOpen = hostFunction.ReturnsJsValue ? string.Empty : "JsValue.FromObjectUnsafe(";
            var wrapClose = hostFunction.ReturnsJsValue ? string.Empty : ")";

            switch (hostFunction.Signature)
            {
                case HostFunctionSignature.NoArgs:
                    membersSource.Append("(_, _) => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("()")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case HostFunctionSignature.ArgsOnly:
                    membersSource.Append("args => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(args)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case HostFunctionSignature.ThisOnly:
                    membersSource.Append("(thisValue, _) => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(thisValue)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case HostFunctionSignature.ArgsRealm:
                    membersSource.Append("args => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(args, realm)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case HostFunctionSignature.ThisArgsRealm:
                    membersSource.Append("(thisValue, args) => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(thisValue, args, realm)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case HostFunctionSignature.ArgsContext:
                    membersSource.Append("args => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(args, null)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                case HostFunctionSignature.ThisArgsContext:
                    membersSource.Append("(thisValue, args) => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(thisValue, args, null)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
                default:
                    membersSource.Append("(thisValue, args) => ").Append(wrapOpen).Append(target).Append(".")
                        .Append(hostFunction.MethodName).Append("(thisValue, args)")
                        .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                    break;
            }

            if (hostFunction.UsesContext)
            {
                switch (hostFunction.Signature)
                {
                    case HostFunctionSignature.ArgsContext:
                        membersSource.Append("        ").Append(functionVar)
                            .Append(".SetInvokeWithContext((args, _, context, _) => ")
                            .Append(wrapOpen).Append(target).Append(".")
                            .Append(hostFunction.MethodName).Append("(args, context)")
                            .Append(wrapClose).AppendLine(");");
                        break;
                    case HostFunctionSignature.ThisArgsContext:
                        membersSource.Append("        ").Append(functionVar)
                            .Append(".SetInvokeWithContext((args, thisValue, context, _) => ")
                            .Append(wrapOpen).Append(target).Append(".")
                            .Append(hostFunction.MethodName).Append("(thisValue, args, context)")
                            .Append(wrapClose).AppendLine(");");
                        break;
                }
            }

            if (hostFunction.DeletePrototype)
            {
                membersSource.Append("        ").Append(functionVar).AppendLine(".Properties.Delete(\"prototype\");");
            }

            membersSource.Append("        ").Append(functionVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                .Append(hostFunction.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            membersSource.Append("        ").Append(functionVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(hostFunction.DisplayName.Replace("\"", "\\\""))
                .Append("\", Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            membersSource.Append("        constructor.DefineProperty(\"").Append(hostFunction.Name)
                .Append("\", new PropertyDescriptor { Value = ").Append(functionVar)
                .Append(", Writable = ").Append(hostFunction.Writable ? "true" : "false")
                .Append(", Enumerable = ").Append(hostFunction.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(hostFunction.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

        // Generate symbol-keyed getter registrations (e.g., [Symbol.species])
        foreach (var getter in info.SymbolGetters)
        {
            var getterVar = $"symbolGetter_{Sanitize(getter.SymbolName)}";
            membersSource.Append("        var ").Append(getterVar)
                .Append(" = new HostFunction(");
            if (getter.TakesThisValue)
            {
                membersSource.Append("(thisValue, _) => ").Append(info.ClassName).Append(".")
                    .Append(getter.MethodName)
                    .AppendLine("(thisValue), realm, isConstructor: false);");
            }
            else
            {
                membersSource.Append("(_, _) => ").Append(info.ClassName).Append(".")
                    .Append(getter.MethodName)
                    .AppendLine("(), realm, isConstructor: false);");
            }
            membersSource.Append("        ").Append(getterVar)
                .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });")
                .AppendLine();
            membersSource.Append("        ").Append(getterVar)
                .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                .Append(getter.DisplayName.Replace("\"", "\\\""))
                .AppendLine("\", Writable = false, Enumerable = false, Configurable = true });");
            membersSource.Append("        constructor.DefineProperty($\"@@symbol:{JsSymbol.For(\"Symbol.")
                .Append(getter.SymbolName).Append("\").GetHashCode()}\", new PropertyDescriptor { Get = ").Append(getterVar)
                .Append(", Enumerable = ").Append(getter.Enumerable ? "true" : "false")
                .Append(", Configurable = ").Append(getter.Configurable ? "true" : "false")
                .AppendLine(" });");
        }

        membersSource.AppendLine("    }");
        membersSource.AppendLine("}");

        context.AddSource($"{info.ClassName}.Constructor.g.cs", baseSource.ToString());
        context.AddSource($"{info.ClassName}.Constructor.Members.g.cs", membersSource.ToString());
    }

    private static void EmitHostFunctions(SourceProductionContext context, HostFunctionContainerInfo info)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using System;");
        source.AppendLine("using Asynkron.JsEngine.JsTypes;");
        source.AppendLine("using Asynkron.JsEngine.Runtime;");
        source.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            source.Append("namespace ").Append(info.Namespace).AppendLine(";");
            source.AppendLine();
        }

        source.Append("public ");
        if (info.IsStatic)
        {
            source.Append("static ");
        }
        source.Append("partial class ").Append(info.ClassName).AppendLine();
        source.AppendLine("{");
        source.Append("    public ");
        if (info.IsStatic)
        {
            source.Append("static ");
        }
        source.AppendLine("void RegisterHostFunctions(IJsObjectLike global, RealmState realm)");
        source.AppendLine("    {");

        var hostFunctionIndex = 0;
        foreach (var hostFunction in info.HostFunctions)
        {
            var functionVar = $"hostFunction_{Sanitize(hostFunction.Name)}_{hostFunctionIndex++}";
            var methodTarget = hostFunction.IsStatic ? info.ClassName : "this";

            void AppendHostFunction(string targetExpression)
            {
                var wrapOpen = hostFunction.ReturnsJsValue ? string.Empty : "JsValue.FromObjectUnsafe(";
                var wrapClose = hostFunction.ReturnsJsValue ? string.Empty : ")";

                source.Append("        var ").Append(functionVar).Append(" = new HostFunction(");
                switch (hostFunction.Signature)
                {
                    case HostFunctionSignature.NoArgs:
                        source.Append("(_, _) => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("()")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    case HostFunctionSignature.ArgsOnly:
                        source.Append("args => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(args)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    case HostFunctionSignature.ThisOnly:
                        source.Append("(thisValue, _) => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(thisValue)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    case HostFunctionSignature.ArgsRealm:
                        source.Append("args => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(args, realm)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    case HostFunctionSignature.ThisArgsRealm:
                        source.Append("(thisValue, args) => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(thisValue, args, realm)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    case HostFunctionSignature.ArgsContext:
                        source.Append("args => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(args, null)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    case HostFunctionSignature.ThisArgsContext:
                        source.Append("(thisValue, args) => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(thisValue, args, null)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                    default:
                        source.Append("(thisValue, args) => ").Append(wrapOpen).Append(methodTarget).Append(".")
                            .Append(hostFunction.MethodName).Append("(thisValue, args)")
                            .Append(wrapClose).AppendLine(", realm, isConstructor: false);");
                        break;
                }

                if (hostFunction.UsesContext)
                {
                    switch (hostFunction.Signature)
                    {
                        case HostFunctionSignature.ArgsContext:
                            source.Append("        ").Append(functionVar)
                                .Append(".SetInvokeWithContext((args, _, context, _) => ")
                                .Append(wrapOpen).Append(methodTarget).Append(".")
                                .Append(hostFunction.MethodName).Append("(args, context)")
                                .Append(wrapClose).AppendLine(");");
                            break;
                        case HostFunctionSignature.ThisArgsContext:
                            source.Append("        ").Append(functionVar)
                                .Append(".SetInvokeWithContext((args, thisValue, context, _) => ")
                                .Append(wrapOpen).Append(methodTarget).Append(".")
                                .Append(hostFunction.MethodName).Append("(thisValue, args, context)")
                                .Append(wrapClose).AppendLine(");");
                            break;
                    }
                }

                source.Append("        if (global is JsObject realmObject_").Append(hostFunctionIndex).AppendLine(")");
                source.AppendLine("        {");
                source.Append("            ").Append(functionVar).Append(".Realm = realmObject_")
                    .Append(hostFunctionIndex).AppendLine(";");
                source.AppendLine("        }");

                if (hostFunction.DeletePrototype)
                {
                    source.Append("        ").Append(functionVar).AppendLine(".Properties.Delete(\"prototype\");");
                }

                source.Append("        ").Append(functionVar)
                    .Append(".DefineProperty(\"length\", new PropertyDescriptor { Value = ")
                    .Append(hostFunction.LengthLiteral).Append("d, Writable = false, Enumerable = false, Configurable = true });")
                    .AppendLine();
                source.Append("        ").Append(functionVar)
                    .Append(".DefineProperty(\"name\", new PropertyDescriptor { Value = \"")
                    .Append(hostFunction.DisplayName.Replace("\"", "\\\""))
                    .Append("\", Writable = false, Enumerable = false, Configurable = true });")
                    .AppendLine();
                source.Append("        ").Append(targetExpression).Append(".DefineProperty(\"").Append(hostFunction.Name)
                    .Append("\", new PropertyDescriptor { Value = ").Append(functionVar)
                    .Append(", Writable = ").Append(hostFunction.Writable ? "true" : "false")
                    .Append(", Enumerable = ").Append(hostFunction.Enumerable ? "true" : "false")
                    .Append(", Configurable = ").Append(hostFunction.Configurable ? "true" : "false")
                    .AppendLine(" });");
            }

            switch (hostFunction.Target)
            {
                case HostFunctionTarget.Global:
                    AppendHostFunction("global");
                    break;
                case HostFunctionTarget.Custom:
                    source.Append("        if (global.TryGetProperty(\"").Append(hostFunction.TargetName ?? string.Empty)
                        .Append("\", out var targetValue) && targetValue.TryGetObject<IJsObjectLike>(out var targetCustom))");
                    source.AppendLine();
                    source.AppendLine("        {");
                    AppendHostFunction("targetCustom");
                    source.AppendLine("        }");
                    if (hostFunction.ThrowOnMissingTarget)
                    {
                        source.Append("        else").AppendLine();
                        source.Append("        { throw new InvalidOperationException(\"Missing host function target: ")
                            .Append(hostFunction.TargetName?.Replace("\"", "\\\"") ?? string.Empty).Append("\"); }")
                            .AppendLine();
                    }
                    break;
                case HostFunctionTarget.Constructor:
                case HostFunctionTarget.Prototype:
                    source.Append("        if (realm.").Append(hostFunction.TargetName)
                        .Append(" is IJsObjectLike targetRealm)").AppendLine();
                    source.AppendLine("        {");
                    AppendHostFunction("targetRealm");
                    source.AppendLine("        }");
                    if (hostFunction.ThrowOnMissingTarget)
                    {
                        source.Append("        else").AppendLine();
                        source.Append("        { throw new InvalidOperationException(\"Missing host function target: ")
                            .Append(hostFunction.TargetName?.Replace("\"", "\\\"") ?? string.Empty).Append("\"); }")
                            .AppendLine();
                    }
                    break;
            }
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource($"{info.ClassName}.HostFunctions.g.cs", source.ToString());
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

    private static ImmutableArray<ConstructorSymbolGetterInfo> OrderConstructorSymbolGetters(ImmutableArray<ConstructorSymbolGetterInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static g => g.SymbolName, StringComparer.Ordinal)
                .ThenBy(static g => g.MethodName, StringComparer.Ordinal)
                .ToImmutableArray();

    private static ImmutableArray<HostFunctionInfo> OrderHostFunctions(ImmutableArray<HostFunctionInfo> source)
        => source.Length <= 1
            ? source
            : source.OrderBy(static h => h.Name, StringComparer.Ordinal)
                .ThenBy(static h => h.MethodName, StringComparer.Ordinal)
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
        ImmutableArray<ConstructorMethodInfo> staticMethods,
        ImmutableArray<ConstructorSymbolGetterInfo> symbolGetters,
        ImmutableArray<HostFunctionInfo> hostFunctions)
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

        AppendWithLength(builder, symbolGetters.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var getter in symbolGetters)
        {
            AppendWithLength(builder, getter.SymbolName);
            AppendWithLength(builder, getter.MethodName);
            AppendWithLength(builder, getter.DisplayName);
            AppendBool(builder, getter.Enumerable);
            AppendBool(builder, getter.Configurable);
            AppendBool(builder, getter.TakesThisValue);
        }

        AppendWithLength(builder, hostFunctions.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var hostFunction in hostFunctions)
        {
            AppendWithLength(builder, hostFunction.Name);
            AppendWithLength(builder, hostFunction.MethodName);
            AppendWithLength(builder, hostFunction.DisplayName);
            AppendWithLength(builder, hostFunction.LengthLiteral);
            AppendBool(builder, hostFunction.Enumerable);
            AppendBool(builder, hostFunction.Configurable);
            AppendBool(builder, hostFunction.Writable);
            AppendBool(builder, hostFunction.DeletePrototype);
            AppendWithLength(builder, ((int)hostFunction.Signature).ToString(CultureInfo.InvariantCulture));
            AppendBool(builder, hostFunction.ReturnsJsValue);
            AppendBool(builder, hostFunction.IsStatic);
            AppendBool(builder, hostFunction.UsesContext);
            AppendWithLength(builder, ((int)hostFunction.Target).ToString(CultureInfo.InvariantCulture));
            AppendWithLength(builder, hostFunction.TargetName ?? string.Empty);
            AppendBool(builder, hostFunction.ThrowOnMissingTarget);
        }

        return builder.ToString();
    }

    private static string BuildHostFunctionContainerCacheKey(
        string className,
        string? namespaceName,
        bool isStatic,
        ImmutableArray<HostFunctionInfo> hostFunctions)
    {
        var builder = new StringBuilder();
        AppendWithLength(builder, namespaceName ?? string.Empty);
        AppendWithLength(builder, className);
        AppendBool(builder, isStatic);
        AppendWithLength(builder, hostFunctions.Length.ToString(CultureInfo.InvariantCulture));

        foreach (var hostFunction in hostFunctions)
        {
            AppendWithLength(builder, hostFunction.Name);
            AppendWithLength(builder, hostFunction.MethodName);
            AppendWithLength(builder, hostFunction.DisplayName);
            AppendWithLength(builder, hostFunction.LengthLiteral);
            AppendBool(builder, hostFunction.Enumerable);
            AppendBool(builder, hostFunction.Configurable);
            AppendBool(builder, hostFunction.Writable);
            AppendBool(builder, hostFunction.DeletePrototype);
            AppendWithLength(builder, ((int)hostFunction.Signature).ToString(CultureInfo.InvariantCulture));
            AppendBool(builder, hostFunction.ReturnsJsValue);
            AppendBool(builder, hostFunction.IsStatic);
            AppendBool(builder, hostFunction.UsesContext);
            AppendWithLength(builder, ((int)hostFunction.Target).ToString(CultureInfo.InvariantCulture));
            AppendWithLength(builder, hostFunction.TargetName ?? string.Empty);
            AppendBool(builder, hostFunction.ThrowOnMissingTarget);
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

    private sealed record PrototypeTarget(INamedTypeSymbol TypeSymbol, AttributeData Attribute, string FilePath);

    private sealed record ConstructorTarget(INamedTypeSymbol TypeSymbol, AttributeData Attribute, string FilePath);

    private sealed record HostFunctionCandidate(IMethodSymbol MethodSymbol, AttributeData Attribute, string FilePath);

    private sealed record HostFunctionContainerTarget(INamedTypeSymbol TypeSymbol, ImmutableArray<HostFunctionCandidate> Methods);

    private sealed class PrototypeTargetComparer : IEqualityComparer<PrototypeTarget>
    {
        public static readonly PrototypeTargetComparer Instance = new();

        public bool Equals(PrototypeTarget? x, PrototypeTarget? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(x.TypeSymbol, y.TypeSymbol);
        }

        public int GetHashCode(PrototypeTarget obj)
            => SymbolEqualityComparer.Default.GetHashCode(obj.TypeSymbol);
    }

    private sealed class ConstructorTargetComparer : IEqualityComparer<ConstructorTarget>
    {
        public static readonly ConstructorTargetComparer Instance = new();

        public bool Equals(ConstructorTarget? x, ConstructorTarget? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(x.TypeSymbol, y.TypeSymbol);
        }

        public int GetHashCode(ConstructorTarget obj)
            => SymbolEqualityComparer.Default.GetHashCode(obj.TypeSymbol);
    }

    private static bool ShouldInclude(string? filePath, string? rootSetting)
    {
        if (string.IsNullOrWhiteSpace(rootSetting))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = filePath!.Replace('\\', '/');
        var roots = rootSetting!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var root in roots)
        {
            var trimmed = root.Trim().Trim('/').Replace('\\', '/');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var needle = "/" + trimmed + "/";
            if (normalizedPath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.EndsWith("/" + trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

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
        string DisplayName, ImmutableArray<ConstructorMethodInfo> StaticMethods, ImmutableArray<ConstructorSymbolGetterInfo> SymbolGetters,
        ImmutableArray<HostFunctionInfo> HostFunctions, string CacheKey);

    private sealed record ConstructorMethodInfo(string MethodName, string PropertyName, string DisplayName,
        string LengthLiteral, bool Enumerable, bool Configurable, bool Writable, ConstructorMethodSignature Signature, bool ReturnsJsValue);

    private sealed record ConstructorSymbolGetterInfo(string MethodName, string SymbolName, string DisplayName,
        bool Enumerable, bool Configurable, bool TakesThisValue);

    private sealed record HostFunctionContainerInfo(string ClassName, string? Namespace, bool IsStatic,
        ImmutableArray<HostFunctionInfo> HostFunctions, string CacheKey);

    private sealed record HostFunctionInfo(string MethodName, string Name, string DisplayName, string LengthLiteral,
        bool Enumerable, bool Configurable, bool Writable, bool DeletePrototype, HostFunctionSignature Signature,
        bool ReturnsJsValue, bool IsStatic, bool UsesContext, HostFunctionTarget Target, string? TargetName,
        bool ThrowOnMissingTarget);

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

    private sealed class HostFunctionContainerCacheKeyComparer : IEqualityComparer<HostFunctionContainerInfo>
    {
        public static readonly HostFunctionContainerCacheKeyComparer Instance = new();

        public bool Equals(HostFunctionContainerInfo? x, HostFunctionContainerInfo? y)
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

        public int GetHashCode(HostFunctionContainerInfo obj)
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

    private enum HostFunctionSignature
    {
        ThisArgs = 0,
        ThisOnly = 1,
        ArgsOnly = 2,
        NoArgs = 3,
        ArgsRealm = 4,
        ThisArgsRealm = 5,
        ArgsContext = 6,
        ThisArgsContext = 7
    }

    private enum HostFunctionTarget
    {
        Global = 0,
        Constructor = 1,
        Prototype = 2,
        Custom = 3
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

    private static HostFunctionSignature GetHostFunctionSignature(
        IMethodSymbol method,
        INamedTypeSymbol? jsValueType,
        INamedTypeSymbol? readOnlyListType,
        INamedTypeSymbol? realmStateType,
        INamedTypeSymbol? evaluationContextType)
    {
        if (jsValueType is null || readOnlyListType is null)
        {
            return HostFunctionSignature.ThisArgs;
        }

        var parameters = method.Parameters;

        if (parameters.Length == 0)
        {
            return HostFunctionSignature.NoArgs;
        }

        if (parameters.Length == 1)
        {
            if (IsJsValue(parameters[0].Type, jsValueType))
            {
                return HostFunctionSignature.ThisOnly;
            }

            if (IsReadOnlyListOfJsValue(parameters[0].Type, readOnlyListType, jsValueType))
            {
                return HostFunctionSignature.ArgsOnly;
            }
        }

        if (parameters.Length == 2)
        {
            if (IsJsValue(parameters[0].Type, jsValueType) &&
                IsReadOnlyListOfJsValue(parameters[1].Type, readOnlyListType, jsValueType))
            {
                return HostFunctionSignature.ThisArgs;
            }

            if (realmStateType is not null &&
                IsReadOnlyListOfJsValue(parameters[0].Type, readOnlyListType, jsValueType) &&
                IsNullableRealmState(parameters[1].Type, realmStateType))
            {
                return HostFunctionSignature.ArgsRealm;
            }

            if (evaluationContextType is not null &&
                IsReadOnlyListOfJsValue(parameters[0].Type, readOnlyListType, jsValueType) &&
                IsNullableEvaluationContext(parameters[1].Type, evaluationContextType))
            {
                return HostFunctionSignature.ArgsContext;
            }
        }

        if (parameters.Length == 3 &&
            IsJsValue(parameters[0].Type, jsValueType) &&
            IsReadOnlyListOfJsValue(parameters[1].Type, readOnlyListType, jsValueType))
        {
            if (realmStateType is not null && IsNullableRealmState(parameters[2].Type, realmStateType))
            {
                return HostFunctionSignature.ThisArgsRealm;
            }

            if (evaluationContextType is not null && IsNullableEvaluationContext(parameters[2].Type, evaluationContextType))
            {
                return HostFunctionSignature.ThisArgsContext;
            }
        }

        return HostFunctionSignature.ThisArgs;
    }

    private static bool UsesContext(HostFunctionSignature signature)
        => signature is HostFunctionSignature.ArgsContext or HostFunctionSignature.ThisArgsContext;

    private static HostFunctionTarget GetHostFunctionTarget(AttributeData attr)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (string.Equals(arg.Key, "Target", StringComparison.Ordinal))
            {
                var value = arg.Value.Value;
                if (value is int intValue)
                {
                    return (HostFunctionTarget)intValue;
                }

                if (value is IConvertible convertible)
                {
                    return (HostFunctionTarget)convertible.ToInt32(CultureInfo.InvariantCulture);
                }
            }
        }

        return HostFunctionTarget.Global;
    }

    private static bool IsMatchingHostFunctionTarget(string? targetName, string className, string suffix)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return true;
        }

        if (string.Equals(targetName, className, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(targetName, className + suffix, StringComparison.Ordinal);
    }

    private static bool IsJsValue(ITypeSymbol type, INamedTypeSymbol jsValueType)
        => SymbolEqualityComparer.Default.Equals(type, jsValueType);

    private static bool IsNullableEvaluationContext(ITypeSymbol type, INamedTypeSymbol evaluationContextType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, evaluationContextType))
        {
            return true;
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol namedType &&
            SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, evaluationContextType))
        {
            return true;
        }

        return false;
    }

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
        INamedTypeSymbol? RealmStateType,
        INamedTypeSymbol? EvaluationContextType)
    {
        public static WellKnownTypes From(Compilation compilation)
        {
            var jsValueType = compilation.GetTypeByMetadataName("Asynkron.JsEngine.JsTypes.JsValue");
            var readOnlyListType = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
            var realmStateType = compilation.GetTypeByMetadataName("Asynkron.JsEngine.Runtime.RealmState");
            var evaluationContextType = compilation.GetTypeByMetadataName("Asynkron.JsEngine.EvaluationContext");
            return new WellKnownTypes(jsValueType, readOnlyListType, realmStateType, evaluationContextType);
        }
    }
}
