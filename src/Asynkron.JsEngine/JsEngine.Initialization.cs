using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public sealed partial class JsEngine
{
    private void InitializeCoreGlobals()
    {
        // Bind the global object aliases and standard constructors used across libraries.
        SetGlobal("globalThis", _globalObject);
        SetGlobal("global", _globalObject);

        SetGlobal("console", StandardLibrary.CreateConsoleObject());
        SetGlobal("Math", StandardLibrary.CreateMathObject());
        SetGlobal("Object", StandardLibrary.CreateObjectConstructor(_realm));
        SetGlobal("Function", StandardLibrary.CreateFunctionConstructor(_realm));
        SetGlobal("Number", StandardLibrary.CreateNumberConstructor(_realm));
        var bigIntFunction = StandardLibrary.CreateBigIntFunction(_realm);
        SetGlobal("BigInt", bigIntFunction);
        SetGlobal("Boolean", StandardLibrary.CreateBooleanConstructor(_realm));
        SetGlobal("String", StandardLibrary.CreateStringConstructor(_realm));
        var arrayConstructor = StandardLibrary.CreateArrayConstructor(_realm);
        SetGlobal("Array", arrayConstructor);
        if (arrayConstructor is HostFunction arrayHost)
        {
            arrayHost.RealmState = _realm;
        }
        _globalObject.DefineProperty("Array", new PropertyDescriptor
        {
            Value = arrayConstructor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        _globalObject.DefineProperty("BigInt", new PropertyDescriptor
        {
            Value = bigIntFunction,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        // Common global constants and parsing helpers.
        SetGlobal("Infinity", double.PositiveInfinity);
        SetGlobal("NaN", double.NaN);
        SetGlobal("undefined", Symbols.Undefined);

        SetGlobal("parseInt", StandardLibrary.CreateParseIntFunction());
        SetGlobal("parseFloat", StandardLibrary.CreateParseFloatFunction());
        SetGlobal("isNaN", StandardLibrary.CreateIsNaNFunction());
        SetGlobal("isFinite", StandardLibrary.CreateIsFiniteFunction());

        // Date constructor with static helpers attached to the callable function.
        var dateConstructor = StandardLibrary.CreateDateConstructor(_realm);
        var dateObj = StandardLibrary.CreateDateObject();

        if (dateConstructor is HostFunction hf)
        {
            foreach (var prop in dateObj)
            {
                hf.SetProperty(prop.Key, prop.Value);
            }
        }

        SetGlobal("Date", dateConstructor);
        SetGlobal("JSON", StandardLibrary.CreateJsonObject());
        SetGlobal("RegExp", StandardLibrary.CreateRegExpConstructor());
        SetGlobal("Promise", StandardLibrary.CreatePromiseConstructor(this));
        SetGlobal("Symbol", StandardLibrary.CreateSymbolConstructor());
        SetGlobal("Map", StandardLibrary.CreateMapConstructor());
        SetGlobal("Set", StandardLibrary.CreateSetConstructor());
        SetGlobal("WeakMap", StandardLibrary.CreateWeakMapConstructor());
        SetGlobal("Proxy", StandardLibrary.CreateProxyConstructor(_realm));
        SetGlobal("WeakSet", StandardLibrary.CreateWeakSetConstructor());
        SetGlobal("localStorage", StandardLibrary.CreateLocalStorageObject());
        SetGlobal("Reflect", StandardLibrary.CreateReflectObject());

        // Register eval as an environment-aware callable.
        SetGlobal("eval", new EvalHostFunction(this));
    }

    private void InitializeTypedArrays()
    {
        SetGlobal("ArrayBuffer", StandardLibrary.CreateArrayBufferConstructor());
        SetGlobal("DataView", StandardLibrary.CreateDataViewConstructor());
        SetGlobal("Int8Array", StandardLibrary.CreateInt8ArrayConstructor());
        SetGlobal("Uint8Array", StandardLibrary.CreateUint8ArrayConstructor());
        SetGlobal("Uint8ClampedArray", StandardLibrary.CreateUint8ClampedArrayConstructor());
        SetGlobal("Int16Array", StandardLibrary.CreateInt16ArrayConstructor());
        SetGlobal("Uint16Array", StandardLibrary.CreateUint16ArrayConstructor());
        SetGlobal("Int32Array", StandardLibrary.CreateInt32ArrayConstructor());
        SetGlobal("Uint32Array", StandardLibrary.CreateUint32ArrayConstructor());
        SetGlobal("Float32Array", StandardLibrary.CreateFloat32ArrayConstructor());
        SetGlobal("Float64Array", StandardLibrary.CreateFloat64ArrayConstructor());
        SetGlobal("BigInt64Array", StandardLibrary.CreateBigInt64ArrayConstructor());
        SetGlobal("BigUint64Array", StandardLibrary.CreateBigUint64ArrayConstructor());
    }

    private void InitializeErrorConstructors()
    {
        SetGlobal("Error", StandardLibrary.CreateErrorConstructor(_realm, "Error"));
        SetGlobal("TypeError", StandardLibrary.CreateErrorConstructor(_realm, "TypeError"));
        SetGlobal("RangeError", StandardLibrary.CreateErrorConstructor(_realm, "RangeError"));
        SetGlobal("ReferenceError", StandardLibrary.CreateErrorConstructor(_realm, "ReferenceError"));
        SetGlobal("SyntaxError", StandardLibrary.CreateErrorConstructor(_realm, "SyntaxError"));
    }

    private void InitializeAsyncIterationHelpers()
    {
        SetGlobal("__getAsyncIterator", StandardLibrary.CreateGetAsyncIteratorHelper(this));
        SetGlobal("__iteratorNext", StandardLibrary.CreateIteratorNextHelper(this));
        SetGlobal("__awaitHelper", StandardLibrary.CreateAwaitHelper(this));
    }

    private void InitializeTimersAndImports()
    {
        SetGlobalFunction("setTimeout", SetTimeout);
        SetGlobalFunction("setInterval", SetInterval);
        SetGlobalFunction("clearTimeout", ClearTimer);
        SetGlobalFunction("clearInterval", ClearTimer);
        SetGlobalFunction("import", DynamicImport);
    }
}
