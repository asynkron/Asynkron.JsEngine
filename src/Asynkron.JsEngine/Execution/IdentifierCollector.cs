using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Visitor that collects all IdentifierExpression symbols from instructions.
/// Uses AstVisitor base class for consistent AST traversal.
/// IMPORTANT: Only collects identifiers that are READ or WRITTEN, not ones being DECLARED.
/// Variable declarations (let x = ...) should NOT collect 'x', only the initializer.
/// </summary>
internal sealed class IdentifierCollector : AstVisitor
{
    public HashSet<Symbol> Identifiers { get; } = new(ReferenceEqualityComparer<Symbol>.Instance);
    private readonly HashSet<Symbol> _perIterationSymbols = new(ReferenceEqualityComparer<Symbol>.Instance);

    /// <summary>
    /// Pre-collect per-iteration symbols from all instructions.
    /// Must be called before VisitInstruction to ensure we know which symbols are per-iteration.
    /// </summary>
    public void CollectPerIterationSymbols(IEnumerable<ExecutionInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction is PushEnvironmentInstruction pushEnv)
            {
                foreach (var symbol in pushEnv.PerIterationBindings)
                {
                    _perIterationSymbols.Add(symbol);
                }
            }
        }
    }

    public void VisitInstruction(ExecutionInstruction instruction)
    {
        switch (instruction)
        {
            case StatementInstruction stmt:
                Visit(stmt.Statement);
                break;
            case ExpressionInstruction expr:
                Visit(expr.Expression);
                break;
            case EvaluateAndDiscardInstruction eval:
                Visit(eval.Expression);
                break;
            case YieldInstruction { YieldExpression: not null } yield:
                Visit(yield.YieldExpression);
                break;
            case ReturnInstruction { ReturnExpression: not null } ret:
                Visit(ret.ReturnExpression);
                break;
            case ThrowInstruction thr:
                Visit(thr.Expression);
                break;
            case BranchInstruction branch:
                Visit(branch.Condition);
                break;
            case SimpleVariableDeclarationInstruction varDecl:
                // Only assign slots to let/const declarations, NOT var declarations.
                // var declarations are hoisted and share binding with parameters,
                // so they should NOT get execution plan slots.
                // Also exclude per-iteration bindings - they get slots via PushEnvironmentInstruction.
                if (varDecl.VarKind is VariableKind.Let or VariableKind.Const &&
                    !_perIterationSymbols.Contains(varDecl.TargetSymbol))
                {
                    Identifiers.Add(varDecl.TargetSymbol);
                }
                // Also visit the initializer expression if present
                if (varDecl.Initializer is not null)
                {
                    Visit(varDecl.Initializer);
                }
                break;
            case IteratorInitInstruction iterInit:
                Visit(iterInit.IterableExpression);
                break;
            case EnterWithInstruction enterWith:
                Visit(enterWith.ObjectExpression);
                break;
            case YieldStarInstruction yieldStar:
                Visit(yieldStar.IterableExpression);
                break;
            // Note: PushEnvironmentInstruction symbols are pre-collected via CollectPerIterationSymbols()
            // and excluded from Identifiers since they get their slots via PushEnvironmentInstruction.
        }
    }

    protected override void VisitStatement(StatementNode statement)
    {
        while (true)
        {
            // Special handling for statements that declare variables via bindings
            // We should NOT collect the binding targets (they declare NEW variables)
            // We should ONLY collect identifiers that are READ or WRITTEN

            switch (statement)
            {
                case VariableDeclaration varDecl:
                    // Only visit initializers, NOT binding targets (e.g., in 'let x = 0', don't collect 'x')
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null) VisitExpression(declarator.Initializer);
                    }

                    return;

                case ForEachStatement forEach:
                    // Don't visit Target (declares loop variable), only iterable and body
                    VisitExpression(forEach.Iterable);
                    statement = forEach.Body;
                    continue;

                case TryStatement tryStmt:
                    // Don't visit catch binding (declares error variable)
                    VisitBlockStatement(tryStmt.TryBlock);
                    if (tryStmt.Catch is not null) VisitBlockStatement(tryStmt.Catch.Body); // Skip Catch.Binding
                    if (tryStmt.Finally is not null) VisitBlockStatement(tryStmt.Finally);
                    return;

                default:
                    // For all other statements, use the default behavior
                    base.VisitStatement(statement);
                    return;
            }
        }
    }

    protected override void VisitExpression(ExpressionNode expression)
    {
        while (true)
        {
            // Special handling for DestructuringAssignmentExpression
            // Target is a binding pattern that may declare variables, don't traverse it
            if (expression is DestructuringAssignmentExpression destructuring)
            {
                expression = destructuring.Value;
                continue;
            }

            // For all other expressions, use the default behavior
            base.VisitExpression(expression);
            break;
        }
    }

    protected override void VisitIdentifier(IdentifierExpression node)
    {
        // Collect compiler-generated symbols (resume slots, iterator state, etc.)
        // and any symbol that might reference a locally declared variable
        // Compiler-generated symbols all start with '\u0001' prefix
        // User variables declared via SimpleVariableDeclarationInstruction are collected
        // in VisitInstruction, so we add them to Identifiers there.
        // Here we only need to add compiler-generated symbols.
        if (node.Name.Name.StartsWith('\u0001'))
        {
            Identifiers.Add(node.Name);
        }
    }

    protected override void VisitAssignment(AssignmentExpression node)
    {
        // Collect compiler-generated assignment targets
        if (node.Target.Name.StartsWith('\u0001'))
        {
            Identifiers.Add(node.Target);
        }
        base.VisitAssignment(node);
    }
}
