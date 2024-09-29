using System;
using Quartz;
using System.IO;
using System.Text;
using Quartz.Impl;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Quartz.Impl.Matchers;
using ColpaBot.Controllers;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using static ColpaBot.UserProfile;
using static ColpaBot.DataManagement.BotMessages;
using static ColpaBot.DataManagement.DataUtilites;

namespace ColpaBot.DataManagement
{
    /// <summary>
    /// This class is responsible for scheduling reminders and sending push notifications to users.
    /// </summary>
    public class ReminderScheduler
    {
        /// <summary>
        /// Indicates whether the ReminderScheduler class has been initialized.
        /// </summary>
        public static bool IsReminderSchedulerClassInitialized { get; private set; } = false;

        // File path for storing reminders
        private static readonly string _REMINDERS_FILE_PATH = GetFilePath("reminders", [SHEET_EXTENSION], DATA_DIRECTORY);

        // Holds the reminder configurations loaded from the file
        private static ReminderConfiguration[] _reminderConfigurations;

        // Scheduler instance
        private static IScheduler _scheduler;

        // Bot URL used for sending reminders
        private static string _botUrl;

        /// <summary>
        /// Structure representing a reminder configuration.
        /// </summary>
        private readonly struct ReminderConfiguration
        {
            /// <summary>
            /// Message key for the reminder.
            /// </summary>
            public string MessageKey { get; }

            /// <summary>
            /// Number of days before or after the event to send the reminder.
            /// </summary>
            public int DaysOffset { get; }

            /// <summary>
            /// Time mode ('A' for absolute, 'R' for relative).
            /// </summary>
            public char TimeMode { get; }

            /// <summary>
            /// Hours at which the reminder should be sent.
            /// </summary>
            public int Hours { get; }

            /// <summary>
            /// Minutes at which the reminder should be sent.
            /// </summary>
            public int Minutes { get; }

            /// <summary>
            /// Optional function to adjust the reminder time (e.g., Min, Max).
            /// </summary>
            public string Function { get; }

            public ReminderConfiguration(string messageKey, int daysOffset, char timeMode, int hours, int minutes, string function)
            {
                MessageKey = messageKey;
                DaysOffset = daysOffset;
                TimeMode = timeMode;
                Hours = hours;
                Minutes = minutes;
                Function = function;
            }
        }

        /// <summary>
        /// Structure representing a reminder.
        /// </summary>
        public readonly struct Reminder
        {
            /// <summary>
            /// Message to be sent in the reminder.
            /// </summary>
            public string Message { get; }

            /// <summary>
            /// User ID associated with the reminder.
            /// </summary>
            public string UserId { get; }

            public Reminder(string message, string userId)
            {
                Message = message;
                UserId = userId;
            }
        }

        /// <summary>
        /// Initializes the ReminderScheduler class.
        /// </summary>
        /// <param name="configuration">Application configuration containing necessary settings.</param>
        public static void Initialize(IConfiguration configuration)
        {
            // Ensure bot messages are initialized before starting the scheduler
            if (!IsClassMsgsInitialized)
            {
                throw new Exception("Bot messages must be initialized before initializing the Reminder Scheduler");
            }

            // Retrieve bot URL from configuration
            _botUrl = configuration["BotUrl"] ?? throw new Exception($"{nameof(ReminderScheduler)} could not retrieve the bot's URL needed to send the reminders");

            // Create and start the scheduler
            _scheduler = new StdSchedulerFactory().GetScheduler().Result;
            _scheduler.Start().Wait();

            // Load reminder configurations from the file
            using StreamReader reader = new(_REMINDERS_FILE_PATH);
            try
            {
                List<ReminderConfiguration> configurations = [];
                bool isFirst = true;
                string line;
                while ((line = SkipAndReadLine(reader)) != null)
                {
                    string[] parts = line.Split(Separator);
                    string messageKey = parts[0];
                    int days = int.Parse(parts[1]);
                    char timeMode = parts[2][0];
                    int hours = int.Parse(parts[3]);
                    int minutes = int.Parse(parts[4]);
                    string function = parts.Length == 6
                        ? (!string.IsNullOrWhiteSpace(parts[5])
                            ? parts[5]
                            : null)
                        : null;

                    // The first line should not contain a function
                    if (isFirst && !string.IsNullOrWhiteSpace(function))
                    {
                        throw new InvalidDataException("The first line must not have a function");
                    }

                    // Validate time mode
                    if (timeMode != 'A' && timeMode != 'R')
                    {
                        throw new InvalidDataException("Time mode must be 'A' or 'R'");
                    }

                    // Ensure message key or function is specified correctly
                    if ((string.IsNullOrWhiteSpace(messageKey) && string.IsNullOrWhiteSpace(function)) || (!string.IsNullOrWhiteSpace(messageKey) && !string.IsNullOrWhiteSpace(function)))
                    {
                        throw new InvalidDataException("Only message key or function must be in one line");
                    }

                    // Validate message key
                    if (!string.IsNullOrWhiteSpace(messageKey) && !HasMessage(messageKey))
                    {
                        throw new InvalidDataException("Message key must be present in the bot messages file");
                    }

                    isFirst = false;
                    configurations.Add(new ReminderConfiguration(messageKey, days, timeMode, hours, minutes, function));
                }

                _reminderConfigurations = configurations.ToArray();

                // Simulate reminder times to ensure no duplicates
                for (int minutes = FROM_HOUR_FOR_APPOINTMENT * 60; minutes < TO_HOUR_FOR_APPOINTMENT * 60; minutes++)
                {
                    DateTime simulatedDateTime = DateTime.UtcNow.Date.AddMinutes(minutes);
                    IEnumerable<DateTime> reminders = Reminders(simulatedDateTime).Select(reminder => reminder.Item1);

                    // Check for duplicate reminders
                    if (reminders.Count() != reminders.Distinct().Count())
                    {
                        string[] duplicates = reminders
                            .GroupBy(n => n)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key.ToString(DATE_TIME_FORMAT))
                            .ToArray();
                        throw new InvalidDataException($"The reminders chosen are ambiguous since there are 2 or more reminder calculations that can generate the same reminders, those reminders are {string.Join(", ", duplicates)}");
                    }
                }
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"The {_REMINDERS_FILE_PATH} file is not in a good format\n" + e.Message);
            }

            IsReminderSchedulerClassInitialized = true;
        }

        /// <summary>
        /// Generates reminder times for a given appointment date.
        /// </summary>
        /// <param name="appointmentLocalDate">The local date and time of the user's appointment.</param>
        /// <returns>An array of tuples containing reminder times and message keys.</returns>
        private static (DateTime, string)[] Reminders(DateTime appointmentLocalDate)
        {
            List<(DateTime, string)> reminders = [];
            (DateTime, string) lastDateWithMessageKey = default; // Holds the last reminder with a message key for Min/Max calculations

            foreach (ReminderConfiguration config in _reminderConfigurations)
            {
                DateTime remindDate = config.TimeMode == 'A' // TimeMode != 'R'
                    ? appointmentLocalDate.AddDays(config.DaysOffset).Date.AddHours(config.Hours).AddMinutes(config.Minutes) // Absolute time: calculate based on days offset and fixed time
                    : appointmentLocalDate.AddDays(config.DaysOffset).AddHours(config.Hours).AddMinutes(config.Minutes); // Relative time: calculate based on offset from the appointment time

                // Handle Min/Max function if specified
                if (config.Function != null)
                {
                    if (config.Function.Equals("Max", StringComparison.OrdinalIgnoreCase))
                    {
                        remindDate = new DateTime(Math.Max(remindDate.Ticks, lastDateWithMessageKey.Item1.Ticks));
                    }
                    else if (config.Function.Equals("Min", StringComparison.OrdinalIgnoreCase))
                    {
                        remindDate = new DateTime(Math.Min(remindDate.Ticks, lastDateWithMessageKey.Item1.Ticks));
                    }
                    lastDateWithMessageKey = (remindDate, lastDateWithMessageKey.Item2);
                    reminders[^1] = lastDateWithMessageKey; // Replace the last reminder with the adjusted one
                }
                else
                {
                    lastDateWithMessageKey = (remindDate, config.MessageKey);
                    reminders.Add(lastDateWithMessageKey);
                }
            }

            return reminders.ToArray();
        }

        /// <summary>
        /// Creates reminders for a given appointment and schedules them.
        /// </summary>
        /// <param name="user">The user to send the reminders to.</param>
        public static async Task CreateRemindersAsync(UserProfile user)
        {

            if (user == null || user.Language == null || user.Id == null || user.LocalDateAppointment == default || user.UtcDateAppointment == default)
            {
                throw new ArgumentNullException(nameof(user) + " cannot be null nor any of its attributes");
            }
            await DeleteExistingRemindersAsync(user.Id);
            TimeSpan utcOffset = user.LocalDateAppointment - user.UtcDateAppointment;

            foreach ((DateTime remindLocalDate, string messageKey) in Reminders(user.LocalDateAppointment))
            {
                // Local user time to UTC
                DateTime remindUtcDate = remindLocalDate - utcOffset;

                if (remindUtcDate < DateTime.UtcNow) // if the reminder is in the past, don't schedule it
                {
                    continue;
                }
                string message = GetMessage(messageKey, user.Language);
                await ScheduleReminder(message, remindUtcDate, user.Id);
            }
        }

        /// <summary>
        /// Schedules a reminder for the user at the specified time.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="remindUtcDate">The UTC date and time for the reminder.</param>
        /// <param name="userId">The user ID to send the reminder to.</param>
        private static async Task ScheduleReminder(string message, DateTime remindUtcDate, string userId)
        {
            // Define the job and tie it to our Job class
            IJobDetail job = JobBuilder.Create<ReminderJob>()
            .WithIdentity($"{remindUtcDate.ToString(DATE_TIME_FORMAT)}_{userId}", userId)
                .UsingJobData(ReminderJob.MESSAGE_JOB_KEY, message)
                .Build();

            // Trigger the job to run at the remindDate
            ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity($"{remindUtcDate.ToString(DATE_TIME_FORMAT)}_{userId}_trigger", userId)
                .StartAt(remindUtcDate.ToLocalTime())
                .Build();

            // this will cause an exception if there is already a job with the same key,
            // but it is not supposed to happen because the user cannot create reminders
            // and i make sure that there are not 2 reminders with the same date
            await _scheduler.ScheduleJob(job, trigger);
        }

        private static async Task DeleteExistingRemindersAsync(string userId)
        {
            var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(userId));

            foreach (var jobKey in jobKeys)
            {
                var triggers = await _scheduler.GetTriggersOfJob(jobKey);
                foreach (var trigger in triggers)
                {
                    await _scheduler.UnscheduleJob(trigger.Key);
                }
                await _scheduler.DeleteJob(jobKey);
            }
        }

        /// <summary>
        /// A job that sends the reminder notification to the user.
        /// </summary>
        private class ReminderJob : IJob
        {
            public const string MESSAGE_JOB_KEY = "message";
            /// <summary>
            /// Executes the job to send the reminder notification.
            /// </summary>
            /// <param name="context">Job execution context.</param>
            /// <returns>A task representing the asynchronous operation.</returns>
            Task IJob.Execute(IJobExecutionContext context)
            {
                var message = context.JobDetail.JobDataMap.GetString(MESSAGE_JOB_KEY);
                var userId = context.JobDetail.Key.Group;
                SendReminderAsync(message, userId).Wait();
                return Task.CompletedTask;
            }

            private static async Task SendReminderAsync(string message, string userId)
            {
                using var client = new HttpClient();
                client.BaseAddress = new Uri(_botUrl);
                string jsonSerializedMessage = JsonSerializer.Serialize(new Reminder(message, userId));
                HttpContent content = new StringContent(jsonSerializedMessage, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(NotifyController.ENDPOINT, content);

                if (!response.IsSuccessStatusCode)
                {   
                    Console.Error.WriteLine($"Reminder {message} failed to be sent to user {userId}\n{await response.Content.ReadAsStringAsync()}");
                }
            }
        }
    }
}