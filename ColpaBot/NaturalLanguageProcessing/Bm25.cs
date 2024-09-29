using System;
using System.Linq;
using System.Collections.Generic;
using static ColpaBot.DataManagement.Qna;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;
using static ColpaBot.DataManagement.SynonymManager;

namespace ColpaBot.NaturalLanguageProcessing
{
    public class Bm25 : QuestionMatchingAlgorithm
    {
        private const double _MIN_SIMILARITY = 0.75;
        private const double _ACCEPTABLE_SIMILARITY = 0.85;

        /// <summary>
        /// BM25 free parameter, controls term frequency saturation
        /// </summary>
        private const double _k1 = 1.2;

        /// <summary>
        /// BM25 free parameter, controls field length normalization
        /// </summary>
        private const double _b = 0.75;

        /// <summary>
        /// Dictionary to store word frequencies across all documents
        /// </summary>
        private readonly Dictionary<string, int> wordDocumentFrequencies = [];

        /// <summary>
        /// Average document length in the collection
        /// </summary>
        private readonly double _avgDocLength;

        /// <summary>
        /// Total number of documents in the collection
        /// </summary>
        private readonly int _totalDocuments;

        /// <summary>
        /// Language of the documents being processed
        /// </summary>
        private readonly string _language;

        /// <summary>
        /// Initializes a new instance of the BM25 algorithm
        /// </summary>
        /// <param name="language">The language of the documents to process</param>
        public Bm25(string language) : base(_MIN_SIMILARITY, _ACCEPTABLE_SIMILARITY)
        {
            _language = language;
            int numberOfWords = 0;
            string[] collection = LangQnaPairs[language].NormalizedQuestions;
            _totalDocuments = collection.Length;

            foreach (string doc in collection)
            {
                string[] words = Tokenize(doc);
                numberOfWords += words.Length;
                HashSet<string> uniqueWords = new(words);
                foreach (var word in uniqueWords)
                {
                    wordDocumentFrequencies[word] = wordDocumentFrequencies.GetValueOrDefault(word, 0) + 1;
                }
            }
            _avgDocLength = numberOfWords / collection.Length;
        }

        /// <summary>
        /// Calculates the BM25 score for a document given a set of query words
        /// </summary>
        /// <param name="normalizedDocument">The tokenized document</param>
        /// <param name="words">The query words</param>
        /// <returns>The normalized BM25 score</returns>
        private double CalculateBm25Score(string[] normalizedDocument, IEnumerable<string> words)
        {
            double bm25Score = 0;
            foreach (var word in words)
            {
                // Calculate Term Frequency (TF)
                int wordFrequency = normalizedDocument.Count(w => w == word); // f(qi, D)
                if (wordFrequency == 0) continue; // Skip if word is not in the document, since the score will be 0 anyway
                double numerator = wordFrequency * (_k1 + 1);
                double denominator = wordFrequency + (_k1 * (1 - _b + (_b * (normalizedDocument.Length / _avgDocLength))));
                double tf = numerator / denominator;

                // Calculate Inverse Document Frequency (IDF)
                int docFrequency = wordDocumentFrequencies.GetValueOrDefault(word, 0); // n(qi)
                double idf = Math.Log(1 + ((_totalDocuments - docFrequency + 0.5) / (docFrequency + 0.5)));
                bm25Score += tf * idf;
            }
            // Normalize score to be between 0 and 1
            return Math.Max(0, 1 - Math.Exp(-bm25Score));
        }

        /// <summary>
        /// Finds the most similar questions to the input
        /// </summary>
        /// <param name="input">The input question</param>
        /// <param name="questions">The list of questions to compare against</param>
        /// <param name="numQuestions">The number of similar questions to return</param>
        /// <returns>An array of QuestionMatch objects representing the most similar questions</returns>
        public override QuestionMatch[] FindMostSimilar(string input, string[] questions, int numQuestions = 1)
        {
            string[] processedQuestions = GetNormalizedQuestions(questions, true);
            string[] tokenizedInput = Tokenize(Normalize(input));
            List<QuestionMatch> bestMatches = [];
            List<List<string>> listOfWords = ProcessInputWithSynonyms(tokenizedInput, _language);

            foreach (List<string> words in listOfWords)
            {
                bestMatches.AddRange(
                    processedQuestions.Select(
                        (q, i) => new QuestionMatch(questions[i], CalculateBm25Score(Tokenize(q), words))
                    ).OrderByDescending(item => item.Score).Distinct().Take(numQuestions)
                );
            }
            return bestMatches.OrderByDescending(item => item.Score).Distinct().Take(numQuestions).ToArray();
        }
    }
}