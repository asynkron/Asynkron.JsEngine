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

            // First pass: determine if strict mode
            var isStrict = false;
            foreach (var switchCase in self.Cases)
            {
                if (switchCase.Body.IsStrict)
                {
                    isStrict = true;
                    break;
                }
            }

            // Second pass: collect declarations
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
                        // Per Annex B.3.3.1: In strict mode, function declarations in blocks
                        // are NOT hoisted as var-scoped bindings (no AnnexB extension).
                        // They remain as lexical declarations evaluated when execution reaches them.
                        if (!isStrict)
                        {
                            var isAsyncOrGenerator = funcDecl.Function.IsAsync || funcDecl.Function.WasAsync ||
                                                     funcDecl.Function.IsGenerator;
                            functionBindings.Add(new SwitchFunctionBinding(funcDecl.Name, funcDecl.Function,
                                !isAsyncOrGenerator));
                        }
                        continue;
                    }

                    if (stmt is ClassDeclaration classDecl)
                    {
                        classBindings.Add(classDecl.Name);
                    }
                }
            }

            return new SwitchInstantiationPlan(
                isStrict,
                lexicalBindings.ToImmutable(),
                functionBindings.ToImmutable(),
                classBindings.ToImmutable());
        });
    }
}
