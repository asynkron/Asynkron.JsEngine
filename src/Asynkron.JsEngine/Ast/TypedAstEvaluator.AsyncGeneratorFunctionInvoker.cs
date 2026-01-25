#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Callable for async generator functions (async function*).
    /// Returns an async iterator when invoked.
    /// </summary>
    private sealed class AsyncGeneratorFunctionInvoker : GeneratorFunctionBase
    {
        public AsyncGeneratorFunctionInvoker(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment = false,
            bool isConstructorFunction = true)
            : base(function, closure, realmState, isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction)
            => Initialize();

        protected override string FunctionTypeName => "AsyncGeneratorFunction";

        protected override void ValidateFunctionType()
        {
            if (!_function.IsGenerator || !_function.IsAsync)
            {
                throw new ArgumentException("Factory can only wrap async generator functions.", nameof(_function));
            }
        }

        public override JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            var context = CreateInvocationContext(arguments, thisValue);
            var instance = context.CreateAsyncGeneratorInvoker();
            instance.Initialize();
            return (JsValue)instance.CreateAsyncIteratorObject();
        }

        protected override void EnsureIntrinsics()
        {
            var engine = RealmState.Engine ?? throw new InvalidOperationException("Engine reference is missing.");

            // %AsyncIteratorPrototype% (inherits from %Object.prototype%)
            if (RealmState.AsyncIteratorPrototype is null)
            {
                // Use the generated prototype factory to get Symbol.asyncIterator method
                var asyncIteratorProto = (JsObject)AsyncIteratorPrototype.CreatePrototype(RealmState);
                if (RealmState.ObjectPrototype is not null)
                {
                    asyncIteratorProto.SetPrototype(RealmState.ObjectPrototype);
                }

                RealmState.AsyncIteratorPrototype = asyncIteratorProto;
            }

            // %AsyncGeneratorPrototype% (inherits from %AsyncIteratorPrototype%)
            if (RealmState.AsyncGeneratorPrototype is null)
            {
                // Use the generated prototype factory to get all required properties (Symbol.toStringTag, methods, etc.)
                var asyncGenProto = (JsObject)AsyncGeneratorPrototype.CreatePrototype(RealmState);
                asyncGenProto.SetPrototype(RealmState.AsyncIteratorPrototype ?? RealmState.ObjectPrototype);
                RealmState.AsyncGeneratorPrototype = asyncGenProto;
            }

            // %AsyncGeneratorFunction.prototype%
            if (RealmState.AsyncGeneratorFunctionPrototype is null && RealmState.FunctionPrototype is not null)
            {
                var asyncGenFuncProto = new JsObject();
                asyncGenFuncProto.SetPrototype(RealmState.FunctionPrototype);

                // Add Symbol.toStringTag property per ES spec (non-writable, non-enumerable, configurable)
                asyncGenFuncProto.DefineProperty(SymbolKeys.ToStringTag,
                    new PropertyDescriptor { Value = "AsyncGeneratorFunction", Writable = false, Enumerable = false, Configurable = true });

                asyncGenFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = RealmState.AsyncGeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true
                    });

                if (RealmState.AsyncGeneratorFunctionConstructor is null)
                {
                    RealmState.AsyncGeneratorFunctionConstructor =
                        CreateAsyncGeneratorFunctionConstructor(engine, RealmState);
                }

                if (RealmState.AsyncGeneratorFunctionConstructor is { } asyncGenCtor)
                {
                    // AsyncGeneratorFunction.prototype must be non-writable per ES spec
                    asyncGenCtor.DefineProperty("prototype",
                        new PropertyDescriptor
                        {
                            Value = asyncGenFuncProto,
                            Writable = false,
                            Enumerable = false,
                            Configurable = false
                        });
                }

                asyncGenFuncProto.DefineProperty("constructor",
                    new PropertyDescriptor
                    {
                        Value = RealmState.AsyncGeneratorFunctionConstructor,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true
                    });

                RealmState.AsyncGeneratorFunctionPrototype = asyncGenFuncProto;

                // %AsyncGeneratorPrototype%.constructor === %AsyncGenerator%
                // Per ES spec: non-writable, non-enumerable, configurable.
                if (RealmState.AsyncGeneratorPrototype is { } asyncGenProto &&
                    asyncGenProto.GetOwnPropertyDescriptor("constructor") is null)
                {
                    asyncGenProto.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = asyncGenFuncProto,
                            Writable = false,
                            Enumerable = false,
                            Configurable = true
                        });
                }
            }
        }

        protected override IJsPropertyAccessor? GetFunctionPrototype()
        {
            return RealmState.AsyncGeneratorFunctionPrototype;
        }

        protected override JsObject? GetGeneratorPrototype()
        {
            return RealmState.AsyncGeneratorPrototype ?? RealmState.ObjectPrototype;
        }

        protected override void CustomizePrototypeObject(JsObject prototypeObject)
        {
            // Async generators add a constructor property pointing to themselves
            prototypeObject.DefineProperty("constructor",
                new PropertyDescriptor
                {
                    Value = this,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
        }

        private static HostFunction CreateAsyncGeneratorFunctionConstructor(JsEngine engine, RealmState realm)
        {
            HostFunction constructor = null!;

            constructor = new HostFunction((_, args) =>
                CreateDynamicGeneratorFunction(args, constructor, engine, realm, "async function*", realm.AsyncGeneratorFunctionConstructor!))
            { RealmState = realm };

            constructor.SetInvokeWithContext((args, _, _, newTarget) =>
            {
                IJsCallable targetCallable = constructor;
                if (newTarget.TryUnwrap(out IJsCallable? callable))
                {
                    targetCallable = callable;
                }

                return CreateDynamicGeneratorFunction(args, targetCallable, engine, realm, "async function*", realm.AsyncGeneratorFunctionConstructor!);
            });

            StandardLibrary.DefineConstantProperty(constructor, "length", 1d, true);
            StandardLibrary.DefineConstantProperty(constructor, "name", "AsyncGeneratorFunction", true);

            if (realm.FunctionPrototype is { } functionPrototype)
            {
                constructor.Properties.SetPrototype(functionPrototype);
            }

            return constructor;
        }
    }
}
