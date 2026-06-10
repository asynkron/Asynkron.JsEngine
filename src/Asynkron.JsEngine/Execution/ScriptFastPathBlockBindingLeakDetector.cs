using System.Collections.Generic;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// <para>
/// Detects whether a script body references a block-scoped lexical binding from outside the block
/// (or for-loop head) that declares it.
/// </para>
/// <para>
/// On the script-root-slot fast path (issue #3084) block-scoped <c>let</c>/<c>const</c> bindings are
/// allocated flat slots, but those slots are not invalidated when their block exits. A reference to
/// such a binding from outside its block — e.g. <c>typeof i</c> or <c>i</c> after a
/// <c>for (let i ...)</c> loop — would therefore read the binding's stale value instead of resolving
/// against the live scope chain and reporting it as undeclared. Scripts matching this shape must
/// decline the fast path and run via the scope-chain-aware path instead.
/// </para>
/// <para>
/// The analysis is deliberately conservative: it may decline a script that would in fact be safe,
/// but it never admits a leaking shape. It walks each <c>for</c> head and each nested block,
/// collecting the lexical binding names they introduce, then reports a leak if any of those names is
/// referenced anywhere in the body outside the declaring construct.
/// </para>
/// </summary>
internal static class ScriptFastPathBlockBindingLeakDetector
{
    public static bool HasOutOfScopeBlockBindingReference(BlockStatement body)
    {
        return HasOutOfScopeBlockBindingReference(body, includeNestedBlocks: true);
    }

    public static bool HasOutOfScopeForHeadBindingReference(BlockStatement body)
    {
        return HasOutOfScopeBlockBindingReference(body, includeNestedBlocks: false);
    }

    private static bool HasOutOfScopeBlockBindingReference(BlockStatement body, bool includeNestedBlocks)
    {
        // Collect (declaring construct, block-scoped lexical names) pairs.
        var declarations = new List<(StatementNode Construct, HashSet<Symbol> Names)>();
        CollectBlockLexicalDeclarations(body, isFunctionBody: true, includeNestedBlocks, declarations);

        if (declarations.Count == 0)
        {
            return false;
        }

        foreach (var (construct, names) in declarations)
        {
            var referenceScanner = new IdentifierReferenceScanner(names, construct);
            referenceScanner.Visit(body);
            if (referenceScanner.FoundOutsideReference)
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectBlockLexicalDeclarations(
        BlockStatement block,
        bool isFunctionBody,
        bool includeNestedBlocks,
        List<(StatementNode Construct, HashSet<Symbol> Names)> declarations)
    {
        foreach (var statement in block.Statements)
        {
            CollectFromStatement(statement, isTopLevelBlock: isFunctionBody, includeNestedBlocks, declarations);
        }
    }

    private static void CollectFromStatement(
        StatementNode statement,
        bool isTopLevelBlock,
        bool includeNestedBlocks,
        List<(StatementNode Construct, HashSet<Symbol> Names)> declarations)
    {
        switch (statement)
        {
            case ForStatement forStatement:
            {
                if (forStatement.Initializer is VariableDeclaration
                    {
                        Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                    } declaration)
                {
                    var names = new HashSet<Symbol>();
                    foreach (var declarator in declaration.Declarators)
                    {
                        CollectBindingNames(declarator.Target, names);
                    }

                    if (names.Count > 0)
                    {
                        declarations.Add((forStatement, names));
                    }
                }

                CollectFromStatement(forStatement.Body, isTopLevelBlock: false, includeNestedBlocks, declarations);
                break;
            }

            case ForEachStatement forEach:
            {
                if (forEach.DeclarationKind is
                    VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing)
                {
                    var names = new HashSet<Symbol>();
                    CollectBindingNames(forEach.Target, names);
                    if (names.Count > 0)
                    {
                        declarations.Add((forEach, names));
                    }
                }

                CollectFromStatement(forEach.Body, isTopLevelBlock: false, includeNestedBlocks, declarations);
                break;
            }

            case BlockStatement nestedBlock:
            {
                // A nested block (not the function's own top-level block) introduces a block scope.
                if (includeNestedBlocks && !isTopLevelBlock)
                {
                    var names = new HashSet<Symbol>();
                    foreach (var inner in nestedBlock.Statements)
                    {
                        if (inner is VariableDeclaration
                            {
                                Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                            } innerDeclaration)
                        {
                            foreach (var declarator in innerDeclaration.Declarators)
                            {
                                CollectBindingNames(declarator.Target, names);
                            }
                        }
                    }

                    if (names.Count > 0)
                    {
                        declarations.Add((nestedBlock, names));
                    }
                }

                foreach (var inner in nestedBlock.Statements)
                {
                    CollectFromStatement(inner, isTopLevelBlock: false, includeNestedBlocks, declarations);
                }

                break;
            }

            case IfStatement ifStatement:
                CollectFromStatement(ifStatement.Then, isTopLevelBlock: false, includeNestedBlocks, declarations);
                if (ifStatement.Else is not null)
                {
                    CollectFromStatement(ifStatement.Else, isTopLevelBlock: false, includeNestedBlocks, declarations);
                }

                break;

            case LoopStatementNode { Body: { } loopBody }:
                CollectFromStatement(loopBody, isTopLevelBlock: false, includeNestedBlocks, declarations);
                break;

            case TryStatement tryStatement:
                CollectFromStatement(tryStatement.TryBlock, isTopLevelBlock: false, includeNestedBlocks, declarations);
                if (tryStatement.Catch is { } catchClause)
                {
                    CollectFromStatement(catchClause.Body, isTopLevelBlock: false, includeNestedBlocks, declarations);
                }

                if (tryStatement.Finally is { } finallyBlock)
                {
                    CollectFromStatement(finallyBlock, isTopLevelBlock: false, includeNestedBlocks, declarations);
                }

                break;
        }
    }

    private static void CollectBindingNames(BindingTarget? target, HashSet<Symbol> names)
    {
        switch (target)
        {
            case null:
                break;

            case IdentifierBinding identifier:
                names.Add(identifier.Name);
                break;

            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    CollectBindingNames(element?.Target, names);
                }

                CollectBindingNames(arrayBinding.RestElement, names);
                break;

            case ObjectBinding objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    CollectBindingNames(property.Target, names);
                }

                CollectBindingNames(objectBinding.RestElement, names);
                break;
        }
    }

    /// <summary>
    /// Walks the function body and reports whether any of <paramref name="targetNames"/> appears as
    /// an identifier reference outside the declaring <paramref name="construct"/>.
    /// </summary>
    private sealed class IdentifierReferenceScanner : AstVisitor
    {
        private readonly HashSet<Symbol> _targetNames;
        private readonly StatementNode _construct;
        private bool _insideConstruct;

        public IdentifierReferenceScanner(HashSet<Symbol> targetNames, StatementNode construct)
        {
            _targetNames = targetNames;
            _construct = construct;
        }

        public bool FoundOutsideReference { get; private set; }

        protected override void VisitStatement(StatementNode statement)
        {
            if (FoundOutsideReference)
            {
                ShouldStop = true;
                return;
            }

            if (ReferenceEquals(statement, _construct))
            {
                var previous = _insideConstruct;
                _insideConstruct = true;
                base.VisitStatement(statement);
                _insideConstruct = previous;
                return;
            }

            base.VisitStatement(statement);
        }

        protected override void VisitIdentifierExpression(IdentifierExpression node)
        {
            if (!_insideConstruct && _targetNames.Contains(node.Name))
            {
                FoundOutsideReference = true;
                ShouldStop = true;
                return;
            }

            base.VisitIdentifierExpression(node);
        }
    }
}
