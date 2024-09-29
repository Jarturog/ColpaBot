using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static ColpaBot.DataManagement.DataUtilites;
using static ColpaBot.DataManagement.Qna;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;

namespace ColpaBot.DataManagement
{
    /// <summary>
    /// Manages synonyms for different languages.
    /// </summary>
    public class SynonymManager
    {
        /// <summary>
        /// Gets the file path for synonyms based on the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The file path for synonyms.</returns>
        private static string SynonymsFilePath(string lang) => GetFilePath("synonyms_" + lang, [SHEET_EXTENSION], SYNONYMS_DIRECTORY);

        /// <summary>
        /// A dictionary that holds normalized synonyms categorized by language.
        /// </summary>
        public readonly static Dictionary<string, CategoriesDictionary<string>> NormalizedSynonyms = [];

        /// <summary>
        /// Initializes the synonym manager by loading synonyms from files.
        /// </summary>
        /// <exception cref="Exception">Thrown if natural language processing is not initialized.</exception>
        public static void Initialize()
        {
            if (!IsQnaClassInitialized)
            {
                throw new Exception("Natural language processing must be initialized before initializing the SynonymsManager");
            }

            foreach (string lang in Lang.ReadUsedLanguageListFromDisk())
            {
                CategoriesDictionary<string> synSet = [];
                if (SynonymsFilePath(lang) == null)
                {
                    // No synonyms file found for this language
                    NormalizedSynonyms[lang] = synSet;
                    continue;
                }

                using StreamReader reader = new(SynonymsFilePath(lang));
                string line;
                while ((line = SkipAndReadLine(reader)) != null)
                {
                    bool addSynonyms = true;
                    string[] synonyms = line
                        .Split(Separator)
                        .Select(Normalize)
                        .Where(s => s != null && s != "")
                        .ToArray();

                    // At least one synonym must be present in the vocabulary
                    if (!synonyms.Any(word => LangQnaPairs[lang].NormalizedVocabulary.Contains(word)))
                    {
                        // Check if synonyms can be divided into words present in the vocabulary. If all of the words are part of the vocabulary, consider it as good.
                        bool hasSynonymDividedInWordsPresentInTheVocabulary = synonyms
                            .Select(synonym => synonym.Split(' '))
                            .Any(synonymDividedInWords => synonymDividedInWords.All(word => LangQnaPairs[lang].NormalizedVocabulary.Contains(word)));
                        // only add the synonyms if they appear in the file because they can only be useful if they can be traced back to a word in the qna vocabulary
                        // this implementation makes it so the final data structure does not reflect the file
                        addSynonyms = hasSynonymDividedInWordsPresentInTheVocabulary;
                    }

                    if (addSynonyms)
                    {
                        try
                        {
                            synSet[synonyms[0]] = synonyms.Skip(1).ToHashSet(); // Add synonyms to the set
                        }
                        catch (InvalidOperationException)
                        {
                            // Ignore conflicts (e.g., a word in two synonym groups)
                            // This is possible because CategoriesDictionary reverts any invalid operation
                        }
                    }
                }
                NormalizedSynonyms[lang] = synSet; // Store the normalized synonyms for the language
            }
        }

        /// <summary>
        /// Processes the input tokens to include their synonyms.
        /// </summary>
        /// <param name="tokenizedInput">The input tokens.</param>
        /// <param name="language">The language code.</param>
        /// <returns>A list of lists containing tokens with synonyms included.</returns>
        public static List<List<string>> ProcessInputWithSynonyms(string[] tokenizedInput, string language)
        {
            string[] vocabulary = LangQnaPairs[language].NormalizedVocabulary; // Vocabulary for the language
            List<List<string>> synonymInputs = []; // Stores lists of words with synonyms

            foreach (string word in tokenizedInput)
            {
                (int Amount, int Distance)[] parameters = [(3, 1), (3, 2)]; // Parameters for closest matches
                var enumerator = parameters.GetEnumerator();
                HashSet<string> union = vocabulary.Contains(word) ? [word] : [];

                // Prepare a set to store non-repeated words in the vocabulary
                while (union.Count == 0 && enumerator.MoveNext())
                {
                    (int amount, int distance) = ((int, int))enumerator.Current;
                    union = FindClosestMatches(word, vocabulary // wordsInVocabulary
                        .Union(NormalizedSynonyms[language].Keys), amount, distance) // wordsInSynonymsVocabulary
                        .ToHashSet(); // Remove duplicates
                }

                if (union.Count == 0)
                {
                    continue; // Skip if no matching words found
                }

                List<string> wordsToAdd = []; // Prepare words to add
                foreach (string w in union)
                {
                    // Determine if the word has synonyms or if it should be added directly
                    bool hasSynonyms = NormalizedSynonyms[language].ContainsKey(w);

                    // Get the list of synonyms if available, otherwise use an empty list
                    IEnumerable<string> synonyms = hasSynonyms ? NormalizedSynonyms[language][w] : [w];

                    wordsToAdd.AddRange(synonyms.Where(synonym => vocabulary.Contains(synonym)).ToList()); // Add valid synonyms
                }
                synonymInputs.Add(wordsToAdd); // Add to the list of synonym inputs
            }

            if (synonymInputs.Count != 0 && synonymInputs.Any(s => s.Contains(null) || s.Contains("")))
            {
                throw new Exception($"Weird error, there should not be any null or empty values in the synonyms!\n - {string.Join("\n - ", synonymInputs.SelectMany(l => l.Select(s => s ?? "null")))}");
            }
            // Example of expected code behaviour. Tokenized input was ["nyc", "is", "nice"] now it is:
            // state: [["new york", "nyc"], ["is"], ["nice"]]

            // Create the Cartesian product of the synonym inputs
            synonymInputs = Cartesian(synonymInputs);
            // state: [["new york", "is", "nice"], ["nyc", "is", "nice"]]

            // Some words are composed of more than one word, so we must tokenize them
            return synonymInputs
                .Select(sentence => sentence
                    .Select(word => Tokenize(word)).SelectMany(x => x).ToList())
                .ToList();
            // state: [["new", "york", "is", "nice"], ["nyc", "is", "nice"]]
        }
    }
}