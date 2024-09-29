using ColpaBot.DataManagement;
using Microsoft.Bot.Builder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static ColpaBot.DataManagement.BotMessages;
using static ColpaBot.DataManagement.ReminderScheduler;

namespace ColpaBot
{
    /// <summary>
    /// Represents the user state as a serializable .NET class.
    /// </summary>
    public class UserProfile
    {
        /// <summary>
        /// The format string for dates.
        /// </summary>
        public const string DATE_FORMAT = "dd/MM/yyyy";

        /// <summary>
        /// The format string for times, with and without leading zeros for hours.
        /// </summary>
        public const string SHORTENED_TIME_FORMAT = "H:mm", TIME_FORMAT = "HH:mm";

        /// <summary>
        /// The format string for date and time combined.
        /// </summary>
        public const string DATE_TIME_FORMAT = DATE_FORMAT + " " + TIME_FORMAT;

        private const int MONTHS_NECESSARY_FOR_CLEANUP = 12;

        /// <summary>
        /// The maximum number of consecutive misses before giving options to the user.
        /// </summary>
        public const int MAX_MISSES_BEFORE_GIVING_OPTIONS = 1;

        /// <summary>
        /// The time range for appointments (8:00 AM to 2:00 PM).
        /// </summary>
        public const int FROM_HOUR_FOR_APPOINTMENT = 8, TO_HOUR_FOR_APPOINTMENT = 14;

        /// <summary>
        /// Timer to clean up old users every 30 days
        /// </summary>
        private static readonly Timer _cleanupTimer = new(CleanupOldUsers, null, TimeSpan.Zero, TimeSpan.FromDays(30));

        /// <summary>
        /// Gets or sets the user's unique identifier.
        /// </summary>
        public string Id { get; set; } = null;

        /// <summary>
        /// Gets or sets the user's preferred language.
        /// </summary>
        public string Language { get; set; } = null;

        /// <summary>
        /// DateTime of the user's appointment in the user's time zone. Set to default if a new appointment needs to be scheduled or if there was none scheduled.
        /// </summary>
        public DateTime LocalDateAppointment { get; set; } = default;

        /// <summary>
        /// UTC DateTime of the user's appointment.
        /// </summary>
        public DateTime UtcDateAppointment { get; set; } = default;
        public bool UserProfileDialogComplete { get; set; } = false; // default is false because it is required from the beginning
        public bool QuestionsDialogComplete { get; set; } = true; // default is true because it is not required from the beginning
        public bool ChangeAppointmentDialogComplete { get; set; } = true; // default is true because the user does not start changing the appointment

        /// <summary>
        /// Gets a value indicating whether the user is currently in any dialog.
        /// </summary>
        public bool IsInAnyDialog => !UserProfileDialogComplete || !QuestionsDialogComplete || !ChangeAppointmentDialogComplete;

        /// <summary>
        /// Gets or sets a value indicating whether the user is in debugging mode.
        /// </summary>
        public bool IsDebugging { get; set; } = false;

        /// <summary>
        /// Gets or sets the number of consecutive misses.
        /// </summary>
        public int ConsecutiveMisses { get; set; } = 0;

        /// <summary>
        /// Gets or sets the current algorithm being used.
        /// </summary>
        public int CurrentAlgorithm { get; set; } = 0;

        // next commented line is used with the method GetAnswer to consider the previous messages in the chat as part of the context. The fetaure is not working as desired, therefore it is left commented.
        // public (string Text, bool wasSuccessfullyAnswered, string Answer) LastPrompt { get; set; } = (null, false, null);

        /// <summary>
        /// Parameterless constructor needed for JSON serialization
        /// </summary>
        private UserProfile() { } 

        /// <summary>
        /// Initializes a new instance of the <see cref="UserProfile"/> class.
        /// </summary>
        public UserProfile(string id, string language, DateTime localAppointment, DateTime utcAppointment, bool isInitialized)
        {
            Id = id;
            Language = language;
            LocalDateAppointment = localAppointment;
            UtcDateAppointment = utcAppointment;
            UserProfileDialogComplete = isInitialized;
        }

        /// <summary>
        /// Initializes the UserProfile class and sets reminders for all users.
        /// </summary>
        public static void Initialize()
        {
            if (!IsClassMsgsInitialized)
            {
                throw new Exception("Bot messages must be initialized before initializing the UserProfile");
            }
            else if (!IsReminderSchedulerClassInitialized)
            {
                throw new Exception("Reminder Scheduler must be initialized before initializing the UserProfile");
            }

            // Set reminders for all users when the bot starts
            foreach ((_, UserProfile user) in SQLiteEncryptedUserDbHandler.LoadAllUserData() ?? [])
            {
                if (user.UserProfileDialogComplete)
                {
                    CreateRemindersAsync(user).Wait();
                }
            }
        }

        /// <summary>
        /// Checks if a user exists in the state or database.
        /// </summary>
        public static async Task<bool> DoesUserExist(IStatePropertyAccessor<UserProfile> userProfileAccessor, ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            UserProfile user = await userProfileAccessor.GetAsync(turnContext, () => null, cancellationToken);
            user ??= SQLiteEncryptedUserDbHandler.LoadUserData(turnContext.Activity.From.Id);
            return user != null;
        }

        /// <summary>
        /// Retrieves the user profile from the state or database.
        /// </summary>
        public static async Task<UserProfile> GetUserProfile(IStatePropertyAccessor<UserProfile> userProfileAccessor, ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            UserProfile user = await userProfileAccessor.GetAsync(turnContext, () => null, cancellationToken);
            if (user == null) // If not cached
            {
                string id = turnContext.Activity.From.Id;
                user = SQLiteEncryptedUserDbHandler.LoadUserData(id);
                user ??= new UserProfile() { Id = id };
                await userProfileAccessor.SetAsync(turnContext, user, cancellationToken);
            }
            return user;
        }

        /// <summary>
        /// Sets the user profile in the state and optionally saves it to the database.
        /// </summary>
        public async Task<bool> SetUserProfile(IStatePropertyAccessor<UserProfile> userProfileAccessor, ITurnContext turnContext, CancellationToken cancellationToken = default, bool saveInDatabase = false)
        {
            await userProfileAccessor.SetAsync(turnContext, this, cancellationToken);
            if (saveInDatabase)
            {
                return SQLiteEncryptedUserDbHandler.SaveUserData(this);
            }
            return true;
        }

        /// <summary>
        /// Deletes the user profile from the cache and database.
        /// </summary>
        public async Task DeleteUserProfile(IStatePropertyAccessor<UserProfile> userProfileAccessor, ITurnContext turnContext, CancellationToken cancellationToken)
        {
            await userProfileAccessor.DeleteAsync(turnContext, cancellationToken);
            SQLiteEncryptedUserDbHandler.DeleteUserData(Id);
        }

        /// <summary>
        /// Cleans up old users from the database every 30 days.
        /// </summary>
        private static void CleanupOldUsers(object state)
        {
            foreach ((_, UserProfile user) in SQLiteEncryptedUserDbHandler.LoadAllUserData() ?? [])
            {
                if (user.UtcDateAppointment.AddMonths(MONTHS_NECESSARY_FOR_CLEANUP) < DateTime.UtcNow) // If MONTHS_NECESSARY_FOR_CLEANUP months have passed since the appointment
                {
                    SQLiteEncryptedUserDbHandler.DeleteUserData(user.Id);
                }
            }
        }

        /// <summary>
        /// Returns a string representation of the UserProfile.
        /// </summary>
        public override string ToString()
        {
            return $"User {Id} with UTC appointment {UtcDateAppointment.ToString(DATE_TIME_FORMAT)}, local appointment {LocalDateAppointment.ToString(DATE_TIME_FORMAT)} and language {Language}";
        }
    }
}