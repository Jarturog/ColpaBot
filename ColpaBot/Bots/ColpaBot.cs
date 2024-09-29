// This constant should be deleted when the one in the SQLiteEncryptedUserDbHandler is deleted
#define ALLOW_TESTING

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using ColpaBot.Dialogs;
using ColpaBot.DataManagement;
using static ColpaBot.DataManagement.DataUtilites;
using static ColpaBot.DataManagement.BotMessages;
using static ColpaBot.UserProfile;

namespace ColpaBot.Bots
{
    public class ColpaBot<T, S, U> : ActivityHandler where T : Dialog where S : Dialog where U : Dialog
    {
        /// <summary>
        /// Dictionary to store image categories and their corresponding file names.
        /// </summary>
        private readonly static Dictionary<string, HashSet<string>> _IMG_DIC = [];

        /// <summary>
        /// Initializes the image dictionary by reading files from the images directory.
        /// </summary>
        public static void Initialize()
        {
            // Read all files in images directory
            string[] images = Directory.GetFiles(IMAGES_DIRECTORY);
            foreach (string image in images)
            {
                string categoryName = Path.GetFileNameWithoutExtension(image).ToLower();
                string value = Path.GetFileName(image);
                _IMG_DIC[categoryName] = [value];
            }
            string[] imageDirecetories = Directory.GetDirectories(IMAGES_DIRECTORY);
            foreach (string directory in imageDirecetories)
            {
                images = Directory.GetFiles(directory);
                string categoryName = Path.GetFileName(directory).ToLower();
                _IMG_DIC[categoryName] = [];
                foreach (string image in images)
                {
                    string value = Path.GetFileName(image);
                    _IMG_DIC[categoryName].Add(value);
                }
            }
        }

        private Dialog _userProfileDialog;
        private readonly Dialog _questionsDialog, _appointmentDialog;
        private readonly ConversationState _conversationState;
        private readonly UserState _userState;
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;
        private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

        /// <summary>
        /// Initializes a new instance of the ColpaBot class.
        /// </summary>
        public ColpaBot(ConversationState conversationState, UserState userState, T uDialog, S qDialog, U aDialog, ConcurrentDictionary<string, ConversationReference> conversationReferences)
        {
            _conversationState = conversationState;
            _userState = userState;
            _userProfileDialog = uDialog;
            _questionsDialog = qDialog;
            _appointmentDialog = aDialog;
            _userProfileAccessor = _userState.CreateProperty<UserProfile>(nameof(UserProfile));
            _conversationReferences = conversationReferences;
        }

        /// <summary>
        /// Handles incoming message activities.
        /// </summary>
        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            AddConversationReference(turnContext.Activity as Activity);

            UserProfile user = await GetUserProfile(_userProfileAccessor, turnContext, cancellationToken);

            if (await HasUserWrittenACommandAsync(turnContext, cancellationToken))
            {
                return;
            }

            // If user is not in any dialog, show typing indicator. This has to be checked because if i send an activity while the user is in a dialog the retry prompt causes a bug
            if (!user.IsInAnyDialog)
            {
                await turnContext.SendActivityAsync(Activity.CreateTypingActivity(), cancellationToken);
            }

            if (turnContext.Activity.Attachments != null && turnContext.Activity.Attachments.Count > 0)
            {
                string message = GetMessage("messageContainsMultimedia", user.Language ?? Lang.TransformFromBcp47(turnContext.Activity.Locale));
                await turnContext.SendActivityAsync(message, message, InputHints.AcceptingInput, cancellationToken);
                return;
            }

            // Handle different dialog states
            if (!user.UserProfileDialogComplete)
            {
                await _userProfileDialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
                return;
            }
            else if (!user.ChangeAppointmentDialogComplete)
            {
                DialogContext dc = await CreateDialogContextAsync(_appointmentDialog, turnContext, cancellationToken);
                DialogTurnResult result = await dc.ContinueDialogAsync(cancellationToken);
                if (result.Status != DialogTurnStatus.Complete && result.Status != DialogTurnStatus.Cancelled)
                {
                    return;
                }
                if ((bool)result.Result)
                {
                    _userProfileDialog = new UserProfileDialog(_userState);
                    await _userProfileDialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
                }
                return;
            }
            else if (!user.QuestionsDialogComplete)
            {
                DialogContext dc = await CreateDialogContextAsync(_questionsDialog, turnContext, cancellationToken);
                DialogTurnResult result = await dc.ContinueDialogAsync(cancellationToken);
                if (result.Status != DialogTurnStatus.Complete && result.Status != DialogTurnStatus.Cancelled)
                {
                    return;
                }
                if (result.Result is not ValueTuple<FoundChoice, bool> returnValue)
                {
                    throw new Exception("Result from ContinueDialogAsync is not a (FoundChoice, bool)");
                }

                (FoundChoice foundChoice, bool isMiss) = returnValue;
                turnContext.Activity.Text = foundChoice.Value;
                user.ConsecutiveMisses = isMiss ? user.ConsecutiveMisses++ : 0;
            }

            Qna qna = Qna.LangQnaPairs[user.Language];

            int misses = user.ConsecutiveMisses;
            // to add a "see previous messages to obtain an answer in case none is found" behaviour uncomment the next 2 lines. However the quality of the bot's answers is not good enough to use this feature right now
            (Qna.ActionsAndAnswer match, string[] similarQuestions, bool answerFound) = qna.GetAnswer(turnContext.Activity.Text, user.UtcDateAppointment, ref misses, qna.QmAlgorithms[user.CurrentAlgorithm].GetType());//, user.LastPrompt);
            // user.LastPrompt = (turnContext.Activity.Text, answerFound, answerFound ? match.Answer : null);
            user.ConsecutiveMisses = misses;

            await user.SetUserProfile(_userProfileAccessor, turnContext, cancellationToken);

            // if no match was found but there are similar questions, start the questions dialog
            if (!answerFound && similarQuestions.Length > 0)
            {
                DialogContext dc = await CreateDialogContextAsync(_questionsDialog, turnContext, cancellationToken);
                await dc.BeginDialogAsync(_questionsDialog.Id, similarQuestions.ToList(), cancellationToken);
                return;
            }
            string action = match.Actions?[0];
            string response = match.Answer;

            // if there is no special behaviour to process send the message as is
            if (action == null)
            {
                await turnContext.SendActivityAsync(response, response, InputHints.AcceptingInput, cancellationToken);
                return;
            }
            IMessageActivity processedResponse = MessageFactory.Text(response, response, InputHints.AcceptingInput);
            action = action.ToLower();
            switch (action)
            {
                case "appointmentdate": // AppointmentDate
                    response = FillMessageGaps(response, [user.LocalDateAppointment.ToString(DATE_FORMAT), user.LocalDateAppointment.ToString(TIME_FORMAT)]);
                    processedResponse = MessageFactory.Text(response, response, InputHints.AcceptingInput);
                    break;
                case "dontanswer": // DontAnswer
                    return;
                case "noneofthequestionsoption": // noneOfTheQuestionsOption
                    // there should be a reply to the noneOfTheQuestionsOption question. The question is defined in defined both in bot_messages and qna.
                    if (!HasMessage("noneOfTheQuestionsOption"))
                    {
                        throw new Exception($"Code noneOfTheQuestionsOption found in QnA not found in bot messages, which could lead to unexpected behaviour.");
                    }
                    break;
                case "changeappointment": // ChangeAppointment
                    await _appointmentDialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
                    return;
                case "dontshowasexample": // DontShowAsExample
                    break;                // ignore and do not process differently
                default:
                    if (action.StartsWith("copyquestion")) // CopyQuestion...
                    {                                      // ignore and do not process differently
                        break;
                    }
                    if (action.StartsWith("image"))
                    {
                        string image = null;
                        string category = action.Substring("image".Length);
                        if (_IMG_DIC.ContainsKey(category))
                        {
                            HashSet<string> imageNames = _IMG_DIC[category];
                            image = imageNames.ElementAt(new Random().Next(imageNames.Count)); // Selects random image from the category
                        }

                        if (turnContext.Activity.ChannelId == "telegram" || turnContext.Activity.ChannelId == "whatsapp")
                        {
                            var attachments = await GetImageAttachmentAsync(image, category, response);
                            if (attachments != null)
                            {
                                processedResponse = MessageFactory.Attachment(attachments, inputHint: InputHints.AcceptingInput);
                            }
                        }
                        else
                        {
                            processedResponse.Attachments = await GetImageAttachmentAsync(image, category);
                        }
                        break;
                    }
                    throw new Exception($"Action {action} not implemented");
            }
            await turnContext.SendActivityAsync(processedResponse, cancellationToken);
        }

        /// <summary>
        /// Handles the turn logic for the bot.
        /// </summary>
        public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            await base.OnTurnAsync(turnContext, cancellationToken);

            // Save any state changes that might have occurred during the turn.
            await _conversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await _userState.SaveChangesAsync(turnContext, false, cancellationToken);
        }

        /// <summary>
        /// Handles the addition of new members to the conversation.
        /// </summary>
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            // Get the locale
            string language = null;
            if (await DoesUserExist(_userProfileAccessor, turnContext, cancellationToken))
            {
                UserProfile user = await GetUserProfile(_userProfileAccessor, turnContext, cancellationToken);
                language = user.Language;
            }
            language ??= turnContext.Activity.GetLocale() == null
                    ? null
                    : Lang.TransformFromBcp47(turnContext.Activity.GetLocale());
            language ??= Lang.DEFAULT_LANGUAGE;
            string welcomeMessage = GetMessage("welcome", language);

            foreach (ChannelAccount member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(welcomeMessage, welcomeMessage, InputHints.AcceptingInput, cancellationToken);
                    await _userProfileDialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
                }
            }
        }

        /// <summary>
        /// Returns the image attachment with the name imageName and with a possible message.
        /// </summary>
        private static async Task<IList<Attachment>> GetImageAttachmentAsync(string imageName, string category, string message = null)
        {
            const string IMAGE_NOT_FOUND = "image-not-found.png";
            imageName ??= IMAGE_NOT_FOUND;
            string path = Path.Combine(IMAGES_DIRECTORY, imageName);
            if (!File.Exists(path))
            {
                path = Path.Combine(IMAGES_DIRECTORY, category, imageName);
                if (!File.Exists(path))
                {
                    // If not found, set the image-not-found image
                    path = Path.Combine(DATA_DIRECTORY, IMAGE_NOT_FOUND);
                    if (!File.Exists(path))
                    {
                        return null;
                    }
                }
            }

            string imageExtension = Path.GetExtension(path).Substring(1); // Get extension and remove the dot
            string imageData = Convert.ToBase64String(await File.ReadAllBytesAsync(path));

            Attachment image = new("image/" + imageExtension, "data:image/" + imageExtension + $";base64,{imageData}", null, message);

            return [image];
        }

        /// <summary>
        /// Adds or updates the conversation reference for the current activity.
        /// </summary>
        private void AddConversationReference(Activity activity)
        {
            ConversationReference conversationReference = activity.GetConversationReference();
            _conversationReferences.AddOrUpdate(conversationReference.User.Id, conversationReference, (key, newValue) => conversationReference);
        }

        /// <summary>
        /// Handles conversation update activities.
        /// </summary>
        protected override Task OnConversationUpdateActivityAsync(ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            AddConversationReference(turnContext.Activity as Activity);

            return base.OnConversationUpdateActivityAsync(turnContext, cancellationToken);
        }

        /// <summary>
        /// Creates a dialog context for the given dialog.
        /// </summary>
        private async Task<DialogContext> CreateDialogContextAsync(Dialog dialog, ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var dialogStateAccessor = _conversationState.CreateProperty<DialogState>(nameof(DialogState));
            var d = new DialogSet(dialogStateAccessor);
            d.Add(dialog);
            return await d.CreateContextAsync(turnContext, cancellationToken);
        }

        /// <summary>
        /// Checks if the user has written a command and processes it if so.
        /// </summary>
        public async Task<bool> HasUserWrittenACommandAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            string text = turnContext.Activity.Text;
            var attachments = turnContext.Activity.Attachments;
            if (string.IsNullOrEmpty(text) || (attachments != null && attachments.Any()) || !text.StartsWith("/"))
            {
                return false;
            }
            string[] command = text.Split(" ");
            if (command.Length == 0)
            {
                return false;
            }
            UserProfile user = await GetUserProfile(_userProfileAccessor, turnContext, cancellationToken);

            string commandName = command[0].Substring(1).ToLower();
            var commands = new Dictionary<string, Func<Task<string>>>();
            // Define available commands
            commands.Add("help", () =>
            {
                return Task.FromResult($"The available commands are {string.Join(", ", commands.Keys)}");
            });
            commands.Add("restart", async () =>
            {
                await _conversationState.DeleteAsync(turnContext, cancellationToken);
                await user.DeleteUserProfile(_userProfileAccessor, turnContext, cancellationToken);
                return $"The user has been deleted";
            });
            commands.Add("user", () =>
            {
                return Task.FromResult("User id: " + turnContext.Activity.From.Id);
            });
            commands.Add("dev", async () =>
            {
                user.IsDebugging = !user.IsDebugging;
#if ALLOW_TESTING
                await user.SetUserProfile(_userProfileAccessor, turnContext, cancellationToken, true);
#else
                await user.SetUserProfile(_userProfileAccessor, turnContext, cancellationToken);
#endif
                return "Debug mode " + (user.IsDebugging ? "activated" : "deactivated");
            });
            commands.Add("curralg", () =>
            {
                if (!user.UserProfileDialogComplete)
                {
                    return Task.FromResult("First you need to complete the questions");
                }
                var alg = Qna.LangQnaPairs[user.Language].QmAlgorithms[user.CurrentAlgorithm];
                return Task.FromResult($"Current algorithm: {alg.GetType().Name}");
            });
            commands.Add("alg", async () =>
            {
                if (command.Length > 2)
                {
                    return "Format: /alg number";
                }
                if (!user.UserProfileDialogComplete)
                {
                    return "First you need to complete the questions";
                }
                if (command.Length == 1 || !int.TryParse(command[1], out int option))
                {
                    string[] options = Qna.LangQnaPairs[user.Language].QmAlgorithms
                            .Select((a, i) => "\n" + i + " - " + a.GetType().Name)
                            .ToArray();
                    return $"Algorithms: \n{string.Join("\n", options)}";
                }
                if (option < 0 || option >= Qna.LangQnaPairs[user.Language].QmAlgorithms.Length)
                {
                    return $"The algorithm must be between 0 and {Qna.LangQnaPairs[user.Language].QmAlgorithms.Length}";
                }
                user.CurrentAlgorithm = option;
#if ALLOW_TESTING
                await user.SetUserProfile(_userProfileAccessor, turnContext, cancellationToken, true);
#else
                await user.SetUserProfile(_userProfileAccessor, turnContext, cancellationToken);
#endif
                var alg = Qna.LangQnaPairs[user.Language].QmAlgorithms[option];
                return $"Algorithm selected: {alg.GetType().Name}";
            });

            if (commands.ContainsKey(commandName))
            {
                string response = await commands[commandName]();
                await turnContext.SendActivityAsync(response, response, cancellationToken: cancellationToken);
                return true;
            }
            return false;
        }
    }
}
