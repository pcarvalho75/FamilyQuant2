using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace FamilyQuant;

/// <summary>The one list of curation format-2 parts. Every class in this assembly that derives from
/// <see cref="CurationFormatPart"/> and carries <see cref="CurationFormatPartAttribute"/> is a part; the list is
/// ordered by <see cref="CurationFormatPartAttribute.Order"/>, then by name. A part registers itself by existing,
/// so two parts never edit the same file.</summary>
public static class CurationFormatRegistry
{
    private static readonly Regex PartName = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Lazy<IReadOnlyList<CurationFormatPart>> registered = new(() => Discover(AssemblyTypes()));

    private static IEnumerable<Type> AssemblyTypes()
    {
        try { return typeof(CurationFormatPart).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException partial) { return partial.Types.OfType<Type>(); }
    }

    /// <summary>The parts this build writes and can read, in order.</summary>
    public static IReadOnlyList<CurationFormatPart> Parts => registered.Value;

    /// <summary>Builds the part list from the given types: each concrete <see cref="CurationFormatPart"/> must carry
    /// the attribute and a parameterless constructor, and names must be unique. Public so a test can build a list
    /// from its own synthetic parts.</summary>
    public static IReadOnlyList<CurationFormatPart> Discover(IEnumerable<Type> types)
    {
        var parts = new List<CurationFormatPart>();
        foreach (Type type in types.Where(t => typeof(CurationFormatPart).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var mark = type.GetCustomAttribute<CurationFormatPartAttribute>()
                ?? throw new InvalidOperationException(type.FullName + " derives from CurationFormatPart but has no [CurationFormatPart].");
            if (!PartName.IsMatch(mark.Name))
                throw new InvalidOperationException(type.FullName + " has the part name \"" + mark.Name
                    + "\"; use lower-case letters, digits and single hyphens.");
            var part = (CurationFormatPart)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException(type.FullName + " could not be created."));
            part.Name = mark.Name; part.Order = mark.Order;
            parts.Add(part);
        }
        var duplicate = parts.GroupBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException("Two curation format parts are named \"" + duplicate.Key + "\": "
                + string.Join(", ", duplicate.Select(p => p.GetType().FullName)) + ".");
        return parts.OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Options for the sections: the shared curation options, without any part's converters.</summary>
    internal static JsonSerializerOptions SectionOptions { get; } = new(FamilyClusterFile.CurationJsonOptions);

    /// <summary>Options for one format-2 write or read: the shared curation options (so indentation and every
    /// other choice follow <see cref="FamilyClusterFile.CurationJsonOptions"/>), plus the active parts' converters
    /// and type changes, bound to this operation's context.</summary>
    internal static JsonSerializerOptions CreateOptions(CurationFormatContext context, IReadOnlyList<CurationFormatPart> active)
    {
        var options = new JsonSerializerOptions(FamilyClusterFile.CurationJsonOptions);
        var resolver = new DefaultJsonTypeInfoResolver();
        foreach (CurationFormatPart part in active)
        {
            foreach (var converter in part.CreateConverters(context)) options.Converters.Add(converter);
            resolver.Modifiers.Add(info => part.ModifyType(info, context));
        }
        options.TypeInfoResolver = resolver;
        return options;
    }
}
