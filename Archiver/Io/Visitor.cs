namespace Archiver.Io;

public interface IVisitorHandler
{
    public DirectoryVisitState OnDirectory(DirectoryInfo directory)
    {
        return DirectoryVisitState.Continue;
    }

    /// only called
    /// if <see cref="OnDirectory(DirectoryInfo)"/>
    /// returned <see cref="DirectoryVisitState.Continue"/>
    /// and children visit is successful.
    public void OnDirectoryExit(DirectoryInfo directory)
    {
        // no-op
    }

    public void OnFile(FileInfo file)
    {
        // no-op
    }

    public enum DirectoryVisitState
    {
        Continue,
        SkipSubsTree
    }
}

public class Visitor<T>(DirectoryInfo root, T handler)
    where T : IVisitorHandler
{
    public T Visit()
    {
        if (root.Exists)
        {
            DoVisit(root);
        }

        return handler;
    }

    private void DoVisit(DirectoryInfo dir)
    {
        if (handler.OnDirectory(dir) == IVisitorHandler.DirectoryVisitState.SkipSubsTree)
        {
            return;
        }

        foreach (var child in dir.EnumerateFileSystemInfos())
        {
            switch (child)
            {
                case FileInfo f:
                    handler.OnFile(f);
                    break;
                case DirectoryInfo d:
                    DoVisit(d);
                    break;
                default:
                    throw new ArgumentException($"Unknown type: {child.GetType()}");
            }
        }

        handler.OnDirectoryExit(dir);
    }
}
