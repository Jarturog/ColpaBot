using System;
using System.Linq;
using System.Collections.Generic;
using static ColpaBot.DataManagement.Qna;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;
using static ColpaBot.DataManagement.SynonymManager;

namespace ColpaBot.NaturalLanguageProcessing
{
    /// <summary>
    /// Implements TF-IDF (Term Frequency-Inverse Document Frequency) algorithm for question matching.
    /// </summary>
    public class Tfidf : QuestionMatchingAlgorithm
    {
        private const double _MIN_SIMILARITY = 0.5;
        private const double _ACCEPTABLE_SIMILARITY = 0.7;

        /// <summary>
        /// Stores TF-IDF vectors for each document in the collection.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, double>> _tfidfVectors;

        /// <summary>
        /// The language for which this TF-IDF instance is created.
        /// </summary>
        private readonly string _language;

        /// <summary>
        /// Total number of documents in the collection.
        /// </summary>
        private readonly int totalDocuments;

        /// <summary>
        /// Stores the number of documents each word appears in.
        /// </summary>
        private readonly Dictionary<string, int> wordDocumentFrequencies = [];

        /// <summary>
        /// Initializes a new instance of the Tfidf class.
        /// </summary>
        /// <param name="language">The language for TF-IDF calculations.</param>
        public Tfidf(string language) : base(_MIN_SIMILARITY, _ACCEPTABLE_SIMILARITY)
        {
            _language = language;
            string[] collection = LangQnaPairs[language].NormalizedQuestions;
            totalDocuments = collection.Length;

            // Calculate document frequencies for each word
            foreach (string doc in collection)
            {
                string[] words = Tokenize(doc);
                HashSet<string> uniqueWords = new(words);

                foreach (string word in uniqueWords)
                {
                    wordDocumentFrequencies[word] = wordDocumentFrequencies.GetValueOrDefault(word, 0) + 1;
                }
            }

            // Calculate TF-IDF vectors for each document
            _tfidfVectors = [];
            foreach (string doc in collection)
            {
                string[] words = Tokenize(doc);
                _tfidfVectors[doc] = CalculateTfidf(words);
            }
        }

        /// <summary>
        /// Calculates the TF-IDF vector for a given document.
        /// </summary>
        /// <param name="tokenizedDocument">The tokenized document.</param>
        /// <returns>A dictionary representing the TF-IDF vector.</returns>
        private Dictionary<string, double> CalculateTfidf(IEnumerable<string> tokenizedDocument)
        {
            Dictionary<string, double> vector = [];

            foreach (string term in LangQnaPairs[_language].NormalizedVocabulary)
            {
                int numberOfOccurrences = tokenizedDocument.Count(t => t == term);
                if (numberOfOccurrences == 0) // skip the calculations since it will be 0
                {
                    vector[term] = 0;
                    continue;
                }
                double tf = (double)numberOfOccurrences / tokenizedDocument.Count();
                // Add 1 to both numerator and denominator to avoid division by zero while also smoothing the values
                double idf = Math.Log((double)(1 + totalDocuments) / (1 + wordDocumentFrequencies[term]));
                vector[term] = tf * idf; // TF is between 0 and 1, IDF is always positive
            }
            return vector;
        }

        /// <summary>
        /// Finds the most similar questions to the input.
        /// </summary>
        /// <param name="input">The input question.</param>
        /// <param name="questions">The list of questions to compare against.</param>
        /// <param name="numQuestions">The number of similar questions to return.</param>
        /// <returns>An array of QuestionMatch objects representing the most similar questions.</returns>
        public override QuestionMatch[] FindMostSimilar(string input, string[] questions, int numQuestions = 1)
        {
            string[] processedQuestions = GetNormalizedQuestions(questions, true);
            string[] tokenizedInput = Tokenize(Normalize(input));

            List<QuestionMatch> bestMatches = [];

            List<List<string>> listOfWords = ProcessInputWithSynonyms(tokenizedInput, _language);
            foreach (var words in listOfWords)
            {
                var vector = CalculateTfidf(words.ToArray());

                // Select the best matches for each synonym variation
                bestMatches.AddRange(
                    processedQuestions.Select(
                        (q, i) => new QuestionMatch(questions[i], Math.Max(0, CosineSimilarity(vector, _tfidfVectors[q])))
                    ).OrderByDescending(item => item.Score).Distinct().Take(numQuestions));
            }
            // Select the overall best matches
            return bestMatches.OrderByDescending(item => item.Score).Distinct().Take(numQuestions).ToArray();
        }
    }
}