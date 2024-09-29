using System;
using System.Collections.Generic;
using System.Linq;
using static ColpaBot.DataManagement.Qna;

namespace ColpaBot.NaturalLanguageProcessing
{
    /// <summary>
    /// Represents an algorithm for matching questions based on similarity.
    /// </summary>
    /// <param name="MinimumSimilarityThreshold">Minimum similarity threshold. Once passed, the match should be considered, even though it may not be a good match.</param>
    /// <param name="AcceptanceSimilarityThreshold">Acceptance similarity threshold. Once passed, it means that the match is a good match to the given string.</param>
    public abstract class QuestionMatchingAlgorithm(double MinimumSimilarityThreshold, double AcceptanceSimilarityThreshold)
    {
        private readonly double MinimumSimilarityThreshold = MinimumSimilarityThreshold;
        private readonly double AcceptanceSimilarityThreshold = AcceptanceSimilarityThreshold;

        /// <summary>
        /// Represents a match made by the algorithm.
        /// </summary>
        /// <param name="text">The matched text.</param>
        /// <param name="score">Score from 0 to 1 given to the match. A perfect match would be 1 and a really bad one would be 0.</param>
        public readonly struct QuestionMatch
        {
            public readonly string Text;
            public readonly double Score;

            public QuestionMatch(string text, double score)
            {
                if (score > 1 || score < 0)
                    throw new ArgumentOutOfRangeException(nameof(score), $"The score must be between 0 and 1, it is {score}");
                Text = text;
                Score = score;
            }

            /// <summary>
            /// Checks if the match passes the minimum threshold.
            /// </summary>
            public readonly bool PassesMinThreshold(QuestionMatchingAlgorithm alg) => Score >= alg.MinimumSimilarityThreshold;

            /// <summary>
            /// Checks if the match passes the acceptance threshold.
            /// </summary>
            public readonly bool PassesAcceptanceThreshold(QuestionMatchingAlgorithm alg) => PassesMinThreshold(alg) && Score >= alg.AcceptanceSimilarityThreshold;

            /// <summary>
            /// Orders the matches by best score first.
            /// </summary>
            public static void OrderByBestFirst(List<QuestionMatch> matches)
            {
                matches.Sort((x, y) => y.Score.CompareTo(x.Score));
            }

            /// <summary>
            /// Orders the matches with actions and answers by best score first.
            /// </summary>
            public static void OrderByBestFirst(List<(QuestionMatch, ActionsAndAnswer)> matches)
            {
                matches.Sort((x, y) => y.Item1.Score.CompareTo(x.Item1.Score));
            }

            // Equality operators and overrides
            public static bool operator ==(QuestionMatch left, QuestionMatch right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(QuestionMatch left, QuestionMatch right)
            {
                return !left.Equals(right);
            }

            public override readonly bool Equals(object obj)
            {
                if (obj is QuestionMatch other)
                {
                    return Text == other.Text;
                }
                return false;
            }

            public override readonly int GetHashCode() => Text.GetHashCode();
        }

        /// <summary>
        /// Finds the most similar questions to the input.
        /// </summary>
        /// <param name="input">The input question.</param>
        /// <param name="questions">The list of questions to compare against.</param>
        /// <param name="numQuestions">The number of similar questions to return.</param>
        /// <returns>An array of the most similar QuestionMatches.</returns>
        public abstract QuestionMatch[] FindMostSimilar(string input, string[] questions, int numQuestions = 1);

        /// <summary>
        /// Calculates the cosine similarity between two vectors represented as dictionaries.
        /// </summary>
        protected static double CosineSimilarity(Dictionary<string, double> v1, Dictionary<string, double> v2)
        {
            IEnumerable<string> intersection = v1.Keys.Intersect(v2.Keys);
            double dotProduct = intersection.Sum(key => v1[key] * v2[key]);
            double magnitude1 = Math.Sqrt(v1.Values.Sum(val => val * val));
            double magnitude2 = Math.Sqrt(v2.Values.Sum(val => val * val));

            // If a magnitude is zero, it doesn't contribute any meaningful information
            if (magnitude1 == 0 || magnitude2 == 0) return 0;

            double cosineSimilarity = dotProduct / (magnitude1 * magnitude2);

            // Clamp the result to [-1, 1] to remove some floating point errors
            return Math.Max(-1.0, Math.Min(1.0, cosineSimilarity));
        }

        /// <summary>
        /// Calculates the cosine similarity between two vectors represented as double arrays.
        /// </summary>
        protected static double CosineSimilarity(double[] v1, double[] v2)
        {
            IEnumerable<(double, double)> zip = v1.Zip(v2, (a, b) => (a, b));
            double dotProduct = zip.Sum(v => v.Item1 * v.Item2);
            double magnitude1 = Math.Sqrt(v1.Sum(val => val * val));
            double magnitude2 = Math.Sqrt(v2.Sum(val => val * val));

            // If a magnitude is zero, it doesn't contribute any meaningful information
            if (magnitude1 == 0 || magnitude2 == 0) return 0;

            double cosineSimilarity = dotProduct / (magnitude1 * magnitude2);

            // Clamp the result to [-1, 1] to remove some floating point errors
            return Math.Max(-1.0, Math.Min(1.0, cosineSimilarity));
        }

        /// <summary>
        /// Exception thrown when a language is not supported by the algorithm.
        /// </summary>
        public class LanguageNotSupportedException(string language, QuestionMatchingAlgorithm algorithm)
            : Exception($"The language '{language}' is not supported by the '{algorithm.GetType().Name}' algorithm.")
        { }
    }
}