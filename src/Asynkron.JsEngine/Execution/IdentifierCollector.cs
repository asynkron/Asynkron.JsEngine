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
            case SimpleVariableDeclarationInstruction { Initializer: not null } varDecl:
                // Only visit the initializer expression, NOT the target symbol
                // The target symbol declares a new variable and should NOT be collected
                // because its scope is determined by the enclosing block/loop, not the plan
                Visit(varDecl.Initializer);
                break;
            case IteratorInitInstruction iterInit:
                Visit(iterInit.IterableExpression);
                break;
            case CompoundAssignmentSlotInstruction compoundAssign:
                // Visit the RHS expression for any identifiers it references
                // Don't collect target symbol - it's looked up through the scope chain
                Visit(compoundAssign.RhsExpression);
                break;
            // IncrementSlotInstruction just operates on a symbol - no expressions to visit
            case EnterWithInstruction enterWith:
                Visit(enterWith.ObjectExpression);
                break;
            case YieldStarInstruction yieldStar:
                Visit(yieldStar.IterableExpression);
                break;
            // Note: PushEnvironmentInstruction symbols belong to iteration environments,
            // not the execution plan environment. They already have slots assigned by
            // LoopNormalizer/IteratorDriverFactory, so we don't collect them here.
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
        // Only collect compiler-generated symbols (resume slots, iterator state, etc.)
        // User variables from outer scopes should NOT be assigned slots in the execution plan environment
        // Compiler-generated symbols all start with '\u0001' prefix
        // User variables declared IN the plan are collected via SimpleVariableDeclarationInstruction
        if (node.Name.Name.StartsWith('\u0001'))
        {
            Identifiers.Add(node.Name);
        }
    }

    protected override void VisitAssignment(AssignmentExpression node)
    {
        // Only collect compiler-generated assignment targets
        // User variables declared IN the plan are collected via SimpleVariableDeclarationInstruction
        if (node.Target.Name.StartsWith('\u0001'))
        {
            Identifiers.Add(node.Target);
        }
        base.VisitAssignment(node);
    }
}
