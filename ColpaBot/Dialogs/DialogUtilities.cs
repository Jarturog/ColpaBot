using ColpaBot.NaturalLanguageProcessing;
using Microsoft.Bot.Builder.Dialogs.Choices;
using System.Collections.Generic;
using System;

namespace ColpaBot.Dialogs
{
    public static class DialogUtilities
    {
        /// <summary>
        /// Retrieves the non-normalized choice from a list of possible choices based on a selected choice.
        /// </summary>
        /// <param name="selectedChoice">The choice selected by the user.</param>
        /// <param name="possibleChoices">An enumerable collection of possible choices.</param>
        /// <returns>The non-normalized choice that matches the selected choice, or null if no match is found.</returns>
        /// <exception cref="Exception">Thrown when the selected choice is ambiguous and matches multiple choices.</exception>
        public static string GetNonNormalizedChoice(string selectedChoice, IEnumerable<string> possibleChoices)
        {
            string normalizedChoice = TextProcessingUtilities.Normalize(selectedChoice);
            string match = null;

            foreach (string choice in possibleChoices)
            {
                if (normalizedChoice == TextProcessingUtilities.Normalize(choice))
                {
                    if (match != null)
                    {
                        throw new Exception($"{selectedChoice} is ambiguous because it can be mapped into more than one choice");
                    }
                    match = choice;
                }
            }
            return match;
        }

        /// <summary>
        /// Creates a list of Choice objects with normalized synonyms for each choice.
        /// </summary>
        /// <param name="choices">An enumerable collection of choices.</param>
        /// <returns>A list of Choice objects with normalized synonyms.</returns>
        public static List<Choice> CreateChoicesWithNormalizedSynonyms(IEnumerable<string> choices)
        {
            List<Choice> choicesWithSynonyms = [];

            foreach (string choice in choices)
            {
                // Add a new Choice with the original choice as the value and its normalized form as a synonym
                choicesWithSynonyms.Add(new(choice) { Synonyms = [TextProcessingUtilities.Normalize(choice)] });
            }

            return choicesWithSynonyms;
        }
    }
}