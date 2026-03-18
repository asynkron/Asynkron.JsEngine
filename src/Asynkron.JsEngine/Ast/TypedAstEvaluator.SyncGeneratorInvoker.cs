#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Callable for synchronous generator functions (function*).
    /// Returns a sync iterator when invoked.
    /// </summary>
    private sealed class SyncGeneratorInvoker : GeneratorFunctionBase
    {
        public SyncGeneratorInvoker(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment = false,
            bool isConstructorFunction = true)
            : base(function, closure, realmState, isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction)
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
            var runner = CreateRunner(arguments, thisValue);
            runner.Initialize();
            return (JsValue)runner.CreateGeneratorObject();
        }

        protected override void EnsureIntrinsics()
        {
            // Lazily initialize the GeneratorFunction.prototype and Generator.prototype intrinsics
            // if they haven't been created yet.

            // First create %GeneratorPrototype% if needed
            if (RealmState.GeneratorPrototype is null)
            {
                // Use the generated prototype factory to get all required properties
                // (Symbol.toStringTag, next/return/throw methods with proper descriptors, etc.)
                var generatorProto = (JsObject)GeneratorPrototype.CreatePrototype(RealmState);
                // %GeneratorPrototype% inherits from %IteratorPrototype%, which inherits from %Object.prototype%
                var iteratorPrototype = RealmState.IteratorPrototype ??=
                    (JsObject)IteratorPrototype.CreatePrototype(RealmState);
                generatorProto.SetPrototype(iteratorPrototype);

                RealmState.GeneratorPrototype = generatorProto;
            }

            // Then create %GeneratorFunction.prototype% and link it to %GeneratorPrototype%
            if (RealmState.GeneratorFunctionPrototype is null && RealmState.FunctionPrototype is not null)
            {
                var genFuncProto = new JsObject();
                genFuncProto.SetPrototype(RealmState.FunctionPrototype);

                // Add Symbol.toStringTag property per ES spec (non-writable, non-enumerable, configurable)
                genFuncProto.DefineProperty(SymbolKeys.ToStringTag,
                    new PropertyDescriptor { Value = "GeneratorFunction", Writable = false, Enumerable = false, Configurable = true });

                // %GeneratorFunction.prototype% should have a .prototype property pointing to %GeneratorPrototype%
                genFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = RealmState.GeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true
                    });

                RealmState.GeneratorFunctionPrototype = genFuncProto;

                // %GeneratorPrototype%.constructor === %GeneratorFunction.prototype%
                // Per ES spec: non-writable, non-enumerable, configurable.
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

                // Create the GeneratorFunction constructor if we have access to the engine
                if (RealmState is { Engine: { } engine, GeneratorFunctionConstructor: null })
                {
                    var generatorFunctionConstructor = CreateGeneratorFunctionConstructor(engine, RealmState);
                    RealmState.GeneratorFunctionConstructor = generatorFunctionConstructor;

                    // Set GeneratorFunctionPrototype.constructor = GeneratorFunction
                    genFuncProto.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = generatorFunctionConstructor,
                            Writable = false,
                            Enumerable = false,
                            Configurable = true
                        });

                    // GeneratorFunction.__proto__ === Function (inherit from Function)
                    if (RealmState.FunctionPrototype is { } functionPrototype)
                    {
                        generatorFunctionConstructor.Properties.SetPrototype(functionPrototype);
                    }

                    // GeneratorFunction.prototype must be non-writable per ES spec
                    generatorFunctionConstructor.DefineProperty("prototype",
                        new PropertyDescriptor
                        {
                            Value = genFuncProto,
                            Writable = false,
                            Enumerable = false,
                            Configurable = false
                        });
                }
            }
        }

        protected override IJsPropertyAccessor? GetFunctionPrototype()
        {
            return RealmState.GeneratorFunctionPrototype;
        }

        protected override JsObject? GetGeneratorPrototype()
        {
            return RealmState.GeneratorPrototype;
        }

        private static HostFunction CreateGeneratorFunctionConstructor(JsEngine engine, RealmState realm)
        {
            HostFunction generatorFunctionConstructor = null!;

            generatorFunctionConstructor = new HostFunction((_, args) =>
                CreateDynamicGeneratorFunction(args, generatorFunctionConstructor, engine, realm, "function*", realm.GeneratorFunctionConstructor!))
            {
                RealmState = realm
            };

            generatorFunctionConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
            {
                IJsCallable targetCallable = generatorFunctionConstructor;
                if (newTarget.TryUnwrap(out IJsCallable? callable))
                {
                    targetCallable = callable;
                }

                return CreateDynamicGeneratorFunction(args, targetCallable, engine, realm, "function*", realm.GeneratorFunctionConstructor!);
            });

            StandardLibrary.DefineConstantProperty(generatorFunctionConstructor, "length", 1d, true);
            StandardLibrary.DefineConstantProperty(generatorFunctionConstructor, "name", "GeneratorFunction", true);

            return generatorFunctionConstructor;
        }
    }
}
