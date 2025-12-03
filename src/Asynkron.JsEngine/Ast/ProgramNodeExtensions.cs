using System.Collections.Immutable;
using System.Diagnostics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ProgramNode program)
    {
        public object? EvaluateProgram(
            JsEnvironment environment,
            RealmState realmState,
            CancellationToken cancellationToken = default,
            ExecutionKind executionKind = ExecutionKind.Script,
            bool createStrictEnvironment = true)
        {
            var context = realmState.CreateContext(
                ScopeKind.Program,
                program.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy,
                false,
                cancellationToken,
                executionKind,
                false);
            context.SourceReference = program.Source;
            context.IsStrictSource = program.IsStrict;
            using var programActivity =
                Activity.Current?.StartEvaluatorActivity("Program", context, program.Source);
            programActivity?.SetTag("js.program.strict", program.IsStrict);
            var executionEnvironment = program.IsStrict && createStrictEnvironment
                ? new JsEnvironment(environment, true, true,
                    treatAsGlobalFunctionScope: environment.IsGlobalFunctionScope)
                : environment;
            if (program.IsStrict && !executionEnvironment.IsStrict)
            {
                executionEnvironment = new JsEnvironment(executionEnvironment, true, true,
                    treatAsGlobalFunctionScope: executionEnvironment.IsGlobalFunctionScope);
            }

            if (executionKind == ExecutionKind.Script)
            {
                executionEnvironment.RealmState?.Engine?.SetGlobalExecutionScope(
                    executionEnvironment.GetFunctionScope());
            }

            var programMode = executionKind == ExecutionKind.Eval
                ? program.IsStrict
                    ? ScopeMode.Strict
                    : context.Options.EnableAnnexBFunctionExtensions
                        ? ScopeMode.SloppyAnnexB
                        : ScopeMode.Sloppy
                : executionEnvironment.IsStrict
                    ? ScopeMode.Strict
                    : context.Options.EnableAnnexBFunctionExtensions
                        ? ScopeMode.SloppyAnnexB
                        : ScopeMode.Sloppy;
            using var programScope = context.PushScope(ScopeKind.Program, programMode);

            var programBlock = new BlockStatement(program.Source, program.Body, program.IsStrict);
            var lexicalNames = CollectLexicalNames(programBlock);
            var topLevelLexicalNames = CollectTopLevelLexicalNames(program.Body);
            var catchParameterNames = CollectCatchParameterNames(programBlock);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(programBlock);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);
            context.BlockedFunctionVarNames = bodyLexicalNames;
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            var functionScope = executionEnvironment.GetFunctionScope();
            if (executionKind != ExecutionKind.Eval)
            {
                functionScope.SetBodyLexicalNames(topLevelLexicalNames);
                if (functionScope.IsGlobalFunctionScope &&
                    functionScope.Enclosing is { IsGlobalFunctionScope: true } enclosingGlobal)
                {
                    enclosingGlobal.SetBodyLexicalNames(topLevelLexicalNames);
                }
            }
            if (functionScope.IsGlobalFunctionScope)
            {
                foreach (var blockedName in bodyLexicalNames)
                {
                    if (functionScope.HasRestrictedGlobalProperty(blockedName))
                    {
                        throw StandardLibrary.ThrowSyntaxError(
                            $"Cannot redeclare var-scoped binding '{blockedName.Name}' with lexical declaration",
                            context,
                            context.RealmState);
                    }
                }
            }

            HoistVarDeclarations(
                programBlock,
                executionEnvironment,
                context,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            var result = EmptyCompletion;
            foreach (var statement in program.Body)
            {
                context.ThrowIfCancellationRequested();
                var completion = EvaluateStatement(statement, executionEnvironment, context);
                if (!ReferenceEquals(completion, EmptyCompletion))
                {
                    result = completion;
                }

                if (context.ShouldStopEvaluation)
                {
                    break;
                }
            }

            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return ReferenceEquals(result, EmptyCompletion) ? Symbol.Undefined : result;
        }
    }

    private static HashSet<Symbol> CollectTopLevelLexicalNames(ImmutableArray<StatementNode> statements)
    {
        var names = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } decl:
                    foreach (var declarator in decl.Declarators)
                    {
                        CollectBindingNames(declarator.Target, names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
            }
        }

        return names;
    }

    private static void CollectBindingNames(BindingTarget target, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    names.Add(identifier.Name);
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingNames(element.Target, names);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingNames(property.Target, names);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
            }

            break;
        }
    }
}
