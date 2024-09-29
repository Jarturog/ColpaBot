using System;
using System.IO;
using System.Linq;
using static ColpaBot.DataManagement.DataUtilites;

namespace ColpaBot.DataManagement
{
    public class Lang()
    {
        public const string DEFAULT_LANGUAGE = "DE"; // German
        private static readonly string _LANGUAGES_FILE_PATH = GetFilePath("languages", [SHEET_EXTENSION], DATA_DIRECTORY);

        /// <summary>
        /// List of language codes.
        /// </summary>
        public static string[] LanguageList { get; set; }

        /// <summary>
        /// List of language names in their native form.
        /// </summary>
        public static string[] LanguageNameList { get; set; }

        /// <summary>
        /// List of language names in English.
        /// </summary>
        private static string[] _languageEnglishNameList { get; set; }

        /// <summary>
        /// Reads the list of used languages from the disk.
        /// </summary>
        /// <returns>An array of language codes.</returns>
        public static string[] ReadUsedLanguageListFromDisk()
        {
            using StreamReader reader = new(_LANGUAGES_FILE_PATH);
            return SkipAndReadLine(reader).Split(Separator);
        }

        /// <summary>
        /// Initializes the language lists.
        /// </summary>
        public static void Initialize()
        {
            using StreamReader reader = new(_LANGUAGES_FILE_PATH);
            LanguageList = SkipAndReadLine(reader).Split(Separator); // languages with dialects
            if (!LanguageList.Contains(DEFAULT_LANGUAGE))
            {
                throw new InvalidDataException($"Default language {DEFAULT_LANGUAGE} not found in the list of languages");
            }
            string[] allPossibleLanguageList = SkipAndReadLine(reader).Split(Separator); // skip total amount of languages
            string[] allPossibleLanguageNameList = SkipAndReadLine(reader).Split(Separator);
            string[] allPossibleLanguageEnglishNameList = SkipAndReadLine(reader).Split(Separator);

            // only get the languages names of the languages that are in LanguageList
            LanguageNameList = new string[LanguageList.Length];
            _languageEnglishNameList = new string[LanguageList.Length];
            for (int i = 0; i < LanguageList.Length; i++)
            {
                int j = Array.IndexOf(allPossibleLanguageList, LanguageList[i]);
                if (j >= 0)
                {
                    LanguageNameList[i] = allPossibleLanguageNameList[j];
                    _languageEnglishNameList[i] = allPossibleLanguageEnglishNameList[j];
                }
            }
        }

        /// <summary>
        /// Converts a language name to its corresponding language code.
        /// </summary>
        /// <param name="langName">The language name to convert.</param>
        /// <param name="hasDialect">Whether the language name includes a dialect.</param>
        /// <returns>The corresponding language code.</returns>
        public static string NameToCode(string langName, bool hasDialect = true)
        {
            if (!hasDialect)
            {
                string[] langs = ObtainDialect(langName, false);
                if (langs.Length > 0)
                {
                    langName = langs[0];
                }
            }
            int index = Array.IndexOf(LanguageNameList, langName);
            if (index < 0)
            {
                index = Array.IndexOf(_languageEnglishNameList, langName);
            }
            if (index < 0)
            {
                throw new InvalidDataException($"Language {langName} not found in the list of languages");
            }
            return LanguageList[index];
        }

        /// <summary>
        /// Checks if a language is in the list of supported languages.
        /// </summary>
        /// <param name="lang">The language to check.</param>
        /// <param name="hasDialect">Whether the language includes a dialect.</param>
        /// <returns>True if the language is supported, false otherwise.</returns>
        public static bool Contains(string lang, bool hasDialect = true)
        {
            if (hasDialect)
            {
                return LanguageList.Contains(lang) || LanguageNameList.Contains(lang) || _languageEnglishNameList.Contains(lang);
            }
            return LanguageList.Any(l => RemoveDialect(l, true) == lang) || LanguageNameList.Any(l => RemoveDialect(l, false) == lang) || _languageEnglishNameList.Any(l => RemoveDialect(l, false) == lang);
        }

        /// <summary>
        /// Gets the list of language names for given language codes.
        /// </summary>
        /// <param name="languages">Array of language codes.</param>
        /// <param name="hideDialect">Whether to hide the dialect in the returned names.</param>
        /// <returns>Array of language names.</returns>
        public static string[] GetLanguageNameList(string[] languages, bool hideDialect = false)
        {
            string[] languageNameList = new string[languages.Length];
            for (int i = 0; i < languages.Length; i++)
            {
                int index = Array.IndexOf(LanguageList, languages[i]);
                if (index < 0)
                {
                    throw new InvalidDataException($"Language {languages[i]} not found in the list of possible languages");
                }
                languageNameList[i] = hideDialect
                    ? RemoveDialect(LanguageNameList[index], false)
                    : LanguageNameList[index];
            }
            return languageNameList;
        }

        /// <summary>
        /// Transforms a language code to ISO 639 format.
        /// </summary>
        /// <param name="lang">The language code to transform.</param>
        /// <returns>The language code in ISO 639 format.</returns>
        public static string TransformToIso639(string lang)
        {
            // lower case and remove dialects
            return RemoveDialect(lang, true).ToLower();
        }

        /// <summary>
        /// Removes the dialect from a language code or name.
        /// </summary>
        /// <param name="lang">The language code or name.</param>
        /// <param name="isLangCodeNotIsLangName">True if lang is a language code, false if it's a language name.</param>
        /// <returns>The language code or name without dialect.</returns>
        public static string RemoveDialect(string lang, bool isLangCodeNotIsLangName)
        {
            if (isLangCodeNotIsLangName)
            {
                return lang.Split('-')[0];
            }
            int dialectIndex = lang.IndexOf('(') - 1;
            if (dialectIndex < 0)
            {
                dialectIndex = lang.Length;
            }
            return lang.Substring(0, dialectIndex);
        }

        /// <summary>
        /// Obtains the dialect(s) for a given language.
        /// </summary>
        /// <param name="lang">The language code or name.</param>
        /// <param name="isLangCodeNotIsLangName">True if lang is a language code, false if it's a language name.</param>
        /// <returns>An array of dialects for the given language.</returns>
        private static string[] ObtainDialect(string lang, bool isLangCodeNotIsLangName)
        {
            if (isLangCodeNotIsLangName)
            {
                return LanguageList.Where(l => RemoveDialect(l, true) == lang).ToArray();
            }
            string[] langs = LanguageNameList.Where(l => RemoveDialect(l, false) == lang).ToArray();
            if (langs.Length > 0)
            {
                return langs;
            }
            return _languageEnglishNameList.Where(l => RemoveDialect(l, false) == lang).ToArray();
        }

        /// <summary>
        /// Transforms a BCP 47 language tag to the corresponding language code used in this system.
        /// </summary>
        /// <param name="bcp47Lang">The BCP 47 language tag.</param>
        /// <returns>The corresponding language code, or null if not found.</returns>
        public static string TransformFromBcp47(string bcp47Lang)
        {
            foreach (string lang in LanguageList)
            {
                if (lang.StartsWith(bcp47Lang, StringComparison.OrdinalIgnoreCase))
                {
                    return lang;
                }
            }
            return null;
        }
    }
}