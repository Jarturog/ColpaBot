using ColpaBot.DataManagement;
using Microsoft.Bot.Builder;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.TraceExtensions;
using System.IO;
using Microsoft.Bot.Schema;

namespace ColpaBot.Adapters
{
    /// <summary>
    /// Utility class containing static methods for error handling in bot adapter.
    /// </summary>
    public static class AdapterUtilities
    {
        /// <summary>
        /// Handles any exceptions thrown during the bot's turn by logging the error,
        /// sending a message to the user, and writing the error details to a log file.
        /// </summary>
        /// <param name="turnContext">The context object for the current turn of the conversation.</param>
        /// <param name="exception">The exception that was thrown during the turn.</param>
        /// <param name="logger">The logger used to log errors and trace information.</param>
        /// <param name="userState">The state object to retrieve user profile information.</param>
        /// <returns>A task that represents the asynchronous error handling operation.</returns>
        public async static Task OnTurnErrorFunction(ITurnContext turnContext, Exception exception, ILogger logger, UserState userState)
        {
            // Log the unhandled exception using the logger.
            logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

            // Retrieve the user profile to get language preferences for localized messages.
            IStatePropertyAccessor<UserProfile> userProfileAccessor = userState.CreateProperty<UserProfile>(nameof(UserProfile));
            UserProfile user = await UserProfile.GetUserProfile(userProfileAccessor, turnContext);
            string userLanguage = user?.Language ?? Lang.DEFAULT_LANGUAGE; // Fallback to default language if user language is not available.

            // Send an error message to the user based on their language preference.
            string message = user.IsDebugging
                ? $"{exception.Message}\n{exception.StackTrace}" // Provide detailed error in debug mode.
                : BotMessages.GetMessage("errorMessage", userLanguage); // Provide generic error message in production.
            await turnContext.SendActivityAsync(message, message, InputHints.AcceptingInput);

            // Send a trace activity for debugging purposes in the Bot Framework Emulator.
            await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError");

            // Attempt to write the error message and stack trace to a log file.
            try
            {
                using StreamWriter writer = new("error.log");
                writer.WriteLine($"[{DateTime.Now}] {exception.Message}\n{exception.StackTrace}");
            }
            catch (Exception ex)
            {
                // Log any exception that occurs while attempting to write to the log file.
                logger.LogError(ex, $"[OnTurnError] Error writing error log: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }
    }
}