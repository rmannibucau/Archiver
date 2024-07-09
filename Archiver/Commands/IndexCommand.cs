using System.CommandLine;
using System.CommandLine.Invocation;
using Archiver.Index;
using Archiver.Io;
using Lucene.Net.Analysis.En;
using Lucene.Net.Documents;
using Lucene.Net.Documents.Extensions;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Archiver.Commands;

public class IndexCommand : Command
{
    public IndexCommand()
        : base("index", "Index a directory")
    {
        var srcOpt = new Option<string>("--source-directory", "Directory to read data from.")
        {
            IsRequired = true
        };
        srcOpt.AddAlias("-s");
        var indexOpt = new Option<string>("--index-directory", "Directory to create the index to.")
        {
            IsRequired = true
        };
        indexOpt.AddAlias("-i");
        AddOption(srcOpt);
        AddOption(indexOpt);

        Handler = new Impl(srcOpt, indexOpt);
    }

    private class Impl(Option<string> srcOpt, Option<string> indexOpt) : ICommandHandler
    {
        public int Invoke(InvocationContext context)
        {
            var src = context.ParseResult.GetValueForOption(srcOpt)!;
            var index = context.ParseResult.GetValueForOption(indexOpt)!;

            DoIndex(src, index, context.Console);

            return 0;
        }

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            return await Task.FromResult(Invoke(context));
        }

        internal void DoIndex(string src, string index, IConsole console)
        {
            var root = new DirectoryInfo(src);
            using var dir = FSDirectory.Open(index);
            using var indexWriter = new IndexWriter(
                dir,
                new IndexWriterConfig(
                    LuceneVersion.LUCENE_48,
                    new EnglishAnalyzer(LuceneVersion.LUCENE_48)
                )
            );

            new Visitor<IndexVisitor>(
                root,
                new IndexVisitor(root.FullName, indexWriter, console)
            ).Visit();
            indexWriter.Flush(true, false);
        }
    }

    private class IndexVisitor(string Root, IndexWriter Index, IConsole Console)
        : BaseVisitor(Root, Index, Console) { }
}
