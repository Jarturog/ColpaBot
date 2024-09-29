using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Fastenshtein;

namespace ColpaBot.NaturalLanguageProcessing
{
    /// <summary>
    /// Provides utility methods for text processing and natural language processing tasks.
    /// </summary>
    public static class TextProcessingUtilities
    {
        /// <summary>
        /// Regular expression pattern for punctuation to be replaced with spaces.
        /// </summary>
        private const string _PUNCTUATION_TO_REPLACE_WITH_SPACE = @"[\.,;¡!¿?""\-\+\*\|@#$~%&=\\\/<>(){}\[\]]"; // \ is used as the escape character

        /// <summary>
        /// Regular expression pattern for punctuation to be removed.
        /// </summary>
        private const string _PUNCTUATION_TO_REMOVE = @"[''`´¨^·]";

        /// <summary>
        /// Normalizes the input text by removing diacritics, replacing certain punctuation with spaces,
        /// removing other punctuation, and converting to lowercase.
        /// </summary>
        /// <param name="text">The input text to normalize.</param>
        /// <returns>The normalized text.</returns>
        public static string Normalize(string text)
        {
            text = RemoveDiacriticsFromLatinAlphabet(text);
            text = Regex.Replace(text, _PUNCTUATION_TO_REMOVE, "");
            text = Regex.Replace(text, _PUNCTUATION_TO_REPLACE_WITH_SPACE, " ");
            text = Regex.Replace(text, @"\s+", " "); // Substitute more than 1 space with a single space
            return text.Trim().ToLower(); // Remove leading and trailing spaces, and lowercase the text
        }

        /// <summary>
        /// Removes diacritics from Latin alphabet characters in the input text.
        /// </summary>
        /// <param name="text">The input text to process.</param>
        /// <returns>The text with diacritics removed from Latin alphabet characters.</returns>
        private static string RemoveDiacriticsFromLatinAlphabet(string text)
        {
            return string.Concat(text.Select(c =>
            {
                // Convert the character to lowercase and process it
                string result = char.ToLower(c) switch
                {
                    'ł' => "l",
                    'ñ' or 'ń' => "n",
                    'ź' or 'ż' => "z",
                    'ś' or 'ş' or 'š' => "s",
                    'ß' => "ss",
                    'ç' or 'ć' => "c",
                    'æ' => "ae",
                    'ä' or 'à' or 'á' or 'â' or 'ã' or 'ą' => "a",
                    'ë' or 'è' or 'é' or 'ê' or 'ę' => "e",
                    'ï' or 'ì' or 'í' or 'î' => "i",
                    'ö' or 'ò' or 'ó' or 'ô' or 'õ' => "o",
                    'ü' or 'ù' or 'ú' or 'û' => "u",
                    _ => c.ToString()
                };

                // Convert the result back to uppercase if the original character was uppercase
                return char.IsUpper(c) ? result.ToUpper() : result;
            }));
        }

        /// <summary>
        /// Tokenizes the input text into an array of strings.
        /// </summary>
        /// <param name="text">The input text to tokenize.</param>
        /// <returns>An array of tokens.</returns>
        public static string[] Tokenize(string text)
        {
            return text
                .Split(' ')
                .Where(s => s != null && s != "")
                .ToArray();
        }

        /// <summary>
        /// Tokenizes the input text while preserving an associated value.
        /// </summary>
        /// <typeparam name="T">The type of the associated value.</typeparam>
        /// <param name="parameter">A tuple containing the text to tokenize and its associated value.</param>
        /// <returns>An array of tuples, each containing a token and the associated value.</returns>
        public static (string, T)[] Tokenize<T>((string, T) parameter)
        {
            return parameter.Item1
                .Split(' ')
                .Where(s => !string.IsNullOrEmpty(s))  // Remove null or empty tokens
                .Select(token => (token, parameter.Item2)) // Preserve the T value in each tuple
                .ToArray();
        }

        /// <summary>
        /// Combines an enumerable of tokens into a single string.
        /// </summary>
        /// <param name="tokens">The tokens to combine.</param>
        /// <returns>A string representation of the combined tokens.</returns>
        public static string Detokenize(IEnumerable<string> tokens)
        {
            return string.Join(' ', tokens);
        }

        /// <summary>
        /// Generates a normalized vocabulary from an array of sentences.
        /// </summary>
        /// <param name="sentences">The input sentences to process.</param>
        /// <returns>An array of unique, normalized words from the input sentences.</returns>
        public static string[] GetNormalizedVocabulary(string[] sentences)
        {
            return sentences
                .SelectMany(s => Tokenize(Normalize(s)))
                .Distinct() // Remove duplicates
                .Where(s => s != null && s != "")
                .ToArray(); // Convert the result to a single array
        }

        /// <summary>
        /// Normalizes an array of questions and optionally removes duplicates.
        /// </summary>
        /// <param name="questions">The input questions to normalize.</param>
        /// <param name="allowRepeatedQuestions">Whether to allow duplicate questions in the output.</param>
        /// <returns>An array of normalized questions.</returns>
        public static string[] GetNormalizedQuestions(string[] questions, bool allowRepeatedQuestions = false)
        {
            if (allowRepeatedQuestions)
            {
                return questions
                    .Select(Normalize)
                    .Where(q => q != null && q != "")
                    .ToArray();
            }
            return questions
                .Select(Normalize)
                .Distinct()
                .Where(q => q != null && q != "")
                .ToArray();
        }

        /// <summary>
        /// Generates the Cartesian product of multiple collections.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collections.</typeparam>
        /// <param name="collectionOfCollections">An enumerable of collections to combine.</param>
        /// <returns>A list of lists representing the Cartesian product.</returns>
        public static List<List<T>> Cartesian<T>(IEnumerable<IEnumerable<T>> collectionOfCollections)
        {
            var result = new List<List<T>> { new() };

            foreach (IEnumerable<T> sequence in collectionOfCollections)
            {
                result = result.SelectMany(seq => sequence, (seq, item) => seq.Concat([item]).ToList()).ToList();
            }

            return result;
        }

        /// <summary>
        /// Finds the closest matches to a given word within a collection, using Levenshtein distance.
        /// Designed to lower the influence of the user's typos in the input.
        /// </summary>
        /// <param name="word">The word to find matches for.</param>
        /// <param name="collection">The collection of words to search in.</param>
        /// <param name="maximumAmountOfMatches">The maximum number of matches to return.</param>
        /// <param name="maximumDistance">The maximum Levenshtein distance allowed for a match.</param>
        /// <returns>An array of the closest matching words.</returns>
        public static string[] FindClosestMatches(string word, IEnumerable<string> collection, int maximumAmountOfMatches = 1, int maximumDistance = 10)
        {
            maximumAmountOfMatches = maximumAmountOfMatches < 1 ? 1 : maximumAmountOfMatches;
            return collection
                .Distinct()
                .Select(w => (Word: w, Distance: Levenshtein.Distance(word, w)))
                .OrderBy(tuple => tuple.Distance)
                .Take(maximumAmountOfMatches)
                .Where(tuple => tuple.Distance <= maximumDistance && tuple.Word != null && tuple.Word != "")
                .Select(tuple => tuple.Word)
                .ToArray();
        }

        /// <summary>
        /// Applies stemming to a vocabulary of words. This method is currently not used and is kept for future improvement.
        /// </summary>
        /// <param name="vocabulary">The input vocabulary to stem.</param>
        /// <returns>An array of stemmed words.</returns>
        private static string[] ApplyStemming(IEnumerable<string> vocabulary)
        {
            // Helper function to get the largest common stem between two words
            static string GetLargestCommonStem(string word1, string word2, int minimumStemLength = 4, int minimumWordLength = 3)
            {
                if (word1.Length < minimumWordLength || word2.Length < minimumWordLength)
                {
                    return "";
                }
                string longestWord = word1.Length > word2.Length ? word1 : word2;
                string shortestWord = word1.Length > word2.Length ? word2 : word1;
                string largestCommonStem = "";
                for (int i = 0; i < shortestWord.Length; i++)
                {
                    for (int j = shortestWord.Length; i < j; j--)
                    {
                        string segment = shortestWord.Substring(i, j - i);
                        if (longestWord.Contains(segment) && segment.Length > largestCommonStem.Length && segment.Length >= minimumStemLength)
                        {
                            largestCommonStem = segment;
                        }
                    }
                }
                return largestCommonStem;
            }

            const int MINIMUM_CHARS_IN_COMMON = 5;
            List<string> onlyStemsVocabulary = [];
            foreach (string word1 in vocabulary)
            {
                string shortestCommonStem = word1;
                foreach (string word2 in vocabulary)
                {
                    string stem = GetLargestCommonStem(word1, word2);
                    if (stem.Length < shortestCommonStem.Length && stem.Length >= MINIMUM_CHARS_IN_COMMON)
                    {
                        shortestCommonStem = stem;
                    }
                }
                onlyStemsVocabulary.Add(shortestCommonStem);
            }
            return onlyStemsVocabulary.Distinct().ToArray();
        }
    }
}