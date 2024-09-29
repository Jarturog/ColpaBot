using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System;
using System.IO;
using System.Collections.Generic;

namespace ColpaBot.DataManagement
{
    public static class DataUtilites
    {
        /// <summary>
        /// The base directory for all data resources.
        /// </summary>
        public static readonly string DATA_DIRECTORY = @"Resources";

        /// <summary>
        /// Directory for storing image files.
        /// </summary>
        public static readonly string IMAGES_DIRECTORY = Path.Combine(DATA_DIRECTORY, "Images");

        /// <summary>
        /// Directory for storing questions and answers files.
        /// </summary>
        public static readonly string QNAS_DIRECTORY = Path.Combine(DATA_DIRECTORY, "QuestionsAndAnswers");

        /// <summary>
        /// Directory for storing synonym files.
        /// </summary>
        public static readonly string SYNONYMS_DIRECTORY = Path.Combine(DATA_DIRECTORY, "Synonyms");

        /// <summary>
        /// Directory for storing vector files.
        /// </summary>
        public static readonly string VECTORS_DIRECTORY = Path.Combine(DATA_DIRECTORY, "Vectors");

        /// <summary>
        /// File extension for sheet files.
        /// </summary>
        public const string SHEET_EXTENSION = "tsv";

        /// <summary>
        /// File extension for vector files.
        /// </summary>
        public const string VECTORS_EXTENSION = "vec";

        /// <summary>
        /// Gets the separator character based on the sheet extension.
        /// </summary>
        public static char Separator => SHEET_EXTENSION.Equals("tsv") ? '\t' : throw new InvalidDataException($"Invalid QnA file extension. It its .{SHEET_EXTENSION} but it should be .tsv");

        private const string _COMMENT_PREFIX = "//";
        public const string FORMAT_CHARS = "{}", INDEFINITE_FORMAT_CHARS = "{{}}";
        public const string COPY_ANSWERS_CHARS = ".";

        // Maximum image dimensions allowed by Telegram
        private const int _MAXIMUM_PIXELS = 1279;

        /// <summary>
        /// Initializes the data utilities and validates image sizes.
        /// </summary>
        public static void Initialize()
        {
            List<string> paths = new(Directory.GetFiles(IMAGES_DIRECTORY));
            foreach (string directory in Directory.GetDirectories(IMAGES_DIRECTORY))
            {
                paths.AddRange(Directory.GetFiles(directory));
            }
            foreach (string path in paths)
            {
                using Image<Rgba32> image = Image.Load<Rgba32>(path);
                // Check the size of the image
                if (image.Width > _MAXIMUM_PIXELS || image.Height > _MAXIMUM_PIXELS)
                {
                    throw new Exception($"The image '{Path.GetFileName(path)}' is too wide ({image.Width} px) or too tall ({image.Height} px). The maximum are images of {_MAXIMUM_PIXELS}x{_MAXIMUM_PIXELS} pixels");
                }
            }
        }

        /// <summary>
        /// Gets the file path for a given filename and extensions in the specified directory.
        /// </summary>
        /// <param name="fileName">The name of the file without extension.</param>
        /// <param name="extensions">An array of possible file extensions.</param>
        /// <param name="directory">The directory to search in.</param>
        /// <returns>The full file path if found, otherwise null.</returns>
        public static string GetFilePath(string fileName, string[] extensions, string directory)
        {
            foreach (string ext in extensions)
            {
                string path = Path.Combine(directory, $"{fileName}.{ext}");
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        /// <summary>
        /// Fills in placeholders in a message with provided substitutions.
        /// </summary>
        /// <param name="message">The original message with placeholders.</param>
        /// <param name="substitutions">An array of substitution strings.</param>
        /// <param name="separator">The separator to use for multiple substitutions.</param>
        /// <returns>The message with placeholders replaced by substitutions.</returns>
        public static string FillMessageGaps(string message, string[] substitutions, string separator = ", ")
        {
            if (substitutions == null)
            {
                throw new ArgumentNullException($"Tried to substitute message '{message}' with null substitutions");
            }
            string originalMessage = message, leftPart, rightPart;
            for (int i = 0; i < substitutions.Length; i++)
            {
                int posSubstitution = message.IndexOf(FORMAT_CHARS);
                int posMultipleSubstitutions = message.IndexOf(INDEFINITE_FORMAT_CHARS);
                if (posSubstitution < 0 && posMultipleSubstitutions < 0)
                {
                    throw new InvalidDataException($"Message {originalMessage} does not contain enough format characters {FORMAT_CHARS} or {INDEFINITE_FORMAT_CHARS} for the substitutions");
                }
                // Handle multiple substitutions
                else if (posMultipleSubstitutions >= 0 && (posSubstitution < 0 || posSubstitution >= posMultipleSubstitutions))
                {
                    string injection = "";
                    if (separator == "\n")
                    {
                        injection += separator + separator;
                    }
                    for (int j = i; j < substitutions.Length; j++)
                    {
                        injection += substitutions[j] + separator;
                    }
                    injection = injection.Substring(0, injection.Length - separator.Length); // remove last separator
                    if (separator == "\n")
                    {
                        injection += separator + separator;
                    }
                    leftPart = message.Substring(0, posMultipleSubstitutions);
                    rightPart = message.Substring(posMultipleSubstitutions + INDEFINITE_FORMAT_CHARS.Length);
                    if (separator == "\n")
                    {
                        rightPart = rightPart.Trim();
                    }
                    message = leftPart + injection + rightPart;
                    break;
                }
                // Handle single substitution
                leftPart = message.Substring(0, posSubstitution);
                rightPart = message.Substring(posSubstitution + FORMAT_CHARS.Length);
                message = leftPart + substitutions[i] + rightPart;
            }
            if (message.Contains(FORMAT_CHARS) || message.Contains(INDEFINITE_FORMAT_CHARS))
            {
                throw new InvalidDataException($"Message {originalMessage} received less substitutions ({substitutions.Length}) than it really needed");
            }
            return message;
        }

        /// <summary>
        /// Reads the next non-empty and non-comment line from the StreamReader.
        /// </summary>
        /// <param name="reader">The StreamReader to read from.</param>
        /// <returns>The next valid line, or null if end of stream is reached.</returns>
        public static string SkipAndReadLine(StreamReader reader)
        {
            string line = reader.ReadLine();
            while (line != null && (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith(_COMMENT_PREFIX)))
            {
                line = reader.ReadLine();
            }
            return line;
        }
    }
}