using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using static ColpaBot.DataManagement.BotMessages;
using System.Collections.Generic;

namespace ColpaBot.Dialogs
{
    /// <summary>
    /// Dialog to present a list of questions to the user for selection.
    /// </summary>
    public class ShowQuestionsDialog : ComponentDialog
    {
        /// <summary>
        /// The list of questions to display to the user.
        /// </summary>
        private List<string> _questions;

        /// <summary>
        /// Tracks whether the user selects the "none of the above" option.
        /// </summary>
        private bool _isMiss;

        /// <summary>
        /// Accessor for managing the user profile state.
        /// </summary>
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShowQuestionsDialog"/> class.
        /// </summary>
        /// <param name="userState">The user state object used to manage user data.</param>
        public ShowQuestionsDialog(UserState userState)
            : base(nameof(ShowQuestionsDialog))
        {
            _userProfileAccessor = userState.CreateProperty<UserProfile>(nameof(UserProfile));

            // Define the waterfall steps that will execute in sequence.
            var waterfallSteps = new WaterfallStep[]
            {
                QuestionsStepAsync,
                LastStepAsync
            };

            // Add the dialogs to the DialogSet. These dialogs will be tracked in dialog state.
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new ChoicePrompt(nameof(ChoicePrompt), QuestionsPromptValidatorAsync));

            // Set the initial dialog to run when this dialog starts.
            InitialDialogId = nameof(WaterfallDialog);
        }

        /// <summary>
        /// Called when the dialog starts. Sets up the list of questions to display.
        /// </summary>
        /// <param name="innerDc">The dialog context.</param>
        /// <param name="options">The options passed to the dialog, expected to be a list of questions.</param>
        /// <param name="cancellationToken">Token to cancel the operation if necessary.</param>
        /// <returns>A task representing the asynchronous operation, returning a <see cref="DialogTurnResult"/>.</returns>
        protected async override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            _questions = options as List<string>;
            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        /// <summary>
        /// The step where the user is shown a list of questions and asked to make a selection.
        /// </summary>
        /// <param name="stepContext">The waterfall step context.</param>
        /// <param name="cancellationToken">Token to cancel the operation if necessary.</param>
        /// <returns>A task representing the asynchronous operation, returning a <see cref="DialogTurnResult"/>.</returns>
        private async Task<DialogTurnResult> QuestionsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user profile and mark the questions dialog as incomplete.
            UserProfile user = await UserProfile.GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            user.QuestionsDialogComplete = false;
            await user.SetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);

            // Add the "None of the above" option to the list of questions.
            string defaultOption = GetMessage("noneOfTheQuestionsOption", user.Language);
            _questions.Add(defaultOption);

            // Prepare the prompt and retry messages, localizing them to the user's language.
            string message = GetMessage("matchAlmostFound", user.Language);
            string retryMessage = GetMessage("retryMatchAlmostFound", user.Language, [_questions.Count.ToString()]);

            // Display the prompt to the user with the list of questions as choices.
            return await stepContext.PromptAsync(nameof(ChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(message),
                    Choices = ChoiceFactory.ToChoices(_questions),
                    RetryPrompt = MessageFactory.Text(retryMessage)
                }, cancellationToken);
        }

        /// <summary>
        /// The final step where the user's selection is processed, and the dialog ends.
        /// </summary>
        /// <param name="stepContext">The waterfall step context.</param>
        /// <param name="cancellationToken">Token to cancel the operation if necessary.</param>
        /// <returns>A task representing the asynchronous operation, returning a <see cref="DialogTurnResult"/>.</returns>
        private async Task<DialogTurnResult> LastStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user profile and mark the questions dialog as complete.
            UserProfile user = await UserProfile.GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            user.QuestionsDialogComplete = true;
            await user.SetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);

            // Package the selected choice and whether it was a "miss" into a return value and end the dialog.
            var returnValue = ((FoundChoice)stepContext.Result, _isMiss);
            return await stepContext.EndDialogAsync(returnValue, cancellationToken);
        }

        /// <summary>
        /// Validator to ensure the user's selection is valid and process any special options.
        /// </summary>
        /// <param name="promptContext">The context for the prompt.</param>
        /// <param name="cancellationToken">Token to cancel the operation if necessary.</param>
        /// <returns>A task representing the asynchronous validation operation, returning a boolean indicating success.</returns>
        private Task<bool> QuestionsPromptValidatorAsync(PromptValidatorContext<FoundChoice> promptContext, CancellationToken cancellationToken)
        {
            // Ensure the choice is valid and process it.
            if (int.TryParse(promptContext.Recognized.Value?.Value, out int indexPlusOne))
            {
                promptContext.Recognized.Value.Value = _questions[indexPlusOne - 1];
            }
            _isMiss = promptContext.Recognized.Value?.Value == _questions[^1]; // it is a miss if the user selects the last option
            return Task.FromResult(promptContext.Recognized.Succeeded);
        }
    }
}