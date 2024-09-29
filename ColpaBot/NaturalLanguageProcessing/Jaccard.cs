using System.Linq;
using System.Collections.Generic;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;
using static ColpaBot.DataManagement.SynonymManager;

namespace ColpaBot.NaturalLanguageProcessing
{
    /// <summary>
    /// Implements the Jaccard similarity algorithm for question matching.
    /// </summary>
    public class Jaccard(string language) : QuestionMatchingAlgorithm(_MIN_SIMILARITY, _ACCEPTABLE_SIMILARITY)
    {
        private const double _MIN_SIMILARITY = 0.5;
        private const double _ACCEPTABLE_SIMILARITY = 0.7;

        /// <summary>
        /// The language used for text processing and synonym expansion.
        /// </summary>
        private readonly string _language = language;

        /// <summary>
        /// Finds the most similar questions to the input.
        /// </summary>
        /// <param name="input">The input question to match against.</param>
        /// <param name="questions">The array of questions to compare with.</param>
        /// <param name="numQuestions">The number of top matches to return.</param>
        /// <returns>An array of QuestionMatch objects representing the best matches.</returns>
        public override QuestionMatch[] FindMostSimilar(string input, string[] questions, int numQuestions = 1)
        {
            string[] tokenizedInput = Tokenize(Normalize(input));

            List<QuestionMatch> bestMatches = [];
            foreach (var words in ProcessInputWithSynonyms(tokenizedInput, _language))
            {
                HashSet<string> userSet = new(words);
                // Select the numQuestions best matches and add them to the collection
                bestMatches.AddRange(questions
                    .Select(q => new QuestionMatch(
                        q,
                        CalculateJaccardSimilarity(userSet, new HashSet<string>(Tokenize(Normalize(q))))
                    ))
                    .OrderByDescending(x => x.Score)
                    .Distinct()
                    .Take(numQuestions));
            }
            // Select the overall numQuestions best matches
            return bestMatches
                .OrderByDescending(item => item.Score)
                .Distinct()
                .Take(numQuestions)
                .ToArray();
        }

        /// <summary>
        /// Calculates the Jaccard similarity between two sets of strings.
        /// </summary>
        /// <param name="set1">The first set of strings.</param>
        /// <param name="set2">The second set of strings.</param>
        /// <returns>The Jaccard similarity score.</returns>
        private static double CalculateJaccardSimilarity(HashSet<string> set1, HashSet<string> set2)
        {
            HashSet<string> intersection = new(set1);
            intersection.IntersectWith(set2);
            HashSet<string> union = new(set1);
            union.UnionWith(set2);
            return (double)intersection.Count / union.Count;
        }
    }
}