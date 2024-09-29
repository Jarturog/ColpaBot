using System.Collections.Generic;
using System;
using System.IO;
using ColpaBot.NaturalLanguageProcessing;
using System.Linq;
using static ColpaBot.DataManagement.DataUtilites;
using static ColpaBot.NaturalLanguageProcessing.QuestionMatchingAlgorithm;
using static ColpaBot.NaturalLanguageProcessing.TextProcessingUtilities;
using static ColpaBot.DataManagement.BotMessages;

namespace ColpaBot.DataManagement
{
    /// <summary>
    /// Represents a question and answer (QnA) system.
    /// </summary>
    public class Qna
    {
        /// <summary>
        /// Struct to hold actions and answers for a specific question.
        /// </summary>
        public readonly struct ActionsAndAnswer
        {
            /// <summary>
            /// The actions associated with the question. The first one can be the important one to process; the rest are flags.
            /// </summary>
            public readonly string[] Actions;

            /// <summary>
            /// The answer to the question.
            /// </summary>
            public readonly string Answer;

            /// <summary>
            /// Initializes a new instance of the ActionsAndAnswer struct.
            /// </summary>
            /// <param name="actions">The actions associated with the question.</param>
            /// <param name="answer">The answer to the question.</param>
            public ActionsAndAnswer(string[] actions, string answer)
            {
                Actions = actions;
                Answer = answer;
            }

            // Equality operators and overrides
            public static bool operator ==(ActionsAndAnswer left, ActionsAndAnswer right) => left.Equals(right);
            public static bool operator !=(ActionsAndAnswer left, ActionsAndAnswer right) => !(left == right);

            public override bool Equals(object obj)
            {
                if (obj is ActionsAndAnswer other)
                {
                    return Actions.SequenceEqual(other.Actions) && Answer == other.Answer;
                }
                return false;
            }

            public override int GetHashCode() => (Actions, Answer).GetHashCode();
        }

        /// <summary>
        /// Gets the file path for the QnA data.
        /// </summary>
        private string QnaFilePath => GetFilePath("questions_and_answers_" + _language, [SHEET_EXTENSION], QNAS_DIRECTORY);

        /// <summary>
        /// The language for the QnA.
        /// </summary>
        private readonly string _language;

        /// <summary>
        /// Number of answers to select from in case of not finding the exact answer.
        /// </summary>
        private const int _NUM_ANSWERS_TO_CHOOSE_FROM = 3;

        /// <summary>
        /// Sections based on days before examination.
        /// </summary>
        private Dictionary<int, Dictionary<string, ActionsAndAnswer>> _daySpecificSections;

        /// <summary>
        /// General QnA section.
        /// </summary>
        private Dictionary<string, ActionsAndAnswer> _generalSection;

        /// <summary>
        /// Array of matching algorithms.
        /// </summary>
        public QuestionMatchingAlgorithm[] QmAlgorithms { get; private set; }

        /// <summary>
        /// Dictionary of language-QnA pairs.
        /// </summary>
        public static readonly Dictionary<string, Qna> LangQnaPairs = [];

        /// <summary>
        /// Normalized vocabulary.
        /// </summary>
        public string[] NormalizedVocabulary { get; private set; } = [];

        /// <summary>
        /// Normalized questions.
        /// </summary>
        public string[] NormalizedQuestions { get; private set; } = [];

        /// <summary>
        /// Flag to check if the Qna class has been initialized.
        /// </summary>
        public static bool IsQnaClassInitialized { get; private set; } = false;

        /// <summary>
        /// Initializes the QnA system and loads language-specific data.
        /// </summary>
        public static void Initialize()
        {
            foreach (string lang in Lang.ReadUsedLanguageListFromDisk())
            {
                LangQnaPairs[lang] = ParseQna(lang);
                string[] questions = GetNormalizedQuestions(LangQnaPairs[lang].GetRawQuestions());
                LangQnaPairs[lang].NormalizedQuestions = questions;
                LangQnaPairs[lang].NormalizedVocabulary = GetNormalizedVocabulary(questions);
                List<QuestionMatchingAlgorithm> list = [new Bm25(lang), new Tfidf(lang), new Jaccard(lang), new LevenshteinDistance(lang)];
                // Uncomment the following line to add SentenceEmbedding if supported
                // try { list.Add(new SentenceEmbedding(lang)); } catch (LanguageNotSupportedException) { }
                LangQnaPairs[lang].QmAlgorithms = list.ToArray();
            }
            IsQnaClassInitialized = true;
        }

        /// <summary>
        /// Initializes a new instance of the Qna class.
        /// </summary>
        /// <param name="language">The language for the QnA instance.</param>
        private Qna(string language)
        {
            _language = language;
        }

        /// <summary>
        /// Parses the QnA data from a file for a specific language.
        /// </summary>
        /// <param name="language">The language of the QnA data.</param>
        /// <returns>A Qna object containing the parsed data.</returns>
        private static Qna ParseQna(string language)
        {
            Qna qna = new(language);
            using StreamReader reader = new(qna.QnaFilePath);
            string line;

            // Helper function to process a QnA section
            Dictionary<string, ActionsAndAnswer> ProcessQnaSection()
            {
                Dictionary<string, ActionsAndAnswer> qnaSection = [];
                static string message(string line) => $"Invalid QnA data format caused after line: {line}";
                line = "Beginning of a section";
                line = SkipAndReadLine(reader) ?? throw new InvalidDataException(message(line));
                string[] parts;
                string previousAnswer = line;

                while (line != null && (parts = line.Split(Separator)).Length == 3)
                {
                    if (parts[2].Equals(COPY_ANSWERS_CHARS))
                    {
                        parts[2] = previousAnswer;
                    }
                    else
                    {
                        previousAnswer = parts[2];
                    }
                    if (string.IsNullOrWhiteSpace(parts[0]))
                    {
                        parts[0] = null;
                    }
                    string answer = parts[2];
                    string[] actions = parts[0]?.Split(' ');
                    (string Question, string[] Actions)[] questionsWithActions = [(parts[1], actions)];
                    // if the generic section has been initialized and the questions has as its first action a copy question one
                    if (qna._generalSection != null && actions != null && actions.Length > 0 && actions.Any(action => action.StartsWith("CopyQuestion", StringComparison.CurrentCultureIgnoreCase)))
                    {
                        if (parts[1] != COPY_ANSWERS_CHARS)
                        {
                            throw new Exception($"Every question with the action CopyQuestion should have a {COPY_ANSWERS_CHARS} as the question. The line {line} does not");
                        }

                        string[] copyQuestionActions = actions.Where(action => action.StartsWith("CopyQuestion", StringComparison.CurrentCultureIgnoreCase)).ToArray();
                        if (copyQuestionActions.Length > 1)
                        {
                            throw new Exception($"There should only be one action that starts with CopyQuestion. More than one found in {line}");
                        }
                        actions = actions.Where(action => !action.StartsWith("CopyQuestion", StringComparison.CurrentCultureIgnoreCase)).ToArray();
                        string action = copyQuestionActions.First();
                        // subsitute the COPY_ANSWERS_CHARS question with the questions that have the same action
                        questionsWithActions = qna._generalSection
                            .Where(kvp => kvp.Value.Actions != null && kvp.Value.Actions.Contains(action)) // Filters the collection
                            .Select(kvp => 
                                // also copy the non-image actions
                                (kvp.Key, actions
                                    .Concat(kvp.Value.Actions
                                        .Where(action => !action.StartsWith("image", StringComparison.CurrentCultureIgnoreCase)
                                            && !action.StartsWith("copyQuestion", StringComparison.CurrentCultureIgnoreCase)))
                                    .ToArray())
                            )
                            .ToArray();
                    }

                    foreach ((string Question, string[] Actions) in questionsWithActions)
                    {
                        qnaSection[Question] = new ActionsAndAnswer(Actions, answer);
                    }
                    line = SkipAndReadLine(reader);
                }
                return qnaSection;
            }
            
            line = SkipAndReadLine(reader); // read format chars
            if (!line.Equals(FORMAT_CHARS))
            {
                throw new InvalidDataException($"Invalid format characters in file {qna.QnaFilePath}. Expected {FORMAT_CHARS} but found {line}");
            }
            line = SkipAndReadLine(reader); // read copy answers chars
            if (!line.Equals(COPY_ANSWERS_CHARS))
            {
                throw new InvalidDataException($"Invalid format characters in file {qna.QnaFilePath}. Expected {COPY_ANSWERS_CHARS} but found {line}");
            }

            qna._generalSection = ProcessQnaSection();
            Dictionary<string, ActionsAndAnswer> lastProcessedSection = qna._generalSection;
            qna._daySpecificSections = [];

            // Process day-specific sections
            while (line != null)
            {
                if (!int.TryParse(line, out int daysBeforeExamination))
                {
                    string[] parts = line.Split(Separator);
                    if (parts.Length == 2 && parts[1].Equals(COPY_ANSWERS_CHARS) && int.TryParse(parts[0], out daysBeforeExamination))
                    {
                        qna._daySpecificSections[daysBeforeExamination] = lastProcessedSection;
                        line = SkipAndReadLine(reader);
                        continue;
                    }
                    throw new InvalidDataException($"The beginning of a section should be started with a number of days, {line} was found.");
                }
                qna._daySpecificSections[daysBeforeExamination] = lastProcessedSection = ProcessQnaSection();
            }
            return qna;
        }

        /// <summary>
        /// Gets the answer for a given question based on the current date and appointment date.
        /// </summary>
        /// <param name="currentQuestion">The question to find an answer for.</param>
        /// <param name="utcAppointment">The UTC date of the appointment.</param>
        /// <param name="consecutiveMisses">The number of consecutive misses.</param>
        /// <param name="algorithm">The algorithm type to use for matching.</param>
        /// <param name="lastAskedQuestion">The last asked question and its details.</param>
        /// <returns>A tuple containing the matched answer, similar questions, and a flag indicating if an answer was found.</returns>
        public (ActionsAndAnswer Match, string[] SomewhatSimilarQuestions, bool AnswerFound) GetAnswer(string currentQuestion, DateTime utcAppointment, ref int consecutiveMisses, Type algorithm = null, (string Text, bool HasAnswer, string Answer) lastAskedQuestion = default)
        {
            int daysBeforeExamination = GetDaysBeforeExamination(utcAppointment);
#if IS_TESTING
            Console.WriteLine($"Appointment UTC: {utcAppointment}");
#endif
            if (algorithm == null)
            {
                algorithm = QmAlgorithms.FirstOrDefault().GetType();
            }
            if (algorithm.BaseType != typeof(QuestionMatchingAlgorithm))
            {
                throw new ArgumentException("The algorithm must be a subclass of " + nameof(QuestionMatchingAlgorithm));
            }
            QuestionMatchingAlgorithm questionMatchingAlg = QmAlgorithms
                .Where(alg => alg.GetType() == algorithm)
                .First();

            var results = GetBestMatchesAndAnswers(currentQuestion, questionMatchingAlg, daysBeforeExamination);
#if IS_TESTING
            Console.WriteLine($"Best results found with {algorithm.Name} for {currentQuestion} with its score:");
            results.ForEach(r => Console.WriteLine($"{r.Match.Score.ToString()[..Math.Min(5, r.Match.Score.ToString().Length)]} {r.Match.Text}"));
#endif
            // Combine current question with the previous one if necessary
            bool hasAQuestionBeenAskedBefore = lastAskedQuestion != default && lastAskedQuestion.Text?.Length > 0;
            bool hasTheCurrentQuestionAnAnswer = results.Count > 0 && results.First().Match != default && results.First().Match.PassesAcceptanceThreshold(questionMatchingAlg);
            // only try to improve the current answer considering the previous messages if the question does not have a good enough answer
            if (hasAQuestionBeenAskedBefore && !hasTheCurrentQuestionAnAnswer)
            {
                // try to combine the last question with the current one and check the results
                var newResults = GetBestMatchesAndAnswers(currentQuestion + " " + lastAskedQuestion, questionMatchingAlg, daysBeforeExamination);
                var bestNewResult = newResults.FirstOrDefault();
                bool areBothAnswersTheSame = lastAskedQuestion.Answer != default && lastAskedQuestion.Answer == bestNewResult.Match.Text;
                // if the previous answer was successful, check that the new one is not the same as the previous one
                if (bestNewResult != default && lastAskedQuestion.HasAnswer && !areBothAnswersTheSame && bestNewResult.Match.Text != GetMessage("noneOfTheQuestionsOption", _language))
                {
                    // because lastAskedQuestion.HasAnswer is true, bestOldResult is not default
                    // get a merged results sorted by score
                    results = newResults
                        .Concat(results)
                        .DistinctBy(r => r.Match.Text)
                        .ToList();
                    results.Sort((r1, r2) => r2.Match.Score.CompareTo(r1.Match.Score));
                    results = results
                        .Take(_NUM_ANSWERS_TO_CHOOSE_FROM)
                        .ToList();
#if IS_TESTING
                    Console.WriteLine($"Best new results found with {algorithm.Name} for {currentQuestion} with its score:");
                    results.ForEach(r => Console.WriteLine($"{r.Match.Score.ToString()[..Math.Min(5, r.Match.Score.ToString().Length)]} {r.Match.Text}"));
#endif
                }
            }

            if (results.Count == 0) // none passed the minimum threshold
            {
                consecutiveMisses++;
                if (consecutiveMisses <= UserProfile.MAX_MISSES_BEFORE_GIVING_OPTIONS)
                {
                    return (new ActionsAndAnswer(null, GetMessage("matchNotFound", _language)), [], false);
                }
                // Suggest questions the user could ask
                const int AMOUNT_OF_QUESTIONS_TO_SHOW = 3;
                List<string> suggestedQuestions = new(AMOUNT_OF_QUESTIONS_TO_SHOW);
                HashSet<Dictionary<string, ActionsAndAnswer>> qnaSections = [_daySpecificSections.GetValueOrDefault(daysBeforeExamination, _generalSection), _generalSection];
                Random random = new();
                while (suggestedQuestions.Count < AMOUNT_OF_QUESTIONS_TO_SHOW)
                {
                    if (_daySpecificSections.TryGetValue(daysBeforeExamination, out Dictionary<string, ActionsAndAnswer> section))
                    {
                        qnaSections.Add(section);
                    }
                    Dictionary<string, ActionsAndAnswer> selectedSection = qnaSections.ToArray()[random.Next(qnaSections.Count)];
                    string[] sectionQuestions = selectedSection.Keys.ToArray();
                    string q = sectionQuestions[random.Next(sectionQuestions.Length)];
                    bool exitLoop = true;
                    for (int i = 0; i < 16; i++) // if after an arbitrary number of tries, it still can't find a question that is not similar 
                    {                            // to the ones already selected, exit the loop to avoid infinite loop
                        bool canBeShown = selectedSection[q].Actions != null && !selectedSection[q].Actions.Any(action => action.Equals("DontShowAsExample", StringComparison.OrdinalIgnoreCase));
                        bool hasAlreadyBeenSelected = ContainsSynonymQuestion(q, suggestedQuestions, selectedSection);
                        if (!hasAlreadyBeenSelected && canBeShown) // if it is eligible to be added to suggestedQuestions
                        {
                            exitLoop = false;
                            break;
                        }
                        q = sectionQuestions[random.Next(sectionQuestions.Length)];
                    }
                    if (exitLoop)
                    {
                        break;
                    }
                    suggestedQuestions.Add(q);
                }
                suggestedQuestions = suggestedQuestions.Select(q => " - " + q).ToList(); // add a bullet point to each question
                string message = GetMessage("matchNotFoundPlusExamples", _language, suggestedQuestions.ToArray(), "\n");
                return (new ActionsAndAnswer(null, message), [], false); // send a "no answer was found" message
            }
            else if (results.First().Match.PassesAcceptanceThreshold(questionMatchingAlg)) // the best one passes the acceptance threshold
            {
                consecutiveMisses = 0;
                return (results.First().ActionsAndAnswer, [], true);
            }
            else // the answers are between the minimum and the acceptance thresholds
            {
                return (default, results.Select(r => r.Match.Text).ToArray(), false);
            }
        }

        /// <summary>
        /// Gets the most similar question based on the current question and appointment date.
        /// </summary>
        /// <param name="currentQuestion">The question to find a match for.</param>
        /// <param name="utcAppointment">The UTC date of the appointment.</param>
        /// <param name="algorithm">The algorithm type to use for matching.</param>
        /// <returns>The most similar question match.</returns>
        public QuestionMatch GetMostSimilarQuestion(string currentQuestion, DateTime utcAppointment, Type algorithm = null)
        {
            int daysBeforeExamination = GetDaysBeforeExamination(utcAppointment);

            if (algorithm == null)
            {
                algorithm = QmAlgorithms.FirstOrDefault().GetType();
            }
            if (algorithm.BaseType != typeof(QuestionMatchingAlgorithm))
            {
                throw new ArgumentException("The algorithm must be a subclass of " + nameof(QuestionMatchingAlgorithm));
            }
            QuestionMatchingAlgorithm questionMatchingAlg = QmAlgorithms
                .Where(alg => alg.GetType() == algorithm)
                .First();

            var results = GetBestMatchesAndAnswers(currentQuestion, questionMatchingAlg, daysBeforeExamination);

            QuestionMatch result = results.FirstOrDefault().Match;

            return result.PassesAcceptanceThreshold(questionMatchingAlg)
                ? result
                : default;
        }

        /// <summary>
        /// Gets the best matches and answers for a given question.
        /// </summary>
        /// <param name="question">The question to find matches for.</param>
        /// <param name="questionMatchingAlg">The question matching algorithm to use.</param>
        /// <param name="daysBeforeExamination">The number of days before the examination.</param>
        /// <returns>A list of tuples containing question matches and their corresponding actions and answers.</returns>
        private List<(QuestionMatch Match, ActionsAndAnswer ActionsAndAnswer)> GetBestMatchesAndAnswers(string question, QuestionMatchingAlgorithm questionMatchingAlg, int daysBeforeExamination)
        {
            Dictionary<string, ActionsAndAnswer> relevantQna = _generalSection;
            string[] questions = relevantQna.Keys.ToArray();
            List<(QuestionMatch Match, ActionsAndAnswer)> results = [];

            foreach (QuestionMatch match in questionMatchingAlg.FindMostSimilar(question, questions, _NUM_ANSWERS_TO_CHOOSE_FROM))
            {
                if (match.PassesMinThreshold(questionMatchingAlg))
                {
                    results.Add((match, relevantQna[match.Text]));
                }
            }

            if (_daySpecificSections.TryGetValue(daysBeforeExamination, out Dictionary<string, ActionsAndAnswer> value))
            {
                Dictionary<string, ActionsAndAnswer> specificQna = value;
                questions = specificQna.Keys.ToArray();
                List<(QuestionMatch Match, ActionsAndAnswer)> specificResults = [];

                foreach (QuestionMatch match in questionMatchingAlg.FindMostSimilar(question, questions, _NUM_ANSWERS_TO_CHOOSE_FROM))
                {
                    if (match.PassesMinThreshold(questionMatchingAlg))
                    {
                        specificResults.Add((match, specificQna[match.Text]));
                    }
                }
                // Place specific results first for priority in sorting
                results = specificResults.Concat(results).ToList();
                // Sort by the best results first
                results.Sort((r1, r2) => r2.Match.Score.CompareTo(r1.Match.Score));
            }
            return results.Distinct().ToList();
        }

        /// <summary>
        /// Checks if a list of questions contains a synonym question with the same answer.
        /// </summary>
        /// <param name="question">The question to check.</param>
        /// <param name="questions">The list of questions to search in.</param>
        /// <param name="qna">The dictionary of questions and answers.</param>
        /// <returns>True if a synonym question is found, false otherwise.</returns>
        private static bool ContainsSynonymQuestion(string question, List<string> questions, Dictionary<string, ActionsAndAnswer> qna)
        {
            if (!qna.TryGetValue(question, out ActionsAndAnswer aaa))
            {
                return false;
            }
            foreach (string q in questions)
            {
                if (qna.GetValueOrDefault(q, default) == aaa)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Calculates the number of days before the examination based on the appointment date.
        /// </summary>
        /// <param name="utcAppointment">The UTC date of the appointment.</param>
        /// <returns>The number of days before the examination, or -1 if the appointment is in the past.</returns>
        private static int GetDaysBeforeExamination(DateTime utcAppointment)
        {
            return utcAppointment > DateTime.UtcNow ? (int)(utcAppointment.Date - DateTime.UtcNow.Date).TotalDays : -1;
        }

        /// <summary>
        /// Gets the raw questions from the QnA system.
        /// </summary>
        /// <param name="utcAppointment">The UTC date of the appointment.</param>
        /// <param name="allowRepeatedQuestions">Whether to allow repeated questions in the result.</param>
        /// <returns>An array of raw questions.</returns>
        public string[] GetRawQuestions(DateTime utcAppointment = default, bool allowRepeatedQuestions = true)
        {
            List<string> questions = _generalSection.Keys.ToList();
            int daysBeforeExamination = GetDaysBeforeExamination(utcAppointment);
            var daySpecificQuestions = utcAppointment == default
                ? _daySpecificSections
                : _daySpecificSections.Where(kvp => kvp.Key == GetDaysBeforeExamination(utcAppointment));

            questions.AddRange(daySpecificQuestions.SelectMany(kvp => kvp.Value.Keys));
            return allowRepeatedQuestions
                ? questions.ToArray()
                : questions.Distinct().ToArray();
        }
    }
}