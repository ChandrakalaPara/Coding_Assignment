using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using MarketingCodingAssignment.Models;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Lucene.Net.Analysis.En;
using System.Linq.Expressions;

namespace MarketingCodingAssignment.Services
{
    public class SearchEngine
    {
        // The code below is roughly based on sample code from: https://lucenenet.apache.org/

        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        public SearchEngine()
        {

        }

        public List<FilmCsvRecord> ReadFilmsFromCsv()
        {
            List<FilmCsvRecord> records = new();
            string filePath = $"{System.IO.Directory.GetCurrentDirectory()}{@"\wwwroot\csv"}" + "\\" + "FilmsInfo.csv";
            using (StreamReader reader = new(filePath))
            using (CsvReader csv = new(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                records = csv.GetRecords<FilmCsvRecord>().ToList();

            }
            using (StreamReader r = new(filePath))
            {
                string csvFileText = r.ReadToEnd();
            }
            return records;
        }

        // Read the data from the csv and feed it into the lucene index
        public void PopulateIndexFromCsv()
        {
            // Get the list of films from the csv file
            var csvFilms = ReadFilmsFromCsv();

            // Convert to Lucene format
            List<FilmLuceneRecord> luceneFilms = csvFilms.Select(x => new FilmLuceneRecord
            {
                Id = x.Id,
                Title = x.Title,
                Overview = x.Overview,
                Runtime = int.TryParse(x.Runtime, out int parsedRuntime) ? parsedRuntime : 0,
                Tagline = x.Tagline,
                Revenue = long.TryParse(x.Revenue, out long parsedRevenue) ? parsedRevenue : 0,
                VoteAverage = double.TryParse(x.VoteAverage, out double parsedVoteAverage) ? parsedVoteAverage : 0,
                ReleaseDate= !string.IsNullOrEmpty(x.ReleaseDate)? Convert.ToDateTime(x.ReleaseDate):null,
                ReleaseDateInt = !string.IsNullOrEmpty(x.ReleaseDate) ? Convert.ToDateTime(x.ReleaseDate).Ticks : null
            }).ToList();

            // Write the records to the lucene index
            PopulateIndex(luceneFilms);

            return;
        }

        public void PopulateIndex(List<FilmLuceneRecord> films)
        {
            // Construct a machine-independent path for the index
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string indexPath = Path.Combine(basePath, "index");
            using FSDirectory dir = FSDirectory.Open(indexPath);

            // Create an analyzer to process the text
            StandardAnalyzer analyzer = new(AppLuceneVersion);

            // Create an index writer
            IndexWriterConfig indexConfig = new(AppLuceneVersion, analyzer);
            using IndexWriter writer = new(dir, indexConfig);

            //Add to the index
            foreach (var film in films)
            {
                string dateString = "null";
                if (film.ReleaseDate.HasValue)
                {
                    dateString = film.ReleaseDate.Value.ToString("o", CultureInfo.InvariantCulture); // "o" is the round-trip format specifier
                   
                }
                

                Document doc = new()
                {
                    new StringField("Id", film.Id, Field.Store.YES),
                    new TextField("Title", film.Title, Field.Store.YES),
                    new TextField("Overview", film.Overview, Field.Store.YES),
                    new Int32Field("Runtime", film.Runtime, Field.Store.YES),
                    new TextField("Tagline", film.Tagline, Field.Store.YES),
                    new Int64Field("Revenue", film.Revenue ?? 0, Field.Store.YES),
                    new DoubleField("VoteAverage", film.VoteAverage ?? 0.0, Field.Store.YES),
                    new TextField("CombinedText", film.Title + " " + film.Tagline + " " + film.Overview, Field.Store.NO),
                    new StringField("ReleaseDate",dateString, Field.Store.YES),
                    new StringField("ReleaseDateInt", film.ReleaseDateInt.ToString(), Field.Store.YES),
                };
                writer.AddDocument(doc);
            }

            writer.Flush(triggerMerge: false, applyAllDeletes: false);
            writer.Commit();

           return;
        }

        public void DeleteIndex()
        {
            // Delete everything from the index
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string indexPath = Path.Combine(basePath, "index");
            using FSDirectory dir = FSDirectory.Open(indexPath);
            StandardAnalyzer analyzer = new(AppLuceneVersion);
            IndexWriterConfig indexConfig = new(AppLuceneVersion, analyzer);
            using IndexWriter writer = new(dir, indexConfig);
            writer.DeleteAll();
            writer.Commit();
            return;
        }

        public SearchResultsViewModel Search(string searchString,string id, int startPage, int rowsPerPage, int? durationMinimum, int? durationMaximum, double? voteAverageMinimum, DateTime? minReleaseDate, DateTime? maxReleaseDate)
        {
            // Construct a machine-independent path for the index
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string indexPath = Path.Combine(basePath, "index");
            using FSDirectory dir = FSDirectory.Open(indexPath);
            using DirectoryReader reader = DirectoryReader.Open(dir);
            IndexSearcher searcher = new(reader);

            int hitsLimit = 1000;
            TopScoreDocCollector collector = TopScoreDocCollector.Create(hitsLimit, true);

            var query = this.GetLuceneQuery(searchString,id, durationMinimum, durationMaximum, voteAverageMinimum, minReleaseDate, maxReleaseDate);

            searcher.Search(query, collector);

            int startIndex = (startPage) * rowsPerPage;
            TopDocs hits = collector.GetTopDocs(startIndex, rowsPerPage);
            ScoreDoc[] scoreDocs = hits.ScoreDocs;

            List<FilmLuceneRecord> films = new();
            foreach (ScoreDoc? hit in scoreDocs)
            {
                Document foundDoc = searcher.Doc(hit.Doc);
                var str = foundDoc.Get("ReleaseDate").ToString();
                FilmLuceneRecord film = new()
                {
                    Id = foundDoc.Get("Id").ToString(),
                    Title = foundDoc.Get("Title").ToString(),
                    Overview = foundDoc.Get("Overview").ToString(),
                    Runtime = int.TryParse(foundDoc.Get("Runtime"), out int parsedRuntime) ? parsedRuntime : 0,
                    Tagline = foundDoc.Get("Tagline").ToString(),
                    Revenue = long.TryParse(foundDoc.Get("Revenue"), out long parsedRevenue) ? parsedRevenue : 0,
                    VoteAverage =  double.TryParse(foundDoc.Get("VoteAverage"), out double parsedVoteAverage) ? parsedVoteAverage : 0.0,
                    Score = hit.Score,
                    ReleaseDate= foundDoc.Get("ReleaseDate")!="null"?Convert.ToDateTime(foundDoc.Get("ReleaseDate").ToString()):null
                };
                films.Add(film);
            }

            SearchResultsViewModel searchResults = new()
            {
                RecordsCount = hits.TotalHits,
                Films = films.ToList()
            };

            return searchResults;
        }
        private Query GetLuceneQuery(string searchString,string id, int? durationMinimum, int? durationMaximum, double? voteAverageMinimum, DateTime? minReleaseDate, DateTime? maxReleaseDate)
        {
            if (string.IsNullOrWhiteSpace(searchString) && string.IsNullOrWhiteSpace(id) && durationMinimum ==null && durationMaximum == null&& voteAverageMinimum ==null && minReleaseDate!= null &&  maxReleaseDate !=null)
            {
                // If there's no search string, just return everything.
                return new MatchAllDocsQuery();
            }

            

            Query rq = NumericRangeQuery.NewInt32Range("Runtime", durationMinimum??0, durationMaximum, true, true);
            Query vaq = NumericRangeQuery.NewDoubleRange("VoteAverage", voteAverageMinimum??0.0, 10.0, true, true);
           // Query date = DateRa.NewDoubleRange("VoteAverage", voteAverageMinimum ?? 0.0, 10.0, true, true);


            // Apply the filters.
            BooleanQuery bq = new BooleanQuery() 
            {
                  { vaq, Occur.MUST },
                 { rq, Occur.MUST }
            };

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var pq = new MultiPhraseQuery();
                foreach (var word in searchString.Split(" ").Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    if (!EnglishAnalyzer.DefaultStopSet.Contains(word))
                    {
                        pq.Add(new Term("CombinedText", word.ToLowerInvariant()));
                    }
                }


                bq.Add(new BooleanClause(pq, Occur.MUST));
            }
            if (!string.IsNullOrWhiteSpace(id))
            {
                var query = new TermQuery(new Term("Id", id));


                bq.Add(new BooleanClause(query, Occur.MUST));
            }


            if (minReleaseDate.HasValue)
            {
                // string startReleaseDateString = minReleaseDate.Value.ToString("o", CultureInfo.InvariantCulture); 
              
              //  TermRangeQuery startQuery = new TermRangeQuery("ReleaseDate", startBytes, null, true, true);
                long startTicks = minReleaseDate.Value.Ticks;
                BytesRef startBytes = new BytesRef(startTicks.ToString());
                TermRangeQuery startQuery = new TermRangeQuery("ReleaseDateInt", startBytes, null, true, true);


                bq.Add(new BooleanClause(startQuery, Occur.MUST));
            }

            if (maxReleaseDate.HasValue)
            {
                //string endReleaseDateString = maxReleaseDate.Value.ToString("o", CultureInfo.InvariantCulture);
                //BytesRef endBytes = new BytesRef(endReleaseDateString);
                //TermRangeQuery startQuery = new TermRangeQuery("ReleaseDate", endBytes, null, true, true);

                long endTicks = maxReleaseDate.Value.Ticks;
                BytesRef endBytes = new BytesRef(endTicks.ToString());
                TermRangeQuery endQuery = new TermRangeQuery("ReleaseDateInt", null, endBytes, true, true);

                bq.Add(new BooleanClause(endQuery, Occur.MUST));
            }


            return bq;
        }
    }
}

