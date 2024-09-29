using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using static ColpaBot.DataManagement.DataUtilites;

namespace ColpaBot.DataManagement
{
    public class BotMessages
    {
        /// <summary>
        /// File path for the bot messages data file.
        /// </summary>
        private static readonly string _MESSAGES_FILE_PATH = GetFilePath("bot_messages", [SHEET_EXTENSION], DATA_DIRECTORY);

        /// <summary>
        /// List of supported languages.
        /// </summary>
        public static string[] LanguageList { get; set; }

        /// <summary>
        /// Dictionary to store messages, keyed by message identifier.
        /// </summary>
        private static Dictionary<string, List<string>> _messages { get; } = [];

        /// <summary>
        /// Indicates whether the class messages have been initialized.
        /// </summary>
        public static bool IsClassMsgsInitialized { get; private set; } = false;

        /// <summary>
        /// Initializes the BotMessages class by loading messages from the file.
        /// </summary>
        /// <exception cref="InvalidDataException">Thrown when the file format is invalid or there are duplicate keys.</exception>
        public static void Initialize()
        {
            using StreamReader reader = new(_MESSAGES_FILE_PATH);
            string line = SkipAndReadLine(reader);
            if (!line.Equals(FORMAT_CHARS))
            {
                throw new InvalidDataException($"Invalid format characters in file {_MESSAGES_FILE_PATH}. Expected {FORMAT_CHARS} but found {line}");
            }
            line = SkipAndReadLine(reader);
            if (!line.Equals(INDEFINITE_FORMAT_CHARS))
            {
                throw new InvalidDataException($"Invalid format characters in file {_MESSAGES_FILE_PATH}. Expected {INDEFINITE_FORMAT_CHARS} but found {line}");
            }
            line = SkipAndReadLine(reader);
            LanguageList = line.Split(Separator).Skip(1).ToArray(); // Skips the first column which is not a language
            int numColumns = LanguageList.Length + 1; // plus key column
            while ((line = SkipAndReadLine(reader)) != null)
            {
                string[] parts = line.Split(Separator);
                if (parts.Length != numColumns)
                {
                    throw new InvalidDataException($"Invalid messages file format in {_MESSAGES_FILE_PATH}. Each line must have {numColumns} columns, {parts.Length} were found in line {line}");
                }
                string key = parts[0].ToLower();
                if (_messages.ContainsKey(key))
                {
                    throw new InvalidDataException($"Duplicate key {key} found in file {_MESSAGES_FILE_PATH}");
                }
                List<string> messages = [];
                for (int i = 1; i < parts.Length; i++)
                {
                    messages.Add(parts[i]);
                }
                _messages[key] = messages;
            }
            IsClassMsgsInitialized = true;
        }

        /// <summary>
        /// Retrieves all messages for a given message key.
        /// </summary>
        /// <param name="messageKey">The key to look up messages.</param>
        /// <returns>A list of messages for all languages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messageKey is null.</exception>
        /// <exception cref="InvalidDataException">Thrown when the message key is not found.</exception>
        public static List<string> GetMessages(string messageKey)
        {
            if (messageKey == null)
            {
                throw new ArgumentNullException($"{nameof(messageKey)} must not be null");
            }
            if (!_messages.TryGetValue(messageKey.ToLower(), out List<string> messageRow))
            {
                throw new InvalidDataException($"Message key {messageKey} not found in dictionary");
            }
            return messageRow;
        }

        /// <summary>
        /// Checks if a message exists for the given key.
        /// </summary>
        /// <param name="messageKey">The key to check.</param>
        /// <returns>True if the message exists, false otherwise.</returns>
        public static bool HasMessage(string messageKey)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
            {
                return false;
            }
            return _messages.ContainsKey(messageKey.ToLower());
        }

        /// <summary>
        /// Retrieves a message for a specific key and language, with optional substitutions.
        /// </summary>
        /// <param name="messageKey">The key to look up the message.</param>
        /// <param name="lang">The language of the message.</param>
        /// <param name="substitutions">Optional array of substitutions to be made in the message.</param>
        /// <param name="substitutionsSeparator">Optional separator for substitutions. Default is ", ".</param>
        /// <returns>The formatted message string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messageKey or lang is null.</exception>
        /// <exception cref="InvalidDataException">Thrown when the language or message key is not found.</exception>
        public static string GetMessage(string messageKey, string lang, string[] substitutions = null, string substitutionsSeparator = ", ")
        {
            if (messageKey == null || lang == null)
            {
                throw new ArgumentNullException($"{nameof(messageKey)} and {nameof(lang)} must not be null");
            }
            int langIndex = Array.FindIndex(LanguageList, x => string.Equals(x, lang, StringComparison.OrdinalIgnoreCase));
            if (langIndex < 0)
            {
                throw new InvalidDataException($"Language {lang} not found in file {_MESSAGES_FILE_PATH}");
            }
            if (!_messages.TryGetValue(messageKey.ToLower(), out List<string> messageRow))
            {
                throw new InvalidDataException($"Message key {messageKey} not found in dictionary");
            }
            substitutions ??= []; // if null assign an empty collection
            string msg = messageRow[langIndex];
            return FillMessageGaps(msg, substitutions, substitutionsSeparator);
        }
    }
}