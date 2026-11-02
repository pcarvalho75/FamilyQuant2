using System;
using System.Reflection;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ProjectFormatStageTests()
    {
        Type? schema = typeof(ProjectStore).Assembly.GetType("FamilyQuant.ProjectFileSchema");
        Check("project format has one explicit versioned schema contract", schema != null);
        if (schema != null)
            Check("the schema keeps the existing current format version",
                (int)schema.GetField("CurrentFormatVersion", BindingFlags.Static | BindingFlags.NonPublic)!
                    .GetRawConstantValue()! == ProjectStore.CurrentFormatVersion);
    }
}
