using System.Collections.Concurrent;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using static ColpaBot.DataManagement.ReminderScheduler;

namespace ColpaBot.Controllers
{
    /// <summary>
    /// API Controller to handle sending reminder notifications to users.
    /// </summary>
    [Route(ENDPOINT)]
    [ApiController]
    public class NotifyController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, ConcurrentDictionary<string, ConversationReference> conversationReferences, CancellationToken cancellationToken = default) : ControllerBase
    {
        /// <summary>
        /// Endpoint for the notify controller.
        /// </summary>
        public const string ENDPOINT = "api/notify";

        /// <summary>
        /// The app ID used to identify the bot when continuing a conversation.
        /// </summary>
        private readonly string _appId = configuration["MicrosoftAppId"] ?? string.Empty;

        /// <summary>
        /// Sends a reminder notification to a specific user. It first needs to receive an HTTP POST request from the reminder scheduler.
        /// </summary>
        /// <param name="reminder">The reminder object containing the user ID and message content.</param>
        /// <returns>An IActionResult indicating the success or failure of the reminder operation.</returns>
        [HttpPost]
        public IActionResult SendReminder([FromBody] Reminder reminder)
        {
            // Check if the reminder message or user ID is empty and return a bad request if so.
            if (string.IsNullOrEmpty(reminder.Message) || string.IsNullOrEmpty(reminder.UserId))
            {
                return BadRequest("No reminder sent, "
                    + (string.IsNullOrEmpty(reminder.Message) ? "content" : "user id")
                    + " is empty"); // Specify whether content or user ID is missing.
            }

            // Iterate over all conversation references and find the one corresponding to the reminder's user ID.
            foreach (ConversationReference conversationReference in conversationReferences.Values)
            {
                if (conversationReference.User.Id == reminder.UserId)
                {
                    // Continue the conversation with the user and send the reminder message.
                    ((BotAdapter)adapter).ContinueConversationAsync(
                        _appId,
                        conversationReference,
                        async (ITurnContext turnContext, CancellationToken cancellationToken) =>
                            await turnContext.SendActivityAsync(reminder.Message, reminder.Message, InputHints.AcceptingInput, cancellationToken),
                        cancellationToken
                    );
                }
            }

            // Return success response after sending the reminder.
            return Ok("Notification received successfully");
        }
    }
}