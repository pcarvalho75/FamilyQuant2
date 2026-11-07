using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant;

/// <summary>Marks one part of curation format 2. Each part is a sealed class in its own file under
/// Storage/CurationFormat2/; <see cref="CurationFormatRegistry"/> finds it, so adding a part never edits another part's
/// file or a shared list. The name is written in every file the part shapes and must never change once released.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CurationFormatPartAttribute : Attribute
{
    public CurationFormatPartAttribute(string name, int order) { Name = name; Order = order; }

    /// <summary>Lower-case letters, digits and hyphens, for example "feature-provenance".</summary>
    public string Name { get; }

    /// <summary>Write and read order among the parts; ties are broken by name.</summary>
    public int Order { get; }
}

/// <summary>One part of curation format 2: how one kind of stored data is written smaller and rebuilt on read.
/// A part instance is shared by every read and write, so it keeps no state of its own; whatever one operation
/// needs lives in its <see cref="CurationFormatContext"/>. A part only runs when it is active: on write every
/// registered part is active, on read only the parts the file lists, so a file written before a part existed
/// is read without it.</summary>
public abstract class CurationFormatPart
{
    public string Name { get; internal set; } = string.Empty;
    public int Order { get; internal set; }

    /// <summary>Before writing: look at the file and put any shared table into the context with
    /// <see cref="CurationFormatContext.SetSection{T}"/>. The section is written once, before the curation.
    /// Must not change the file. The header (marker, notice, parts and every section together) must stay under
    /// <see cref="CurationFormat.MaxHeaderBytes"/> (64 MB): a reader refuses a larger one, so the writer refuses to
    /// write it and names the part with the largest section. Pool small records here, never bulk data.</summary>
    public virtual void PrepareWrite(FamilyClusterFile file, CurationFormatContext context) { }

    /// <summary>Converters for the curation body, bound to this one write or read. Convert an item type (a panel
    /// entry, an evidence record), never <see cref="FamilyClusterFile"/> or a list that holds most of the file: a
    /// converter's value is held whole in memory on read and on write, and both must stay streaming.</summary>
    public virtual IEnumerable<JsonConverter> CreateConverters(CurationFormatContext context) => Array.Empty<JsonConverter>();

    /// <summary>Contract changes for the curation body, for example a property left out or renamed, applied to
    /// the type metadata of this one write or read. Prefer this to a converter when the shape stays an object.</summary>
    public virtual void ModifyType(JsonTypeInfo typeInfo, CurationFormatContext context) { }

    /// <summary>After reading the curation: rebuild what the part left out (shared records, copies, derived
    /// hashes). Runs in registry order, before the load finishes.</summary>
    public virtual void CompleteRead(FamilyClusterFile file, CurationFormatContext context) { }
}

/// <summary>The state of one format-2 write or read, shared by the active parts.</summary>
public sealed class CurationFormatContext
{
    private readonly Dictionary<string, JsonElement> sections;
    private readonly HashSet<string> active;

    internal CurationFormatContext(int format, bool writing, IEnumerable<string> activeParts,
        IReadOnlyDictionary<string, JsonElement>? sections = null)
    {
        Format = format; Writing = writing;
        ActiveParts = activeParts.ToArray();
        active = new HashSet<string>(ActiveParts, StringComparer.Ordinal);
        this.sections = sections == null ? new(StringComparer.Ordinal) : new(sections, StringComparer.Ordinal);
    }

    /// <summary>The curation format being written or read.</summary>
    public int Format { get; }

    /// <summary>True while writing, false while reading.</summary>
    public bool Writing { get; }

    /// <summary>The parts this operation applies, in registry order.</summary>
    public IReadOnlyList<string> ActiveParts { get; }

    public bool IsActive(string partName) => active.Contains(partName);

    /// <summary>Scratch space for one operation, for example a lookup a part builds from its section.
    /// Key it by the part's name.</summary>
    public Dictionary<string, object?> State { get; } = new(StringComparer.Ordinal);

    /// <summary>While writing: store the part's shared table. It is written once in the file's Sections,
    /// under the part's name, before the curation.</summary>
    public void SetSection<T>(CurationFormatPart part, T value)
    {
        Require(part);
        if (!Writing) throw new InvalidOperationException("A section is set while writing, not while reading.");
        sections[part.Name] = JsonSerializer.SerializeToElement(value, CurationFormatRegistry.SectionOptions);
    }

    /// <summary>While reading: the part's shared table as the file stored it, or false when the file has none.</summary>
    public bool TryGetSection<T>(CurationFormatPart part, out T? value)
    {
        Require(part);
        if (sections.TryGetValue(part.Name, out JsonElement element))
        {
            value = element.Deserialize<T>(CurationFormatRegistry.SectionOptions);
            return true;
        }
        value = default;
        return false;
    }

    internal Dictionary<string, JsonElement> Sections => sections;

    private void Require(CurationFormatPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (!IsActive(part.Name)) throw new InvalidOperationException("The part " + part.Name + " is not active in this operation.");
    }
}
