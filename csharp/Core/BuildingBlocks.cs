using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Core;

/// The building blocks steps can call: the public instance methods of an actions class, named the way steps
/// write them (GroupSizes is group_sizes). Static methods are helpers, not building blocks.
public partial class BuildingBlocks
{
    readonly Dictionary<string, MethodInfo> methods = [];

    public BuildingBlocks(Type actionsType)
    {
        foreach (MethodInfo method in actionsType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            methods[StepName(method.Name)] = method;
        }
    }

    public bool Has(string name) => methods.ContainsKey(name);

    public IEnumerable<string> Names => methods.Keys;

    public ParameterInfo[] Parameters(string name) => methods[name].GetParameters();

    public string Signature(string name) => $"{name}({string.Join(", ", Parameters(name).Select(ParameterName))})";

    /// A parameter's name the way steps write it (thenTrigger is then_trigger).
    public static string ParameterName(ParameterInfo parameter) => StepName(parameter.Name!);

    /// "shuffle takes 1 argument, got 2 (expected shuffle(zone))", or null if the count is right.
    public string? ArgumentCountProblem(string name, int given)
    {
        int most = Parameters(name).Length;
        int fewest = Parameters(name).Count(parameter => !parameter.HasDefaultValue);
        if (given >= fewest && given <= most)
        {
            return null;
        }
        string expected = fewest == most ? most.ToString() : $"{fewest} to {most}";
        string noun = expected == "1" ? "argument" : "arguments";
        return $"{name} takes {expected} {noun}, got {given} (expected {Signature(name)})";
    }

    /// Calls the building block, with left-out arguments at their defaults, and gives back a step value
    /// (whole numbers as long, lists as List<object?>).
    public object? Call(object actions, string name, List<object?> args)
    {
        ParameterInfo[] parameters = Parameters(name);
        object?[] filled = args.Concat(parameters.Skip(args.Count).Select(parameter => parameter.DefaultValue)).ToArray();
        return ToStepValue(methods[name].Invoke(actions, filled));
    }

    static object? ToStepValue(object? result) => result switch
    {
        int whole => (long)whole,
        float number => (double)number,
        List<object?> or string or IDictionary => result,
        IEnumerable items => items.Cast<object?>().Select(ToStepValue).ToList(),
        _ => result,
    };

    static string StepName(string methodName) => CapitalInside().Replace(methodName, "_$1").ToLowerInvariant();

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex CapitalInside();
}
