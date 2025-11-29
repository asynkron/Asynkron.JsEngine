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
            using var programActivity = StartEvaluatorActivity("Program", context, program.Source);
            programActivity?.SetTag("js.program.strict", program.IsStrict);
            var executionEnvironment = program.IsStrict && createStrictEnvironment
                ? new JsEnvironment(environment, true, true)
                : environment;
            if (program.IsStrict && !executionEnvironment.IsStrict)
            {
                executionEnvironment = new JsEnvironment(executionEnvironment, true, true);
            }

            var programMode = executionEnvironment.IsStrict
                ? ScopeMode.Strict
                : context.Options.EnableAnnexBFunctionExtensions
                    ? ScopeMode.SloppyAnnexB
                    : ScopeMode.Sloppy;
            using var programScope = context.PushScope(ScopeKind.Program, programMode);

            var programBlock = new BlockStatement(program.Source, program.Body, program.IsStrict);
            var lexicalNames = CollectLexicalNames(programBlock);
            var catchParameterNames = CollectCatchParameterNames(programBlock);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(programBlock);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);
            context.BlockedFunctionVarNames = bodyLexicalNames;
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            var functionScope = executionEnvironment.GetFunctionScope();
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
}
