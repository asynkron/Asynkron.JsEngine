#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

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
        {
            if (!function.IsGenerator || !function.IsAsync)
            {
                throw new ArgumentException("Factory can only wrap async generator functions.", nameof(function));
            }

            InitializeProperties();
        }

        protected override string FunctionTypeName => "AsyncGeneratorFunction";

        public override JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            var instance = new AsyncGeneratorInvoker(
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
                _capturedPrivateNameScopes);
            instance.Initialize();
            return (JsValue)instance.CreateAsyncIteratorObject();
        }

        protected override void EnsureIntrinsics()
        {
            var engine = RealmState.Engine ?? throw new InvalidOperationException("Engine reference is missing.");

            // %AsyncIteratorPrototype% (inherits from %Object.prototype%)
            if (RealmState.AsyncIteratorPrototype is null)
            {
                var asyncIteratorProto = new JsObject();
                if (RealmState.ObjectPrototype is not null)
                {
                    asyncIteratorProto.SetPrototype(RealmState.ObjectPrototype);
                }

                RealmState.AsyncIteratorPrototype = asyncIteratorProto;
            }

            // %AsyncGeneratorPrototype% (inherits from %AsyncIteratorPrototype%)
            if (RealmState.AsyncGeneratorPrototype is null)
            {
                var asyncGenProto = new JsObject();
                asyncGenProto.SetPrototype(RealmState.AsyncIteratorPrototype ?? RealmState.ObjectPrototype);
                RealmState.AsyncGeneratorPrototype = asyncGenProto;
            }

            // %AsyncGeneratorFunction.prototype%
            if (RealmState.AsyncGeneratorFunctionPrototype is null && RealmState.FunctionPrototype is not null)
            {
                var asyncGenFuncProto = new JsObject();
                asyncGenFuncProto.SetPrototype(RealmState.FunctionPrototype);

                asyncGenFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = RealmState.AsyncGeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });

                if (RealmState.AsyncGeneratorFunctionConstructor is null)
                {
                    RealmState.AsyncGeneratorFunctionConstructor =
                        CreateAsyncGeneratorFunctionConstructor(engine, RealmState);
                }

                if (RealmState.AsyncGeneratorFunctionConstructor is { } asyncGenCtor)
                {
                    asyncGenCtor.SetProperty("prototype", (JsValue)asyncGenFuncProto);
                }

                asyncGenFuncProto.DefineProperty("constructor",
                    new PropertyDescriptor
                    {
                        Value = RealmState.AsyncGeneratorFunctionConstructor,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });

                RealmState.AsyncGeneratorFunctionPrototype = asyncGenFuncProto;
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
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
        }

        private static HostFunction CreateAsyncGeneratorFunctionConstructor(JsEngine engine, RealmState realm)
        {
            HostFunction constructor = null!;

            constructor = new HostFunction((_, args) =>
                AsyncGeneratorFunctionConstructorBody(args, constructor, engine, realm)) { RealmState = realm };

            constructor.SetInvokeWithContext((args, _, _, newTarget) =>
            {
                IJsCallable targetCallable = constructor;
                if (newTarget.TryUnwrap(out IJsCallable? callable))
                {
                    targetCallable = callable;
                }

                return AsyncGeneratorFunctionConstructorBody(args, targetCallable, engine, realm);
            });

            StandardLibrary.DefineConstantProperty(constructor, "length", 1d, true);
            StandardLibrary.DefineConstantProperty(constructor, "name", "AsyncGeneratorFunction", true);

            if (realm.FunctionPrototype is { } functionPrototype)
            {
                constructor.Properties.SetPrototype(functionPrototype);
            }

            return constructor;
        }

        private static JsValue AsyncGeneratorFunctionConstructorBody(
            IReadOnlyList<JsValue> args,
            IJsCallable newTarget,
            JsEngine engine,
            RealmState realm)
        {
            var evalContext = realm.CreateContext();
            var argCount = args.Count;
            var bodyValue = argCount > 0 ? args[argCount - 1] : (JsValue)string.Empty;
            var parameterCount = Math.Max(argCount - 1, 0);

            var parameters = new string[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                var paramText = ToFunctionArgumentString(args[i], evalContext, realm);
                parameters[i] = paramText;
            }

            var bodySource = ToFunctionArgumentString(bodyValue, evalContext, realm);
            var paramList = string.Join(',', parameters);
            var functionSource = $"(async function* anonymous({paramList}\n) {{\n{bodySource}\n}})";

            var scriptGoalOptions = new JsEngineOptions { AllowImportMeta = false };

            ProgramNode program;
            try
            {
                program = engine.ParseProgram(functionSource, options: scriptGoalOptions);
            }
            catch (ParseException parseException)
            {
                var message = parseException.Message ?? "SyntaxError";
                throw new ThrowSignal(StandardLibrary.CreateSyntaxError(message, evalContext, realm));
            }

            var createdObj = engine.ExecuteProgram(
                program,
                engine.GlobalEnvironment,
                CancellationToken.None);

            var created = JsValue.FromObjectUnsafe(createdObj);

            if (created.TryUnwrap(out IJsObjectLike? objectLike))
            {
                var proto = ReflectHelper.ResolveConstructPrototype(
                    newTarget,
                    realm.AsyncGeneratorFunctionConstructor!,
                    realm);
                if (proto is not null)
                {
                    objectLike.SetPrototype(proto);
                }
            }

            return created;
        }
    }
}
