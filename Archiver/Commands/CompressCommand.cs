using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Compression;
using System.Text;
using Archiver.Index;
using Archiver.Io;
using Lucene.Net.Analysis.En;
using Lucene.Net.Documents;
using Lucene.Net.Documents.Extensions;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Archiver.Commands;

public class CompressCommand : Command
{
    public CompressCommand()
        : base(
            "compress",
            "Compress a directory and creates Lucene index, idea is to keep small zip files for a faster and easier lookup later"
        )
    {
        var srcOpt = new Option<string>("--input-directory", "Directory to compress and index.")
        {
            IsRequired = true,
        };
        srcOpt.AddAlias("-s");
        AddOption(srcOpt);

        var toOpt = new Option<string>("--output-directory", "Output root directory.")
        {
            IsRequired = true,
        };
        toOpt.AddAlias("-t");
        AddOption(toOpt);

        Handler = new Impl(srcOpt, toOpt);
    }

    private class Impl(Option<string> From, Option<string> To) : ICommandHandler
    {
        public int Invoke(InvocationContext context)
        {
            var from = context.ParseResult.GetValueForOption(From)!;
            var to = context.ParseResult.GetValueForOption(To)!;

            var tmpIndex = Path.Combine(to, "_index");
            var root = new DirectoryInfo(from);
            using (var dir = FSDirectory.Open(tmpIndex, NoLockFactory.GetNoLockFactory()))
            {
                using var indexWriter = new IndexWriter(
                    dir,
                    new IndexWriterConfig(
                        LuceneVersion.LUCENE_48,
                        new EnglishAnalyzer(LuceneVersion.LUCENE_48)
                    )
                );

                new Visitor<CompressVisitor>(
                    root,
                    new CompressVisitor(root, to, indexWriter, context.Console)
                ).Visit();
                indexWriter.Flush(true, false);
            }

            // zip the index
            using (
                var indexZip = ZipFile.Open(
                    Path.Combine(to, "_index.zip"),
                    ZipArchiveMode.Create,
                    Encoding.UTF8
                )
            )
            {
                foreach (var it in new DirectoryInfo(tmpIndex).EnumerateFileSystemInfos())
                {
                    indexZip.CreateEntryFromFile(it.FullName, it.Name);
                }
            }

            // finally remove the tmp index directory
            System.IO.Directory.Delete(tmpIndex, true);

            return 0;
        }

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            return await Task.FromResult(Invoke(context));
        }
    }

    private class CompressVisitor : BaseVisitor
    {
        // folders which are ignored when nested in another (normally) archived folder and kepts as a root of archived folders
        // enables to keep nested folders "atomic" (split) and makes it easier to look back in them later on
        //
        // as of today it must follow $root/$child/$promotableFolder layout
        //
        // TODO: option?
        private static readonly HashSet<string> PromotedFolders = ["0_dev"];
        protected static readonly HashSet<string> ArchivedButNotIndexedExtensions =
        [
            ".pem",
            ".jpg",
            ".jpeg",
            ".png",
            ".bmp",
            ".tiff",
            ".giff",
            ".svg"
        ];

        private readonly string _outputBase;
        private readonly DirectoryInfo _root;
        private readonly IndexWriter _index;
        private readonly IConsole _console;

        private string? currentZip;
        private DirectoryInfo? currentDirectory;
        private ZipArchive? zip;

        private ZipArchive _rootZip;
        private FileInfo? _lastIndexedFile;

        internal CompressVisitor(
            DirectoryInfo root,
            string outputBase,
            IndexWriter index,
            IConsole console
        )
            : base(root.FullName, index, console)
        {
            _outputBase = outputBase;
            _root = root;
            _index = index;
            _console = console;

            System.IO.Directory.CreateDirectory(outputBase);

            var rootZipPath = Path.Combine(_outputBase, $"{root.Name}.zip");
            _rootZip = ZipFile.Open(rootZipPath, ZipArchiveMode.Create, Encoding.UTF8);
        }

        public override IVisitorHandler.DirectoryVisitState OnDirectory(DirectoryInfo directory)
        {
            if (directory == _root)
            {
                return IVisitorHandler.DirectoryVisitState.Continue;
            }

            var result = base.OnDirectory(directory);
            if (result == IVisitorHandler.DirectoryVisitState.SkipSubsTree)
            {
                return result;
            }

            if (PromotedFolders.Contains(directory.Name)) // note: if another folder is nested, it is ignored
            {
                var target = Path.Combine(
                    _outputBase,
                    Path.GetRelativePath(_root.FullName, directory.FullName)
                );

                Console.WriteLine($"Specific handling of '{directory.FullName}' to '{target}'");
                System.IO.Directory.CreateDirectory(target);

                new Visitor<CompressVisitor>(
                    directory,
                    new CompressVisitor(directory, target, _index, _console)
                ).Visit();
                _index.Flush(false, false);

                return IVisitorHandler.DirectoryVisitState.SkipSubsTree; // already done
            }

            if (currentDirectory is not null && zip is not null)
            {
                var path =
                    Path.GetRelativePath(currentDirectory.FullName, directory.FullName)
                        .Replace(Path.PathSeparator, '/') + '/';
                Console.WriteLine($"Adding directory '{path}' from '{currentDirectory}'");
                zip.CreateEntry(path);
                return IVisitorHandler.DirectoryVisitState.Continue;
            }

            currentDirectory = directory;
            var relative = $"{Path.GetRelativePath(_root.FullName, directory.FullName)}.zip";
            var archivePath = Path.Combine(_outputBase, relative);
            zip = ZipFile.Open(archivePath, ZipArchiveMode.Create, Encoding.UTF8);
            Console.WriteLine($"Creating {archivePath}");
            currentZip = relative;
            return result;
        }

        public override void OnFile(FileInfo file)
        {
            base.OnFile(file);
            if (
                _lastIndexedFile != file
                && ArchivedButNotIndexedExtensions.Contains(file.Extension)
            ) // was not indexed but can be archived (images)
            {
                DoArchive(file);
            }
        }

        protected override void DoIndex(FileInfo file)
        {
            _lastIndexedFile = file;
            DoArchive(file);
            base.DoIndex(file);
        }

        private void DoArchive(FileInfo file)
        {
            ZipArchive? archive = zip;
            if (archive is null)
            {
                if (
                    System.IO.Directory.GetParent(file.FullName)!.FullName
                    == _root.FullName.TrimEnd('/').TrimEnd('\\')
                )
                {
                    archive = _rootZip;
                }
                else
                {
                    Console.WriteLine($"Ignoring {file.FullName}");
                    return;
                }
            }

            if (archive is null)
            {
                return;
            }

            Console.WriteLine($"Adding '{file.FullName}' from '{currentDirectory}'");
            archive.CreateEntryFromFile(
                file.FullName,
                currentDirectory is not null
                    ? Path.GetRelativePath(currentDirectory.FullName, file.FullName)
                        .Replace(Path.PathSeparator, '/')
                    : file.Name,
                CompressionLevel.SmallestSize
            );
        }

        public override void OnDirectoryExit(DirectoryInfo directory)
        {
            if (directory == currentDirectory)
            {
                Console.WriteLine($"Archived '{currentDirectory.FullName}'");
                zip?.Dispose();
                zip = null;
                currentDirectory = null;
                currentZip = null;
            }
            else if (directory == _root)
            {
                Console.WriteLine($"Archived '{_root}'");
                _rootZip.Dispose();
            }
        }

        protected override Document CreateDocument(FileInfo file)
        {
            var doc = base.CreateDocument(file);
            if (currentZip is not null)
            {
                doc.AddStringField("archive", currentZip, Field.Store.YES);
            }
            else
            {
                var parent = System.IO.Directory.GetParent(file.FullName)!;
                if (parent.FullName == _root.FullName.TrimEnd('/').TrimEnd('\\'))
                {
                    doc.AddStringField("archive", $"{parent.Name}.zip", Field.Store.YES);
                }
            }
            return doc;
        }
    }
}
