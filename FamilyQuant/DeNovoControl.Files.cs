namespace FamilyQuant;

public partial class DeNovoControl
{
    /// <summary>The family source can be the project the shell just saved, or a legacy FCF.</summary>
    public static FamilyClusterFile LoadHomologyCuration(string path) => ProjectStore.IsProjectPath(path)
        ? ProjectStore.ReadFqproj(path).fcf : FamilyClusterFile.Load(path);
}
