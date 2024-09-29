using Fastenshtein;
using System.Linq;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;
using static ColpaBot.DataManagement.SynonymManager;
using System.Collections.Generic;

namespace ColpaBot.NaturalLanguageProcessing
{
    /// <summary>
    /// Implements question matching using Levenshtein distance algorithm.
    /// </summary>
    public class LevenshteinDistance : QuestionMatchingAlgorithm
    {
        private const double _MIN_SIMILARITY = 0.8;
        private const double _ACCEPTABLE_SIMILARITY = 0.95;

        // Maximum allowed Levenshtein distance
        const int MAXIMUM_DISTANCE = 100;

        /// <summary>
        /// The language used for synonym processing.
        /// </summary>
        private readonly string _language;

        /// <summary>
        /// Initializes a new instance of the LevenshteinDistance class.
        /// </summary>
        /// <param name="language">The language to use for synonym processing.</param>
        public LevenshteinDistance(string language) : base(_MIN_SIMILARITY, _ACCEPTABLE_SIMILARITY)
        {
            _language = language;
        }

        /// <summary>
        /// Calculates a similarity score between two strings using Levenshtein distance.
        /// </summary>
        /// <param name="a">The first string to compare.</param>
        /// <param name="b">The second string to compare.</param>
        /// <returns>A float value between 0 and 1, where 1 indicates identical strings.</returns>
        private static float GetScore(string a, string b)
        {
            int distance = Levenshtein.Distance(a, b);
            if (distance > MAXIMUM_DISTANCE)
            {
                return 0;
            }
            return 1 - (float)distance / MAXIMUM_DISTANCE;
        }

        /// <summary>
        /// Finds the most similar questions to the input string.
        /// </summary>
        /// <param name="input">The input string to match against.</param>
        /// <param name="questions">An array of questions to search through.</param>
        /// <param name="numQuestions">The number of top matches to return.</param>
        /// <returns>An array of QuestionMatch objects representing the best matches.</returns>
        public override QuestionMatch[] FindMostSimilar(string input, string[] questions, int numQuestions = 1)
        {
            string[] processedQuestions = GetNormalizedQuestions(questions, true);
            string processedInput = Normalize(input);

            // Find initial best matches
            List<string> bestMatches = processedQuestions
                .OrderByDescending(q => GetScore(q, processedInput))
                .Distinct()
                .Take(numQuestions)
                .ToList();

            // Process input with synonyms and find additional matches
            foreach (List<string> words in ProcessInputWithSynonyms(Tokenize(processedInput), _language))
            {
                string synonymInput = Detokenize(words);
                bestMatches.AddRange(questions
                    .OrderByDescending(q => GetScore(q, synonymInput))
                    .Distinct()
                    .Take(numQuestions));
            }

            // Return final sorted and distinct matches
            return bestMatches
                .OrderByDescending(q => GetScore(q, processedInput))
                .Distinct()
                .Take(numQuestions)
                .Select((q, i) => new QuestionMatch(questions[i], GetScore(q, processedInput)))
                .ToArray();
        }
    }
}