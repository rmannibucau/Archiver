using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Compression;
using Lucene.Net.Analysis.En;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Archiver.Commands;

public class SearchCommand : Command
{
    public SearchCommand()
        : base("search", "Search a file using the Lucene index")
    {
        var indexOpt = new Option<string>(
            "--index",
            "Directory (or zip created from compress command) to read the index from."
        )
        {
            IsRequired = true,
        };
        indexOpt.AddAlias("-i");
        AddOption(indexOpt);

        var arg = new Argument<ICollection<string>>(
            "query",
            () => [""],
            "The query to search for against lucene index."
        )
        {
            Arity = new ArgumentArity(1, 1000)
        };
        AddArgument(arg);

        Handler = new Impl(indexOpt, arg);
    }

    private class Impl(Option<string> IndexOpt, Argument<ICollection<string>> Query)
        : ICommandHandler
    {
        public int Invoke(InvocationContext context)
        {
            var index = context.ParseResult.GetValueForOption(IndexOpt)!;
            var search = context.ParseResult.GetValueForArgument(Query)!;

            var parser = new QueryParser(
                LuceneVersion.LUCENE_48,
                "content",
                new EnglishAnalyzer(LuceneVersion.LUCENE_48)
            )
            {
                AllowLeadingWildcard = true
            };
            var query = parser.Parse(string.Join(" AND ", search));
            using var dir = LoadDirectory(index);
            using var indexReader = DirectoryReader.Open(dir);
            var searcher = new IndexSearcher(indexReader);
            var top = searcher.Search(query, int.MaxValue);

            context.Console.WriteLine(
                $"Matched {top.TotalHits}/{indexReader.GetDocCount("path")} documents:"
            );
            foreach (
                var (Hit, Doc) in top.ScoreDocs.Select(it => (Hit: it, Doc: searcher.Doc(it.Doc)))
            )
            {
                context.Console.WriteLine(
                    $"Path: '{Doc.Get("archive") ?? ""}' > '{Doc.Get("path")}' (score={Hit.Score})"
                );
            }

            return 0;
        }

        private Lucene.Net.Store.Directory LoadDirectory(string index)
        {
            if (index.EndsWith(".zip"))
            {
                using var zip = ZipFile.OpenRead(index);
                var dir = new RAMDirectory();
                var buffer = new byte[8096];
                int len;
                foreach (var entry in zip.Entries)
                {
                    using var indexOutput = dir.CreateOutput(entry.Name, IOContext.DEFAULT);
                    using var stream = entry.Open();
                    while ((len = stream.Read(buffer)) > 0)
                    {
                        indexOutput.WriteBytes(buffer, len);
                    }
                }
                return dir;
            }
            return FSDirectory.Open(index);
        }

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            return await Task.FromResult(Invoke(context));
        }
    }
}
