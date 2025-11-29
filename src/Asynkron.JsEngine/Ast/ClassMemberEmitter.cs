using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

internal static class ClassMemberEmitter
{
    public static void DefineMember(
        ClassMember member,
        string propertyName,
        IJsCallable callable,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype)
    {
        if (member.Kind == ClassMemberKind.Method)
        {
            DefineMethod(member, propertyName, callable, constructorAccessor, prototype);
            return;
        }

        DefineAccessor(member, propertyName, callable, constructorAccessor, prototype);
    }

    private static void DefineMethod(
        ClassMember member,
        string propertyName,
        IJsCallable callable,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype)
    {
        var descriptor = new PropertyDescriptor
        {
            Value = callable,
            Writable = true,
            Enumerable = false,
            Configurable = true,
            HasValue = true,
            HasWritable = true,
            HasEnumerable = true,
            HasConfigurable = true
        };

        if (member.IsStatic)
        {
            if (constructorAccessor is IJsObjectLike ctorObject)
            {
                ctorObject.DefineProperty(propertyName, descriptor);
            }
            else
            {
                constructorAccessor.SetProperty(propertyName, callable);
            }

            return;
        }

        prototype.DefineProperty(propertyName, descriptor);
    }

    private static void DefineAccessor(
        ClassMember member,
        string propertyName,
        IJsCallable callable,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype)
    {
        var accessorTarget = member.IsStatic
            ? constructorAccessor as IJsObjectLike
            : prototype;
        if (accessorTarget is not null)
        {
            var descriptor = new PropertyDescriptor
            {
                Enumerable = false,
                Configurable = true
            };

            if (member.Kind == ClassMemberKind.Getter)
            {
                descriptor.Get = callable;
            }
            else if (member.Kind == ClassMemberKind.Setter)
            {
                descriptor.Set = callable;
            }

            accessorTarget.DefineProperty(propertyName, descriptor);
            return;
        }

        constructorAccessor.SetProperty(propertyName, callable);
    }
}
