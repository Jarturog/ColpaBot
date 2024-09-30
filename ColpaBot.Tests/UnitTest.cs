using ColpaBot.Bots;
using ColpaBot.DataManagement;
using ColpaBot.Dialogs;
using ColpaBot.NaturalLanguageProcessing;
using Microsoft.Extensions.Configuration;
using static ColpaBot.NaturalLanguageProcessing.QuestionMatchingAlgorithm;

namespace ColpaBot.Tests
{
    /// <summary>
    /// Test class to test the QuestionMatchingAlgorithms. It can also compare them.
    /// </summary>
    [TestClass]
    public class QuestionMatchingAlgorithmTests
    {
        /// <summary>
        /// Random generator used for question selection.
        /// </summary>
        private static readonly Random r = new();

        /// <summary>
        /// Indicates whether the system has been initialized.
        /// </summary>
        private static bool _isInitialized = false;

        /// <summary>
        /// Number of questions to test per day.
        /// </summary>
        private const int N_QUESTIONS_TO_TEST_PER_DAY = 5;

        /// <summary>
        /// Number of languages tested.
        /// </summary>
        private const int N_LANGUAGES = 2;

        /// <summary>
        /// Number of typos to introduce in the questions.
        /// </summary>
        private const int N_TYPOS = 2;

        /// <summary>
        /// Days to test for appointment-based questions.
        /// </summary>
        private static readonly int[] daysToTest = new int[] { 10, 2, 1, 0, -1 };

        /// <summary>
        /// Selected questions for specific appointment days in the 'EN-GB' language.
        /// </summary>
        private static readonly (string, Dictionary<int, string[]>) selectedQuestions = ("EN-GB", new Dictionary<int, string[]>
        {
            // days for appointment, questions. The number of days must also be in "daysToTest"
            { 10, new[] { "What should I avoid today?", "What is the recommendation for the diet?" } },
            { 1, new[] { "I do not know what to do today." } }
        });

        /// <summary>
        /// Initializes the system for tests. Loads configuration, sets up directories, and initializes components.
        /// </summary>
        [TestInitialize]
        public void TestInitialize()
        {
            // Set the project directory as the current one
            string path = Directory.GetCurrentDirectory();
            path = path.Replace(".Tests", "");
            Directory.SetCurrentDirectory(path);

            if (_isInitialized)
            {
                return;
            }

            // Read bot URL from appsettings.json
            IConfiguration testConfiguration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            DataUtilites.Initialize();
            Lang.Initialize();
            BotMessages.Initialize();
            Qna.Initialize();
            SynonymManager.Initialize();
            ReminderScheduler.Initialize(testConfiguration);
            UserProfile.Initialize();
            ColpaBot<UserProfileDialog, ShowQuestionsDialog, ChangeAppointmentDialog>.Initialize();
            _isInitialized = true;
        }

        /// <summary>
        /// Prints the structure of QnA data for each language, including the number of words per question.
        /// </summary>
        //[TestMethod] // comment this line to avoid running this test when deploying
        public void PrintQnaStructureData()
        {
            foreach ((string lang, Qna qna) in Qna.LangQnaPairs)
            {
                Console.WriteLine($"QnA data for language {lang}\n" +
                    $"Number of words: {qna.NormalizedVocabulary.Distinct().Count()}\n" +
                    $"Number of questions: {qna.NormalizedQuestions.Distinct().Count()}");

                var distinctQuestions = qna.NormalizedQuestions.Distinct();
                int acc = 0;
                foreach (var q in distinctQuestions)
                {
                    acc += TextProcessingUtilities.Tokenize(q).Where(s => s != null && s != "").Count();
                }
                var averageWords = (double)acc / distinctQuestions.Count();
                Console.WriteLine($"Number of words per question: {averageWords}\n");
            }
        }

        /// <summary>
        /// Tests exact matching of random questions for each algorithm and appointment day.
        /// </summary>
        [TestMethod]
        public void TestExactRandomQuestions()
        {
            var enumerator = Qna.LangQnaPairs.GetEnumerator();
            for (int i = 0; i < Math.Min(N_LANGUAGES, Qna.LangQnaPairs.Count); i++)
            {
                enumerator.MoveNext();
                (_, Qna qna) = enumerator.Current;
                foreach (QuestionMatchingAlgorithm algorithm in qna.QmAlgorithms)
                {
                    Type algType = algorithm.GetType();
                    foreach (int daysForAppointment in daysToTest)
                    {
                        DateTime simulatedAppointment = DateTime.UtcNow.AddDays(daysForAppointment);
                        string[] questions = qna.GetRawQuestions(simulatedAppointment);

                        for (int j = 0; j < N_QUESTIONS_TO_TEST_PER_DAY; j++)
                        {
                            int randomNumber = r.Next(questions.Length);
                            int misses = 0;
                            (Qna.ActionsAndAnswer exactMatch, _, _) = qna.GetAnswer(questions[randomNumber], simulatedAppointment, ref misses, algType);
                            Assert.IsNotNull(exactMatch, $"Failed to get an exact match with algorithm {algType.Name} and question: {questions[randomNumber]}");
                        }
                    }
                    Console.WriteLine($"Test with {algType.Name} finished");
                }
            }
        }

        /// <summary>
        /// Tests and compares algorithms using simulated random questions with introduced typos and synonyms.
        /// </summary>
        //[TestMethod] // comment this line to avoid running this test when deploying
        public void TestAndCompareWithSimulatedRandomQuestions()
        {
            const double MIN_MATCHES_PCT = 0.5; // 50%
            const bool ALLOW_MERGING_WORDS = false;
            string output = "";
            int N_ITERATIONS = 4;
            Dictionary<string, Dictionary<Type, (int GoodMatches, int OkMatches, long AccumulatedTime)>>[] dataPerIterations = new Dictionary<string, Dictionary<Type, (int GoodMatches, int OkMatches, long AccumulatedTime)>>[N_ITERATIONS];

            for (int n = 0; n < N_ITERATIONS; n++)
            {
                Console.WriteLine($"Iteration {n + 1}");
                Dictionary<string, Dictionary<Type, (int GoodMatches, int OkMatches, long AccumulatedTime)>> dataInOneIteration = new();
                var enumerator = Qna.LangQnaPairs.GetEnumerator();
                for (int i = 0; i < Math.Min(N_LANGUAGES, Qna.LangQnaPairs.Count); i++)
                {
                    enumerator.MoveNext();
                    (string lang, Qna qna) = enumerator.Current;
                    output += $"Results with random questions for language {lang}\n";
                    Dictionary<Type, (int GoodMatches, int OkMatches, long AccumulatedTime)> hits = new();
                    dataInOneIteration[lang] = hits;

                    foreach (int daysForAppointment in daysToTest)
                    {
                        DateTime simulatedAppointment = DateTime.UtcNow.AddDays(daysForAppointment);
                        string[] allQuestions = qna.GetRawQuestions(simulatedAppointment);

                        for (int j = 0; j < N_QUESTIONS_TO_TEST_PER_DAY; j++)
                        {
                            string exactQuestion = allQuestions[r.Next(allQuestions.Length)];
                            string questionWithSynonym = SubstituteSynonym(exactQuestion);
                            string simulatedQuestion = ALLOW_MERGING_WORDS ?
                                AddTypoToSentence(questionWithSynonym, N_TYPOS) :
                                AddTypoToWord(questionWithSynonym, N_TYPOS);

                            foreach (QuestionMatchingAlgorithm algorithm in qna.QmAlgorithms)
                            {
                                Type algorithmType = algorithm.GetType();
                                int misses = 0;
                                long now = DateTime.Now.Ticks;
                                (Qna.ActionsAndAnswer exactMatch, _, _) = qna.GetAnswer(exactQuestion, simulatedAppointment, ref misses, algorithmType);
                                (Qna.ActionsAndAnswer simulatedMatch, string[] simulatedSomewhatSimilarQuestions, _) = qna.GetAnswer(simulatedQuestion, simulatedAppointment, ref misses, algorithmType);
                                long elapsed = (DateTime.Now.Ticks - now) / 2; // calculate the average
                                Assert.IsNotNull(exactMatch, $"Failed to get an exact match for question {simulatedQuestion}");

                                bool workedGood = simulatedMatch != default && simulatedMatch.Answer == exactMatch.Answer;
                                if (!hits.ContainsKey(algorithmType))
                                {
                                    hits[algorithmType] = (0, 0, 0);
                                }
                                if (workedGood)
                                {
                                    hits[algorithmType] = (hits[algorithmType].GoodMatches + 1, hits[algorithmType].OkMatches, hits[algorithmType].AccumulatedTime);
                                }
                                else if (!workedGood && simulatedSomewhatSimilarQuestions.Contains(exactQuestion))
                                {
                                    hits[algorithmType] = (hits[algorithmType].GoodMatches, hits[algorithmType].OkMatches + 1, hits[algorithmType].AccumulatedTime);
                                    output += $" Question '{simulatedQuestion}' did not get A GOOD match by {algorithmType.Name}\n";
                                }
                                else
                                {
                                    output += $" Question '{simulatedQuestion}' did not get ANY match by {algorithmType.Name}\n";
                                }
                                hits[algorithmType] = (hits[algorithmType].GoodMatches, hits[algorithmType].OkMatches, hits[algorithmType].AccumulatedTime + elapsed);
                            }
                        }
                    }
                }
                dataPerIterations[n] = dataInOneIteration;
            }
            int nQuestions = N_QUESTIONS_TO_TEST_PER_DAY * daysToTest.Length;
            string fileContent = "Language\tAlgorithm\tTotalQuestions\tGoodMatches\tGoodMatchesRatio\tMisses\tMissesRatio\tAverageTime\n";
            Dictionary<Type, (int GoodMatches, int OkMatches, long AccumulatedTime)> dataAverages = new();
            int iteration = 1;
            foreach (var data in dataPerIterations)
            {
                output += $"Iteration {iteration}\n";
                fileContent += $"Iteration {iteration++}\n";
                foreach (var kvpair1 in data)
                {
                    string lang = kvpair1.Key;
                    var hits = kvpair1.Value;
                    foreach (var kvpair2 in hits)
                    {
                        Type algType = kvpair2.Key;
                        var (GoodMatches, OkMatches, AccumulatedTime) = kvpair2.Value;
                        var previousValue = dataAverages.GetValueOrDefault(algType);
                        dataAverages[algType] = (previousValue.GoodMatches + GoodMatches, previousValue.OkMatches + OkMatches, previousValue.AccumulatedTime + AccumulatedTime);
                        TimeSpan averageTime = TimeSpan.FromTicks(AccumulatedTime / nQuestions);
                        double ratioGood = GoodMatches / (double)nQuestions;
                        double ratioOk = OkMatches / (double)nQuestions;
                        double ratioTotal = (GoodMatches + OkMatches) / (double)nQuestions;
                        int misses = nQuestions - GoodMatches - OkMatches;
                        double ratioMisses = misses / (double)nQuestions;
                        output += $"  In total {algType.Name} obtained {GoodMatches} good matches and {OkMatches} ok matches." +
                            $" It had a total of {ratioGood * 100}% of good matches, {ratioTotal * 100}% of all matches and an average time for finding an answer of {averageTime}\n";
                        fileContent += $"{lang}\t{algType.Name}\t{nQuestions}\t{GoodMatches}\t{ratioGood}\t{misses}\t{ratioMisses}\t{averageTime.TotalSeconds}\n";
                        Assert.IsTrue(ratioGood >= MIN_MATCHES_PCT, $"Algorithm {algType.Name} only had {ratioGood * 100}% of good matches, lower than {MIN_MATCHES_PCT * 100}%");
                    }
                }
                output += "\n\n";
            }
            fileContent += "\nAlgorithm\tTotalQuestions\tGoodMatches\tGoodMatchesRatio\tMisses\tMissesRatio\tAverageTime\n";
            nQuestions *= dataPerIterations.Sum(d => d.Count);
            foreach (var kvp in dataAverages)
            {
                var algType = kvp.Key;
                var (GoodMatches, OkMatches, AccumulatedTime) = kvp.Value;
                TimeSpan averageTime = TimeSpan.FromTicks(AccumulatedTime / nQuestions);
                double ratioGood = GoodMatches / (double)nQuestions;
                double ratioOk = OkMatches / (double)nQuestions;
                int misses = nQuestions - GoodMatches - OkMatches;
                double ratioMisses = misses / (double)nQuestions;
                fileContent += $"{kvp.Key.Name}\t{nQuestions}\t{GoodMatches}\t{ratioGood}\t{misses}\t{ratioMisses}\t{averageTime.TotalSeconds}\n";
            }
            Console.Write(output);
            using var streamWriter = new StreamWriter("test_results.tsv");
            streamWriter.Write(fileContent);
        }

        /// <summary>
        /// Tests and compares algorithms using pre-selected questions.
        /// </summary>
        //[TestMethod] // comment this line to avoid running this test when deploying
        public void TestAndCompareWithSelectedQuestions()
        {
            var enumerator = Qna.LangQnaPairs.GetEnumerator();
            for (int i = 0; i < Math.Min(N_LANGUAGES, Qna.LangQnaPairs.Count); i++)
            {
                enumerator.MoveNext();
                (string lang, Qna qna) = enumerator.Current;
                Console.WriteLine($"Results with selected questions for language {lang}");

                Dictionary<int, Dictionary<string, List<(Type Algorithm, long elapsed, string? Match)>>> hits = new();

                foreach (int daysForAppointment in daysToTest)
                {
                    if (!selectedQuestions.Item2.TryGetValue(daysForAppointment, out string[]? questions))
                    {
                        continue;
                    }
                    hits[daysForAppointment] = new();
                    var currentDictionary = hits[daysForAppointment];
                    DateTime simulatedAppointment = DateTime.UtcNow.AddDays(daysForAppointment);

                    for (int j = 0; j < Math.Min(N_QUESTIONS_TO_TEST_PER_DAY, questions.Length); j++)
                    {
                        currentDictionary[questions[j]] = new();
                        foreach (QuestionMatchingAlgorithm algorithm in qna.QmAlgorithms)
                        {
                            Type algorithmType = algorithm.GetType();

                            long now = DateTime.Now.Ticks;
                            QuestionMatch match = qna.GetMostSimilarQuestion(questions[j], simulatedAppointment, algorithmType);
                            long elapsed = DateTime.Now.Ticks - now;
                            currentDictionary[questions[j]].Add((algorithmType, elapsed, match == default ? null : match.Text));
                        }
                    }

                }
                foreach (QuestionMatchingAlgorithm algorithm in qna.QmAlgorithms)
                {
                    Type algType = algorithm.GetType();
                    int totalQuestions = selectedQuestions.Item2.Sum(kvp => kvp.Value.Length);
                    TimeSpan averageTime = TimeSpan.FromTicks(hits.Sum(d => d.Value.Sum(q => q.Value.Where(t => t.Algorithm == algType).Sum(t => t.elapsed))) / totalQuestions);
                    int numberOfMatches = hits.Sum(d => d.Value.Sum(q => q.Value.Where(t => t.Algorithm == algType).Count(t => t.Match != null)));
                    double ratioMatched = numberOfMatches / (double)totalQuestions;
                    Console.WriteLine($"{algType.Name} obtained {ratioMatched * 100}% of matches with an average time of {averageTime.TotalSeconds} seconds");
                }
                Console.WriteLine();
                foreach (var dayKvp in hits)
                {
                    Console.WriteLine($"Data for day {dayKvp.Key}");
                    foreach (var questionKvp in dayKvp.Value)
                    {
                        Console.WriteLine($"\tData for question {questionKvp.Key}");
                        foreach (var (Algorithm, _, Match) in questionKvp.Value)
                        {
                            Console.WriteLine(Match == null
                                ? $"\t\tAlgorithm {Algorithm.Name} did not obtain any match"
                                : $"\t\tAlgorithm {Algorithm.Name} obtained match \"{Match}\""
                            );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Substitutes a word in the sentence with its synonym.
        /// </summary>
        /// <param name="sentence">The original sentence.</param>
        /// <param name="numberOfSubstitutions">Number of words to substitute.</param>
        /// <returns>The sentence with substituted synonyms.</returns>
        private static string SubstituteSynonym(string sentence, int numberOfSubstitutions = 1)
        {
            int substitutionsMade = 0;
            string[] words = TextProcessingUtilities.Tokenize(sentence).Select(w => TextProcessingUtilities.Normalize(w)).ToArray();
            for (int i = 0; i < words.Length && substitutionsMade < numberOfSubstitutions; i++)
            {
                if (SynonymManager.NormalizedSynonyms.TryGetValue(words[i], out CategoriesDictionary<string>? synonyms))
                {
                    HashSet<string> synSet = synonyms[words[i]];
                    words[i] = synSet.ToArray()[r.Next(synSet.Count)];
                    substitutionsMade++;
                }
            }
            return TextProcessingUtilities.Detokenize(words);
        }

        /// <summary>
        /// Adds typos to random words in the sentence.
        /// </summary>
        /// <param name="sentence">The original sentence.</param>
        /// <param name="numberOfTypos">Number of typos to introduce.</param>
        /// <returns>The sentence with introduced typos.</returns>
        private static string AddTypoToSentence(string sentence, int numberOfTypos = 1)
        {
            string[] tokenizedSentence = TextProcessingUtilities.Tokenize(sentence);
            for (int i = 0; i < numberOfTypos; i++)
            {
                int j = r.Next(tokenizedSentence.Length);
                tokenizedSentence[j] = AddTypoToWord(tokenizedSentence[j]);
            }
            return TextProcessingUtilities.Detokenize(tokenizedSentence);
        }

        /// <summary>
        /// Adds typos to a single word.
        /// </summary>
        /// <param name="word">The original word.</param>
        /// <param name="numberOfTypos">Number of typos to introduce.</param>
        /// <returns>The word with introduced typos.</returns>
        private static string AddTypoToWord(string word, int numberOfTypos = 1)
        {
            for (int numberOfSwaps = 0; numberOfSwaps < word.Length && numberOfSwaps < numberOfTypos; numberOfSwaps++)
            {
                char[] s = word.ToCharArray();
                int i = r.Next(word.Length);
                char c;
                // Select a random character different from the current one
                while ((c = (char)r.Next('a', 'z' + 1)) == s[i]) ;
                s[i] = c;
                word = new string(s);
            }
            return word;
        }
    }
}