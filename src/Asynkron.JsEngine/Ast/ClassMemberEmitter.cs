#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

internal static class ClassMemberEmitter
{
    public static void DefineMember(this LoweredClassMember member, string propertyName,
        IJsCallable callable,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype)
    {
        if (member.IsStatic && string.Equals(propertyName, "prototype", StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowTypeError("Cannot redefine constructor prototype via static member");
        }

        if (member.Kind == ClassMemberKind.Method)
        {
            member.DefineMethod(propertyName, callable, constructorAccessor, prototype);
            return;
        }

        member.DefineAccessor(propertyName, callable, constructorAccessor, prototype);
    }

    private static void DefineMethod(this LoweredClassMember member, string propertyName,
        IJsCallable callable,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype)
    {
        var descriptor = new PropertyDescriptor
        {
            Value = callable,
            Writable = !member.IsPrivate,
            Enumerable = false,
            Configurable = !member.IsPrivate
        };

        if (member.IsStatic)
        {
            if (constructorAccessor is IJsObjectLike ctorObject)
            {
                ctorObject.DefineProperty(propertyName, descriptor);
            }
            else
            {
                constructorAccessor.SetProperty(propertyName, JsValue.FromObjectUnsafe(callable));
            }

            return;
        }

        prototype.DefineProperty(propertyName, descriptor);
    }

    private static void DefineAccessor(this LoweredClassMember member, string propertyName,
        IJsCallable callable,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype)
    {
        var accessorTarget = member.IsStatic
            ? constructorAccessor as IJsObjectLike
            : prototype;
        if (accessorTarget is not null)
        {
            var descriptor = new PropertyDescriptor { Enumerable = false, Configurable = true };

            switch (member.Kind)
            {
                case ClassMemberKind.Getter:
                    descriptor.Get = callable;
                    break;
                case ClassMemberKind.Setter:
                    descriptor.Set = callable;
                    break;
            }

            accessorTarget.DefineProperty(propertyName, descriptor);
            return;
        }

        constructorAccessor.SetProperty(propertyName, JsValue.FromObjectUnsafe(callable));
    }
}
