namespace FamilyQuant;

public partial class DeNovoControl
{
    /// <summary>The family source can be the project the shell just saved, or a legacy FCF.</summary>
    public static FamilyClusterFile LoadHomologyCuration(string path) => DeNovoScreenOperations.LoadHomologyCuration(path);
}
