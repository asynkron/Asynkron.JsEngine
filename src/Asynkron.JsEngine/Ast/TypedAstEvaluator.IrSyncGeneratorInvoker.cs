using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Runtime;

#pragma warning disable CS0618 // IR runner remains the compatibility route for non-resumable sync generator bodies.

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Callable for synchronous generator bodies that are not yet eligible for the
    /// production resumable VM. The route is selected before invocation, so
    /// SyncGeneratorInvoker remains the production-resumable owner only.
    /// </summary>
    private sealed class IrSyncGeneratorInvoker : GeneratorFunctionBase
    {
        public IrSyncGeneratorInvoker(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment = false,
            bool isConstructorFunction = true,
            FunctionExecutionPlanSeed planSeed = default)
            : base(function, closure, realmState, isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction, planSeed)
            => Initialize();

        protected override string FunctionTypeName => "GeneratorFunction";

        protected override void ValidateFunctionType()
        {
            if (!_function.IsGenerator)
            {
                throw new ArgumentException("Factory can only wrap generator functions.", nameof(_function));
            }
        }

        public override JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            var runner = new ExecutionPlanRunner(
                _function,
                _closure,
                arguments,
                thisValue,
                this,
                RealmState,
                _isLexicallyStrict,
                _hasFunctionNameEnvironment,
                _homeObject,
                PrivateNameScope,
                _capturedPrivateNameScopes,
                planOverride: _planSeed.Plan,
                planFailureOverride: _planSeed.Failure);
            runner.Initialize();
            return (JsValue)runner.CreateGeneratorObject();
        }

        protected override void EnsureIntrinsics()
        {
            // Share the synchronous generator intrinsic graph with the production
            // resumable callable so both generator routes expose the same shape.
            if (RealmState.GeneratorPrototype is null)
            {
                var generatorProto = (JsObject)StdLib.GeneratorPrototype.CreatePrototype(RealmState);
                var iteratorPrototype = RealmState.IteratorPrototype ??=
                    (JsObject)StdLib.IteratorPrototype.CreatePrototype(RealmState);
                generatorProto.SetPrototype(iteratorPrototype);
                RealmState.GeneratorPrototype = generatorProto;
            }

            if (RealmState.GeneratorFunctionPrototype is null && RealmState.FunctionPrototype is not null)
            {
                var genFuncProto = new JsObject();
                genFuncProto.SetPrototype(RealmState.FunctionPrototype);
                genFuncProto.DefineProperty(SymbolKeys.ToStringTag,
                    new PropertyDescriptor { Value = "GeneratorFunction", Writable = false, Enumerable = false, Configurable = true });
                genFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = RealmState.GeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true
                    });

                RealmState.GeneratorFunctionPrototype = genFuncProto;
                if (RealmState.GeneratorPrototype is { } generatorProto &&
                    generatorProto.GetOwnPropertyDescriptor("constructor") is null)
                {
                    generatorProto.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = genFuncProto,
                            Writable = false,
                            Enumerable = false,
                            Configurable = true
                        });
                }
            }
        }

        protected override IJsPropertyAccessor? GetFunctionPrototype() => RealmState.GeneratorFunctionPrototype;

        protected override JsObject? GetGeneratorPrototype() => RealmState.GeneratorPrototype;
    }
}
