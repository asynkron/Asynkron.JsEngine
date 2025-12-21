using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.BigIntHelper;
using static Asynkron.JsEngine.StdLib.ObjectHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;
using static Asynkron.JsEngine.StdLib.SymbolHelper;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Object", PrototypeType = typeof(ObjectPrototype), Length = 1d, DisplayName = "Object")]
public sealed partial class ObjectConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    // Static methods registered via code generation
    [JsConstructorMethod("keys", Length = 1d)]
    public static object? Keys(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectKeys(thisArg, args, realm);

    [JsConstructorMethod("values", Length = 1d)]
    public static object? Values(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectValues(thisArg, args, realm);

    [JsConstructorMethod("entries", Length = 1d)]
    public static object? Entries(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectEntries(thisArg, args, realm);

    [JsConstructorMethod("assign", Length = 2d)]
    public static object? Assign(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectAssign(thisArg, args, realm);

    [JsConstructorMethod("fromEntries", Length = 1d)]
    public static object? FromEntries(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectFromEntries(thisArg, args, realm);

    [JsConstructorMethod("hasOwn", Length = 2d)]
    public static object? HasOwn(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectHasOwn(thisArg, args, realm);

    [JsConstructorMethod("freeze", Length = 1d)]
    public static object? Freeze(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectFreeze(thisArg, args, realm);

    [JsConstructorMethod("seal", Length = 1d)]
    public static object? Seal(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectSeal(thisArg, args, realm);

    [JsConstructorMethod("isFrozen", Length = 1d)]
    public static object? IsFrozen(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectIsFrozen(thisArg, args, realm);

    [JsConstructorMethod("isSealed", Length = 1d)]
    public static object? IsSealed(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectIsSealed(thisArg, args, realm);

    [JsConstructorMethod("is", Length = 2d)]
    public static object? Is(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectIs(thisArg, args, realm);

    [JsConstructorMethod("create", Length = 2d)]
    public static object? Create(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectCreate(thisArg, args, realm);

    [JsConstructorMethod("getOwnPropertyNames", Length = 1d)]
    public static object? GetOwnPropertyNames(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectGetOwnPropertyNames(thisArg, args, realm);

    [JsConstructorMethod("getOwnPropertyDescriptor", Length = 2d)]
    public static object? GetOwnPropertyDescriptor(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectGetOwnPropertyDescriptor(thisArg, args, realm);

    [JsConstructorMethod("getOwnPropertyDescriptors", Length = 1d)]
    public static object? GetOwnPropertyDescriptors(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectGetOwnPropertyDescriptors(thisArg, args, realm);

    [JsConstructorMethod("getPrototypeOf", Length = 1d)]
    public static object? GetPrototypeOf(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectGetPrototypeOf(thisArg, args, realm);

    [JsConstructorMethod("defineProperty", Length = 3d)]
    public static object? DefineProperty(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectDefineProperty(thisArg, args, realm);

    [JsConstructorMethod("defineProperties", Length = 2d)]
    public static object? DefineProperties(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectDefineProperties(thisArg, args, realm);

    [JsConstructorMethod("setPrototypeOf", Length = 2d)]
    public static object? SetPrototypeOf(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectSetPrototypeOf(thisArg, args, realm);

    [JsConstructorMethod("preventExtensions", Length = 1d)]
    public static object? PreventExtensions(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectPreventExtensions(thisArg, args, realm);

    [JsConstructorMethod("isExtensible", Length = 1d)]
    public static object? IsExtensible(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectIsExtensible(thisArg, args, realm);

    [JsConstructorMethod("getOwnPropertySymbols", Length = 1d)]
    public static object? GetOwnPropertySymbols(object? thisArg, IReadOnlyList<JsValue> args, RealmState? realm) =>
        ObjectGetOwnPropertySymbols(thisArg, args, realm);

    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, targetCtor);
            return JsValue.FromObjectUnsafe(ConstructCore(args, targetCtor, constructing));
        }

        return JsValue.FromObjectUnsafe(ConstructCore(args, targetCtor, null));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ObjectPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            if (newTarget.TryGetObject<IJsCallable>(out var newTargetCallable))
            {
                return JsValue.FromObjectUnsafe(ConstructCore(args, newTargetCallable!, null));
            }
            return JsValue.FromObjectUnsafe(ConstructCore(args, target, null));
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
        AttachPrototypeShortcut(constructor);
    }

    private object ConstructCore(IReadOnlyList<JsValue> args, IJsCallable newTarget, JsObject? existing)
    {
        if (args.Count == 0 || args[0].IsUndefined || args[0].IsNull)
        {
            return CreateBlank(newTarget, existing);
        }

        var value = args[0];

        // Check if it's a TypedAstSymbol (stored in ObjectValue when Kind is Symbol)
        if (value.IsSymbol && value.ObjectValue is TypedAstSymbol typedSym)
        {
            return CreateSymbolWrapper(typedSym, realm: Realm);
        }
        if (value.TryGetBigInt(out var bigInt))
        {
            return CreateBigIntWrapper(bigInt!, realm: Realm);
        }
        if (value.TryGetBoolean(out var boolValue))
        {
            return BooleanHelper.CreateBooleanWrapper(boolValue, realm: Realm);
        }
        if (value.TryGetString(out var strValue))
        {
            return StringHelper.CreateStringWrapper(strValue!, realm: Realm);
        }
        if (value.TryGetDouble(out var numValue))
        {
            return NumberHelper.CreateNumberWrapper(numValue, realm: Realm);
        }
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor!;
        }

        return CreateBlank(newTarget, existing);
    }

    private JsObject CreateBlank(IJsCallable newTarget, JsObject? existing)
    {
        var targetCtor = _constructor ?? newTarget;
        var obj = existing ?? PrepareThisObject(JsValue.Undefined, assignPrototype: false);
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        if (proto is not null && obj.Prototype is null)
        {
            obj.SetPrototype(proto);
        }

        obj.RealmState ??= Realm;
        return obj;
    }

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }
    }

    private void AttachPrototypeShortcut(HostFunction constructor)
    {
        if (Prototype.TryGetProperty("hasOwnProperty", out var hasOwn))
        {
            constructor.SetProperty("hasOwnProperty", hasOwn);
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Object constructor not initialized");
}
