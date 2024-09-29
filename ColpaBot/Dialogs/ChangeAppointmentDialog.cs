using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using static ColpaBot.DataManagement.BotMessages;
using static ColpaBot.Dialogs.DialogUtilities;

namespace ColpaBot.Dialogs
{
    /// <summary>
    /// Dialog for managing the change of an appointment with the user.
    /// </summary>
    public class ChangeAppointmentDialog : ComponentDialog
    {
        /// <summary>
        /// Accessor to handle user profile state.
        /// </summary>
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeAppointmentDialog"/> class.
        /// </summary>
        /// <param name="userState">The state object used to manage user data across turns.</param>
        public ChangeAppointmentDialog(UserState userState)
            : base(nameof(ChangeAppointmentDialog))
        {
            _userProfileAccessor = userState.CreateProperty<UserProfile>(nameof(UserProfile));

            // Define the steps for the waterfall dialog.
            var waterfallSteps = new WaterfallStep[]
            {
                ConfirmationStepAsync,
                LastStepAsync
            };

            // Add the dialogs to the DialogSet.
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new ChoicePrompt(nameof(ConfirmPrompt), ConfirmPromptValidator));

            // Set the initial dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        /// <summary>
        /// Step that asks the user to confirm if they want to change the appointment.
        /// </summary>
        /// <param name="stepContext">The waterfall step context.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A task that represents the asynchronous operation. The result is a <see cref="DialogTurnResult"/>.</returns>
        private async Task<DialogTurnResult> ConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user profile from state and set initial values.
            UserProfile user = await UserProfile.GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            user.ChangeAppointmentDialogComplete = false;
            await user.SetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);

            // Prepare localized messages for confirmation prompt.
            string confirmationQuestion = GetMessage("changeAppointment", user.Language);
            string yesMessage = GetMessage("yes", user.Language);
            string noMessage = GetMessage("no", user.Language);

            // Prompt the user with confirmation choices (Yes or No).
            return await stepContext.PromptAsync(nameof(ConfirmPrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(confirmationQuestion),
                    Choices = CreateChoicesWithNormalizedSynonyms([yesMessage, noMessage])
                }, cancellationToken);
        }

        /// <summary>
        /// Final step that checks the user's response and updates the user profile accordingly.
        /// </summary>
        /// <param name="stepContext">The waterfall step context.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A task that represents the asynchronous operation. The result is a <see cref="DialogTurnResult"/>.</returns>
        private async Task<DialogTurnResult> LastStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user profile from state.
            UserProfile user = await UserProfile.GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            user.ChangeAppointmentDialogComplete = true;

            // if the user wants to keep with the appointment change, do the initial dialog again, if not, check that the user actually said no
            // in which case there is no need to set the user profile again because its value was already true
            string foundChoiceValue = ((FoundChoice)stepContext.Result).Value;
            string yesMessage = GetMessage("yes", user.Language);
            string noMessage = GetMessage("no", user.Language);
            // user.UserProfileDialogComplete is true here.
            if (GetNonNormalizedChoice(foundChoiceValue, [yesMessage]) != null)
                user.UserProfileDialogComplete = false;
            else if (GetNonNormalizedChoice(foundChoiceValue, [noMessage]) == null)
                throw new Exception("The confirmation step for the change appointment dialog failed");

            // Save the user profile to the database if appointment change is confirmed, otherwise just in memory.
            await user.SetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken, !user.UserProfileDialogComplete);

            // If the user doesn't want to change the appointment, send a message.
            if (user.UserProfileDialogComplete)
            {
                string message = GetMessage("dontChangeAppointment", user.Language);
                await stepContext.Context.SendActivityAsync(message, message, cancellationToken: cancellationToken);
            }

            // End the dialog, passing the success state.
            return await stepContext.EndDialogAsync(!user.UserProfileDialogComplete, cancellationToken);
        }

        /// <summary>
        /// Validator for the confirmation prompt.
        /// </summary>
        /// <param name="promptContext">The context for the prompt.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        private Task<bool> ConfirmPromptValidator(PromptValidatorContext<FoundChoice> promptContext, CancellationToken cancellationToken)
        {
            // Return true if the prompt was successfully recognized.
            return Task.FromResult(promptContext.Recognized.Succeeded);
        }
    }
}