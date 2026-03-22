using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution.Instructions;

internal abstract record BindingTargetProgram
{
    public abstract void CollectSymbols(ICollection<Symbol> names);

    protected abstract string DescribeCore();

    public override string ToString() => DescribeCore();
}

internal sealed record IdentifierBindingTargetProgram(Symbol Name) : BindingTargetProgram
{
    public override void CollectSymbols(ICollection<Symbol> names)
    {
        names.Add(Name);
    }

    protected override string DescribeCore() => Name.Name;
}

internal sealed record ArrayBindingTargetProgram(
    ImmutableArray<ArrayBindingElementProgram> Elements,
    BindingTargetProgram? RestElement) : BindingTargetProgram
{
    public override void CollectSymbols(ICollection<Symbol> names)
    {
        foreach (var element in Elements)
        {
            element.Target?.CollectSymbols(names);
        }

        RestElement?.CollectSymbols(names);
    }

    protected override string DescribeCore()
    {
        var parts = new List<string>(Elements.Length + (RestElement is null ? 0 : 1));
        foreach (var element in Elements)
        {
            parts.Add(element.ToString());
        }

        if (RestElement is not null)
        {
            parts.Add($"...{RestElement}");
        }

        return $"[{string.Join(", ", parts)}]";
    }
}

internal sealed record ArrayBindingElementProgram(
    BindingTargetProgram? Target,
    ExpressionProgram? DefaultProgram = null,
    bool DefaultInfersName = false)
{
    public override string ToString()
    {
        if (Target is null)
        {
            return string.Empty;
        }

        return DefaultProgram is null
            ? Target.ToString()
            : $"{Target} = <expr>";
    }
}

internal sealed record ObjectBindingTargetProgram(
    ImmutableArray<ObjectBindingPropertyProgram> Properties,
    BindingTargetProgram? RestElement) : BindingTargetProgram
{
    public override void CollectSymbols(ICollection<Symbol> names)
    {
        foreach (var property in Properties)
        {
            property.Target.CollectSymbols(names);
        }

        RestElement?.CollectSymbols(names);
    }

    protected override string DescribeCore()
    {
        var parts = new List<string>(Properties.Length + (RestElement is null ? 0 : 1));
        foreach (var property in Properties)
        {
            parts.Add(property.ToString());
        }

        if (RestElement is not null)
        {
            parts.Add($"...{RestElement}");
        }

        return $"{{{string.Join(", ", parts)}}}";
    }
}

internal sealed record ObjectBindingPropertyProgram(
    string Name,
    BindingTargetProgram Target,
    ExpressionProgram? DefaultProgram = null,
    ExpressionProgram? NameProgram = null,
    bool DefaultInfersName = false)
{
    public override string ToString()
    {
        var propertyName = NameProgram is null ? Name : "[computed]";
        if (Target is IdentifierBindingTargetProgram identifier && identifier.Name.Name == Name && NameProgram is null &&
            DefaultProgram is null)
        {
            return propertyName;
        }

        return DefaultProgram is null
            ? $"{propertyName}: {Target}"
            : $"{propertyName}: {Target} = <expr>";
    }
}
