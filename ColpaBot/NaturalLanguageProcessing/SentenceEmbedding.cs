using System;
using System.Linq;
using System.Collections.Generic;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;
using static ColpaBot.DataManagement.DataUtilites;
using System.IO;
using ColpaBot.DataManagement;
using Fastenshtein;

namespace ColpaBot.NaturalLanguageProcessing
{
    /// <summary>
    /// Implements a sentence embedding algorithm using FastText word embeddings.
    /// Vectors are taken from https://fasttext.cc/docs/en/crawl-vectors.html
    /// </summary>
    public class SentenceEmbedding : QuestionMatchingAlgorithm
    {
        private const double _MIN_SIMILARITY = 0.5;
        private const double _ACCEPTABLE_SIMILARITY = 0.7;

        /// <summary>
        /// Dictionary storing word vectors for quick lookup.
        /// </summary>
        private readonly Dictionary<string, double[]> _wordVectors = [];

        /// <summary>
        /// Dictionary storing pre-computed average embeddings for questions.
        /// </summary>
        private readonly Dictionary<string, double[]> _averageQuestionEmbeddings = [];

        /// <summary>
        /// Initializes a new instance of the SentenceEmbedding class.
        /// </summary>
        /// <param name="lang">The language code for which to load the word vectors.</param>
        /// <exception cref="LanguageNotSupportedException">Thrown when the specified language is not supported.</exception>
        /// <exception cref="Exception">Thrown when the vectors file is invalid or cannot be processed.</exception>
        public SentenceEmbedding(string lang) : base(_MIN_SIMILARITY, _ACCEPTABLE_SIMILARITY)
        {
            string vectorsPath = GetFilePath("cc." + Lang.TransformToIso639(lang) + ".300", [VECTORS_EXTENSION], VECTORS_DIRECTORY);

            if (!File.Exists(vectorsPath))
            {
                throw new LanguageNotSupportedException(lang, this);
            }

            using StreamReader reader = new(vectorsPath);
            try
            {
                string[] dimensions = Tokenize(reader.ReadLine());
                if (dimensions.Length != 2)
                {
                    throw new Exception();
                }
                int numWords = int.Parse(dimensions[0]);
                int numDimensions = int.Parse(dimensions[1]);

                for (int i = 0; i < numWords; i++)
                {
                    string[] parts = Tokenize(reader.ReadLine());
                    string word = parts[0].ToLower(); // Normalize to lowercase
                    double[] vector = parts.Skip(1).Select(double.Parse).ToArray();
                    if (vector.Length != numDimensions)
                    {
                        throw new Exception();
                    }
                    _wordVectors[word] = vector;
                }
            }
            catch (Exception)
            {
                throw new Exception("Invalid vectors file, it should have the format: <number of words> <number of dimensions> and then every line is a word followed by a vector, everything separated by spaces");
            }

            // Pre-compute embeddings for all questions
            foreach (string question in Qna.LangQnaPairs[lang].GetRawQuestions())
            {
                string[] words = Tokenize(question.ToLower());
                double[] embedding = GetSentenceEmbedding(words);
                _averageQuestionEmbeddings[question] = embedding ?? throw new Exception("Failed to get embedding for question: " + question);
            }
        }

        /// <summary>
        /// Computes the sentence embedding for a given array of words.
        /// </summary>
        /// <param name="words">An array of words to compute the embedding for.</param>
        /// <returns>The sentence embedding as a double array, or null if no vectors were found.</returns>
        private double[] GetSentenceEmbedding(string[] words)
        {
            List<double[]> vectors = [];

            foreach (var word in words)
            {
                if (_wordVectors.ContainsKey(word))
                {
                    vectors.Add(_wordVectors[word]);
                }
                else
                {
                    // Find closest match using Levenshtein distance
                    string closestWord = _wordVectors.Keys
                        .OrderBy(w => Levenshtein.Distance(word, w))
                        .FirstOrDefault();

                    if (closestWord != null && Levenshtein.Distance(word, closestWord) <= 2)
                    {
                        vectors.Add(_wordVectors[closestWord]);
                    }
                }
            }

            if (vectors.Count == 0) return default;

            // Compute average of all word vectors
            return vectors
                .Aggregate((a, b) => a.Zip(b, (x, y) => x + y).ToArray())
                .Select(sum => sum / vectors.Count)
                .ToArray();
        }

        /// <summary>
        /// Finds the most similar questions to the input string.
        /// It does not calculate synonyms since the synonym words should be close in the vector space.
        /// </summary>
        /// <param name="input">The input string to find matches for.</param>
        /// <param name="questions">An array of questions to search through.</param>
        /// <param name="numQuestions">The number of similar questions to return.</param>
        /// <returns>An array of QuestionMatch objects representing the most similar questions.</returns>
        public override QuestionMatch[] FindMostSimilar(string input, string[] questions, int numQuestions = 1)
        {
            double[] inputEmbedding = GetSentenceEmbedding(Tokenize(input.ToLower())); // Normalize to lowercase
            if (inputEmbedding == default)
            {
                return [];
            }

            return _averageQuestionEmbeddings
                .Where(kvp => questions.Contains(kvp.Key))
                .Select(kvp => new QuestionMatch(kvp.Key, Math.Max(0, CosineSimilarity(inputEmbedding, kvp.Value))))
                .Distinct()
                .OrderByDescending(item => item.Score)
                .Take(numQuestions)
                .ToArray();
        }
    }
}