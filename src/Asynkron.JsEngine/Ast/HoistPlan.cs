using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Cached hoisting metadata for a block. Built once per AST node and reused to avoid per-entry allocations.
/// </summary>
internal sealed class HoistPlan
{
    internal HashSet<Symbol> LexicalNames { get; }
    internal Dictionary<Symbol, bool> LexicalDeclarationKinds { get; }
    internal HashSet<Symbol> CatchParameterNames { get; }
    internal HashSet<Symbol> SimpleCatchParameterNames { get; }
    internal bool HasFunctionDeclarations { get; }
    internal bool NeedsEnvironment =>
        HasFunctionDeclarations ||
        LexicalNames.Count > 0 ||
        CatchParameterNames.Count > 0 ||
        SimpleCatchParameterNames.Count > 0;

    private HoistPlan(
        HashSet<Symbol> lexicalNames,
        Dictionary<Symbol, bool> lexicalDeclarationKinds,
        HashSet<Symbol> catchParameterNames,
        HashSet<Symbol> simpleCatchParameterNames,
        bool hasFunctionDeclarations)
    {
        LexicalNames = lexicalNames;
        LexicalDeclarationKinds = lexicalDeclarationKinds;
        CatchParameterNames = catchParameterNames;
        SimpleCatchParameterNames = simpleCatchParameterNames;
        HasFunctionDeclarations = hasFunctionDeclarations;
    }

    internal static HoistPlan Build(BlockStatement block)
    {
        var lexicalNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        var lexicalKindMap = new Dictionary<Symbol, bool>(ReferenceEqualityComparer<Symbol>.Instance);
        var catchNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        var simpleCatchNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        var hasFunctionDeclarations = false;

        CollectLexical(block, lexicalNames, lexicalKindMap, ref hasFunctionDeclarations);
        CollectCatchNames(block, catchNames);
        CollectSimpleCatchNames(block, simpleCatchNames);

        return new HoistPlan(lexicalNames, lexicalKindMap, catchNames, simpleCatchNames, hasFunctionDeclarations);
    }

    private static void CollectLexical(
        StatementNode statement,
        HashSet<Symbol> names,
        Dictionary<Symbol, bool> lexicalKindMap,
        ref bool hasFunctionDeclarations)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectLexical(inner, names, lexicalKindMap, ref hasFunctionDeclarations);
                    }
                    break;

                case VariableDeclaration
                {
                    Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                } letDecl:
                    var isConstDecl = letDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                    foreach (var declarator in letDecl.Declarators)
                    {
                        CollectBindingSymbols(declarator.Target, names, lexicalKindMap, isConstDecl);
                    }
                    break;

                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    lexicalKindMap[classDeclaration.Name] = true;
                    break;

                case FunctionDeclaration:
                    // Functions are block-scoped in this interpreter, so they force an environment,
                    // but they are not part of lexicalNames.
                    hasFunctionDeclarations = true;
                    break;

                case IfStatement ifStatement:
                    CollectLexical(ifStatement.Then, names, lexicalKindMap, ref hasFunctionDeclarations);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }
                    break;

                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;

                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;

                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;

                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration
                        {
                            Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                        } decl)
                    {
                        var isConstInitializer = decl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                        foreach (var declarator in decl.Declarators)
                        {
                            CollectBindingSymbols(declarator.Target, names, lexicalKindMap, isConstInitializer);
                        }
                    }

                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }
                    break;

                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing)
                    {
                        var isConstForEach = forEachStatement.DeclarationKind is VariableKind.Const
                            or VariableKind.Using or VariableKind.AwaitUsing;
                        CollectBindingSymbols(forEachStatement.Target, names, lexicalKindMap, isConstForEach);
                    }

                    statement = forEachStatement.Body;
                    continue;

                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectLexical(switchCase.Body, names, lexicalKindMap, ref hasFunctionDeclarations);
                    }
                    break;

                case TryStatement tryStatement:
                    CollectLexical(tryStatement.TryBlock, names, lexicalKindMap, ref hasFunctionDeclarations);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is not null)
                        {
                            CollectBindingSymbols(catchClause.Binding, names, lexicalKindMap, isConst: true);
                        }

                        CollectLexical(catchClause.Body, names, lexicalKindMap, ref hasFunctionDeclarations);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }

                    break;

                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    continue;
            }

            break;
        }
    }

    private static void CollectCatchNames(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectCatchNames(inner, names);
                    }
                    break;

                case IfStatement ifStatement:
                    CollectCatchNames(ifStatement.Then, names);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }
                    break;

                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;

                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;

                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;

                case ForStatement forStatement:
                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }
                    break;

                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;

                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectCatchNames(switchCase.Body, names);
                    }
                    break;

                case TryStatement tryStatement:
                    CollectCatchNames(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is not null)
                        {
                            CollectBindingNamesOnly(catchClause.Binding, names);
                        }

                        CollectCatchNames(catchClause.Body, names);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }
                    break;
            }

            break;
        }
    }

    private static void CollectSimpleCatchNames(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectSimpleCatchNames(inner, names);
                    }
                    break;

                case IfStatement ifStatement:
                    CollectSimpleCatchNames(ifStatement.Then, names);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }
                    break;

                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;

                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;

                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;

                case ForStatement forStatement:
                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }
                    break;

                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;

                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectSimpleCatchNames(switchCase.Body, names);
                    }
                    break;

                case TryStatement tryStatement:
                    CollectSimpleCatchNames(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is IdentifierBinding identifierBinding)
                        {
                            names.Add(identifierBinding.Name);
                        }

                        CollectSimpleCatchNames(catchClause.Body, names);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }
                    break;
            }

            break;
        }
    }

    private static void CollectBindingSymbols(BindingTarget target, HashSet<Symbol> names, Dictionary<Symbol, bool> lexicalKindMap, bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    names.Add(id.Name);
                    lexicalKindMap[id.Name] = lexicalKindMap.TryGetValue(id.Name, out var existing)
                        ? existing || isConst
                        : isConst;
                    break;

                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            CollectBindingSymbols(elementTarget, names, lexicalKindMap, isConst);
                        }
                    }

                    if (arrayBinding.RestElement is { } restElement)
                    {
                        target = restElement;
                        continue;
                    }

                    break;

                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingSymbols(property.Target, names, lexicalKindMap, isConst);
                    }

                    if (objectBinding.RestElement is { } restTarget)
                    {
                        target = restTarget;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void CollectBindingNamesOnly(BindingTarget target, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    names.Add(id.Name);
                    break;

                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            CollectBindingNamesOnly(elementTarget, names);
                        }
                    }

                    if (arrayBinding.RestElement is { } restElement)
                    {
                        target = restElement;
                        continue;
                    }

                    break;

                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingNamesOnly(property.Target, names);
                    }

                    if (objectBinding.RestElement is { } restTarget)
                    {
                        target = restTarget;
                        continue;
                    }

                    break;
            }

            break;
        }
    }
}
