#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a switch statement with its cases.
/// </summary>
public sealed record SwitchStatement(
    SourceReference? Source,
    ExpressionNode Discriminant,
    ImmutableArray<SwitchCase> Cases) : StatementNode(Source), IAstCacheable<SwitchInstantiationPlan>
{
    private SwitchInstantiationPlan? _cachedInstantiationPlan;

    SwitchInstantiationPlan IAstCacheable<SwitchInstantiationPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedInstantiationPlan, this, static self =>
        {
            var lexicalBindings = ImmutableArray.CreateBuilder<SwitchLexicalBinding>();
            var functionBindings = ImmutableArray.CreateBuilder<SwitchFunctionBinding>();
            var classBindings = ImmutableArray.CreateBuilder<Symbol>();

            // Collect all declarations from all case bodies
            // Note: In strict mode, function declarations should NOT be hoisted (Annex B.3.3.1),
            // but we collect them here anyway so we can make the decision at runtime based on
            // the actual execution context, not the static parse-time context.
            foreach (var switchCase in self.Cases)
            {
                foreach (var stmt in switchCase.Body.Statements)
                {
                    if (stmt is VariableDeclaration
                        {
                            Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using
                            or VariableKind.AwaitUsing
                        } varDecl)
                    {
                        var isConst =
                            varDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                        foreach (var declarator in varDecl.Declarators)
                        {
                            lexicalBindings.Add(new SwitchLexicalBinding(declarator.Target, isConst));
                        }

                        continue;
                    }

                    if (stmt is FunctionDeclaration funcDecl)
                    {
                        var isAsyncOrGenerator = funcDecl.Function.IsAsync || funcDecl.Function.WasAsync ||
                                                 funcDecl.Function.IsGenerator;
                        functionBindings.Add(new SwitchFunctionBinding(funcDecl.Name, funcDecl.Function,
                            !isAsyncOrGenerator));
                        continue;
                    }

                    if (stmt is ClassDeclaration classDecl)
                    {
                        classBindings.Add(classDecl.Name);
                    }
                }
            }

            return new SwitchInstantiationPlan(
                false, // isStrict is not determinable at cache creation time
                lexicalBindings.ToImmutable(),
                functionBindings.ToImmutable(),
                classBindings.ToImmutable());
        });
    }
}
