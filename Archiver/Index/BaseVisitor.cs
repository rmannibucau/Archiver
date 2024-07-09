using System.CommandLine;
using Archiver.Io;
using Lucene.Net.Documents;
using Lucene.Net.Documents.Extensions;
using Lucene.Net.Index;

namespace Archiver.Index;

public abstract class BaseVisitor(string Root, IndexWriter Index, IConsole Console)
    : IVisitorHandler
{
    protected static readonly HashSet<string> ForbiddenExtensions =
    [
        ".p12",
        ".pem",
        ".jks",
        ".map",
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".tiff",
        ".giff",
        ".exe",
        ".cache",
        ".svg",
        ".iml",
        ".ipr",
        ".iws"
    ];
    protected static readonly HashSet<string> TextSuffixes = ["rc"];
    protected static readonly HashSet<string> TextPrefixes = ["Dockerfile", "Makefile"];
    protected static readonly HashSet<string> TextExtensions =
    [
        ".txt",
        ".adoc",
        ".asciidoc",
        ".md",
        ".markdown",
        ".rst",
        ".java",
        ".cs",
        ".h",
        ".c",
        ".hpp",
        ".cpp",
        ".hxx",
        ".cxx",
        ".rb",
        ".csx",
        ".js",
        ".jsx",
        ".ts",
        ".html",
        ".scala",
        ".rs",
        ".py",
        ".properties",
        ".csproj",
        ".xml",
        ".xsd",
        ".xslt",
        ".json",
        ".yaml",
        ".yml",
        ".rc",
        ".sh",
        ".csv",
        ".sh"
    ];
    protected static readonly HashSet<string> ForbiddenFiles =
    [
        "package-lock.json",
        ".project",
        ".classpath",
        ".yemrc",
        ".sdkman",
        ".gitignore"
    ];
    protected static readonly HashSet<string> ForbiddenDirectories =
    [
        ".env",
        ".git",
        ".idea",
        ".settings",
        ".vscode",
        "target"
    ];

    protected const int BulkSize = 100;
    protected int bulkCounter = 0;

    public virtual IVisitorHandler.DirectoryVisitState OnDirectory(DirectoryInfo directory)
    {
        if (ForbiddenDirectories.Contains(directory.Name))
        {
            return IVisitorHandler.DirectoryVisitState.SkipSubsTree;
        }
        return IVisitorHandler.DirectoryVisitState.Continue;
    }

    public virtual void OnDirectoryExit(DirectoryInfo directory)
    {
        // no-op
    }

    public virtual void OnFile(FileInfo file)
    {
        if (!ForbiddenFiles.Contains(file.Name) && !ForbiddenExtensions.Contains(file.Extension))
        {
            DoIndex(file);
        }
    }

    protected virtual void DoIndex(FileInfo file)
    {
        var doc = CreateDocument(file);

        Index.AddDocument(doc);

        Console.WriteLine($"Indexing '{doc.GetField("path")}'");

        if (++bulkCounter == BulkSize)
        {
            Console.WriteLine("Flushing\r");
            Index.Flush(false, false);
            bulkCounter = 0;
        }
    }

    protected virtual Document CreateDocument(FileInfo file)
    {
        var relative = Path.GetRelativePath(Root, file.FullName);
        var doc = new Document
        {
            new StringField("path", relative, Field.Store.YES),
            new Int64Field("size", file.Length, Field.Store.YES)
        };
        if (
            TextExtensions.Contains(file.Extension.ToLowerInvariant())
            || TextPrefixes.Any(file.Name.StartsWith)
            || TextSuffixes.Any(file.Name.EndsWith)
        )
        {
            doc.AddTextField("content", File.ReadAllText(file.FullName), Field.Store.NO);
        }

        return doc;
    }
}
