using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

internal static class ClassMemberEmitter
{
    extension(ClassMember member)
    {
        public void DefineMember(string propertyName,
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

        private void DefineMethod(string propertyName,
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

        private void DefineAccessor(string propertyName,
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

            constructorAccessor.SetProperty(propertyName, callable);
        }
    }
}
