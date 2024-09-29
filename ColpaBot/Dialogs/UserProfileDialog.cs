using System;
using System.Threading;
using System.Threading.Tasks;
using ColpaBot.DataManagement;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using static ColpaBot.UserProfile;
using static ColpaBot.DataManagement.BotMessages;
using static ColpaBot.Dialogs.DialogUtilities;

namespace ColpaBot.Dialogs
{
    public class UserProfileDialog : ComponentDialog
    {
        private const string DEFAULT_TIME_ZONE = "Central European Standard Time";
        private int _chosen_month = -1;

        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserProfileDialog"/> class.
        /// </summary>
        /// <param name="userState">The user state to access and store user profile information.</param>
        public UserProfileDialog(UserState userState)
            : base(nameof(UserProfileDialog))
        {
            _userProfileAccessor = userState.CreateProperty<UserProfile>(nameof(UserProfile));

            // Define the steps for the Waterfall dialog
            var waterfallSteps = new WaterfallStep[]
            {
                LanguageStepAsync,
                MonthAppointmentStepAsync,
                DayAppointmentStepAsync,
                TimeAppointmentStepAsync,
                TimeZoneStepAsync,
                ConfirmationStepAsync,
                LastStepAsync
            };

            // Add named dialogs to the DialogSet
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new ChoicePrompt(nameof(ChoicePrompt), LanguagePromptValidator));
            AddDialog(new TextPrompt("MonthPrompt", MonthPromptValidator));
            AddDialog(new TextPrompt("DayPrompt", DayPromptValidator));
            AddDialog(new TextPrompt("TimePrompt", TimePromptValidator));
            AddDialog(new TextPrompt("TimeZonePrompt", TimeZonePromptValidator));
            // I would have used class ConfirmPrompt but it does not support all the languages and sadly after answering the confirmation prompt, the options keep being displayed forever and it is a feature: https://github.com/microsoft/botframework-sdk/issues/4033
            AddDialog(new ChoicePrompt(nameof(ConfirmPrompt), ConfirmPromptValidator)); 
            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        /// <summary>
        /// Prompts the user to select a language.
        /// </summary>
        private async Task<DialogTurnResult> LanguageStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string language = Lang.DEFAULT_LANGUAGE;
            stepContext.Values["isChangingAppointment"] = false;
            UserProfile user = await GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            if (user.Language != null) // skip the language selection if the user has already set it (it is changing appointment)
            {
                stepContext.Values["isChangingAppointment"] = true;
                return await stepContext.NextAsync(user.Language, cancellationToken);
            }
            else if (stepContext.Context.Activity.Locale != null && Lang.TransformFromBcp47(stepContext.Context.Activity.Locale) != null)
            {
                language = Lang.TransformFromBcp47(stepContext.Context.Activity.Locale);
            }

            string selectionMessage = GetMessage("langSelection", language);
            return await stepContext.PromptAsync(nameof(ChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(selectionMessage),
                    Choices = CreateChoicesWithNormalizedSynonyms(Lang.GetLanguageNameList(LanguageList, true))
                }, cancellationToken);
        }

        /// <summary>
        /// Prompts the user to select a month for the appointment.
        /// </summary>
        private static async Task<DialogTurnResult> MonthAppointmentStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is FoundChoice choice)
            {
                string languageName = GetNonNormalizedChoice(choice.Value, Lang.GetLanguageNameList(LanguageList, true));
                stepContext.Values["languageName"] = languageName;
                stepContext.Values["language"] = int.TryParse(languageName, out int index)
                    ? Lang.LanguageList[index - 1]
                    : (object)Lang.NameToCode(languageName, false);
            } else // if the previous step was skipped, the result is a string
            {
                int index = Array.FindIndex(Lang.LanguageList, x => string.Equals(x, (string)stepContext.Result, StringComparison.OrdinalIgnoreCase));
                stepContext.Values["languageName"] = Lang.RemoveDialect(Lang.GetLanguageNameList(LanguageList)[index], false);
                stepContext.Values["language"] = stepContext.Result;
            }

            string monthPromptText = GetMessage("monthSelection", (string)stepContext.Values["language"]);
            const int MONTHS_IN_A_YEAR_VALID_FOR_APPOINTMENT = 12;
            string[] months = new string[MONTHS_IN_A_YEAR_VALID_FOR_APPOINTMENT];
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(Lang.TransformToIso639((string)stepContext.Values["language"]));
            }
            catch (CultureNotFoundException)
            {
                culture = null;
            }

            for (int i = 0; i < MONTHS_IN_A_YEAR_VALID_FOR_APPOINTMENT; i++)
            {
                DateTime date = DateTime.Now.AddMonths(i);
                months[i] = culture != null ? date.ToString("MMMM", culture) : date.Month.ToString();
            }

            string retryMonthPromptText = GetMessage("retryMonthSelection", (string)stepContext.Values["language"], months);
            return await stepContext.PromptAsync("MonthPrompt", new PromptOptions
            {
                Prompt = MessageFactory.Text(monthPromptText),
                RetryPrompt = MessageFactory.Text(retryMonthPromptText),
            }, cancellationToken);
        }

        /// <summary>
        /// Prompts the user to select a day for the appointment.
        /// </summary>
        private async Task<DialogTurnResult> DayAppointmentStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values["month"] = (string)stepContext.Result;
            _chosen_month = int.Parse((string)stepContext.Values["month"]);
            int validLastDay = GetMaxDaysInMonth(_chosen_month, false);
            string dayPromptText = GetMessage("daySelection", (string)stepContext.Values["language"]);
            string retryDayPromptText = GetMessage("retryDaySelection", (string)stepContext.Values["language"], [$"{validLastDay}"]);
            return await stepContext.PromptAsync("DayPrompt", new PromptOptions
            {
                Prompt = MessageFactory.Text(dayPromptText),
                RetryPrompt = MessageFactory.Text(retryDayPromptText),
            }, cancellationToken);
        }

        /// <summary>
        /// Prompts the user to select a time for the appointment.
        /// </summary>
        private static async Task<DialogTurnResult> TimeAppointmentStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values["day"] = (string)stepContext.Result;
            string timePromptText = GetMessage("timeSelection", (string)stepContext.Values["language"]);
            string fromTime = new TimeOnly(FROM_HOUR_FOR_APPOINTMENT, 0).ToString(TIME_FORMAT);
            string toTime = new TimeOnly(TO_HOUR_FOR_APPOINTMENT, 0).ToString(TIME_FORMAT);
            string exampleTime = new TimeOnly(TO_HOUR_FOR_APPOINTMENT - 2, 30).ToString(TIME_FORMAT);
            string retryTimePromptText = GetMessage("retryTimeSelection", (string)stepContext.Values["language"], [TIME_FORMAT, fromTime, toTime, exampleTime]);
            return await stepContext.PromptAsync("TimePrompt", new PromptOptions
            {
                Prompt = MessageFactory.Text(timePromptText),
                RetryPrompt = MessageFactory.Text(retryTimePromptText),
            }, cancellationToken);
        }

        /// <summary>
        /// Determines the user's time zone and sets the appointment date and time.
        /// </summary>
        private async Task<DialogTurnResult> TimeZoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            TimeOnly time = TimeOnly.ParseExact((string)stepContext.Result, SHORTENED_TIME_FORMAT);
            DateOnly date = GetAppointmentDate(int.Parse((string)stepContext.Values["month"]), int.Parse((string)stepContext.Values["day"]));
            stepContext.Values["dateTime"] = date.ToDateTime(time);

            UserProfile user = await GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            if (user.UtcDateAppointment != default)
            {
                if (!(bool)stepContext.Values["isChangingAppointment"])
                {
                    throw new Exception("The user's appointment was already set when it should not have been");
                }
                // Get UTC offset from the user's appointment and use it to get the new UTC appointment
                DateTime oldDateTime = user.LocalDateAppointment;
                DateTime oldUtcDateTime = user.UtcDateAppointment;
                string timeZone = GetTimeZoneId(oldDateTime.Hour, oldDateTime.Minute, oldUtcDateTime);
                TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                DateTime newDateTime = (DateTime)stepContext.Values["dateTime"];
                DateTime newUtcDateTime = TimeZoneInfo.ConvertTimeToUtc(newDateTime, timeZoneInfo);
                return await stepContext.NextAsync(newUtcDateTime, cancellationToken);
            }
            else if (stepContext.Context.Activity.LocalTimezone != null)
            {
                return await stepContext.NextAsync(stepContext.Context.Activity.LocalTimezone, cancellationToken);
            }

            string timeZonePromptText = GetMessage("requestHour", (string)stepContext.Values["language"]);
            string exampleTime = new TimeOnly(TO_HOUR_FOR_APPOINTMENT - 2, 30).ToString(TIME_FORMAT);
            string retryTimeZonePromptText = GetMessage("retryRequestHour", (string)stepContext.Values["language"], [TIME_FORMAT, exampleTime]);
            return await stepContext.PromptAsync("TimeZonePrompt", new PromptOptions
            {
                Prompt = MessageFactory.Text(timeZonePromptText),
                RetryPrompt = MessageFactory.Text(retryTimeZonePromptText),
            }, cancellationToken);
        }

        /// <summary>
        /// Confirms the user's appointment details.
        /// </summary>
        private async Task<DialogTurnResult> ConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            DateTime appointment = (DateTime)stepContext.Values["dateTime"];
            TimeZoneInfo timeZone;
            if (stepContext.Result is DateTime utcAppointment)
            {
                string timeZoneId = GetTimeZoneId(appointment.Hour, appointment.Minute, utcAppointment);
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                stepContext.Values["utcDateTime"] = utcAppointment;
            }
            else
            {
                string timeZoneId = stepContext.Result != null
                    ? (string)stepContext.Result
                    : DEFAULT_TIME_ZONE;
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                stepContext.Values["utcDateTime"] = TimeZoneInfo.ConvertTimeToUtc(appointment, timeZone);
            }

            if (stepContext.Result == null)
            {
                string message = GetMessage("timezoneNotFound", (string)stepContext.Values["language"]);
                await stepContext.Context.SendActivityAsync(message, message, InputHints.IgnoringInput, cancellationToken);
            }

            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            string summary = (bool)stepContext.Values["isChangingAppointment"]
                ? GetMessage("summaryUserDate", (string)stepContext.Values["language"], [appointment.ToString(DATE_TIME_FORMAT)])
                : GetMessage("summaryUserData", (string)stepContext.Values["language"], [(string)stepContext.Values["languageName"], appointment.ToString(DATE_TIME_FORMAT), localNow.ToString(DATE_TIME_FORMAT)]);
            await stepContext.Context.SendActivityAsync(summary, summary, InputHints.IgnoringInput, cancellationToken);

            string confirmationQuestion = GetMessage("userDataConfirmationQuestion", (string)stepContext.Values["language"]);
            string yesMessage = GetMessage("yes", (string)stepContext.Values["language"]);
            string noMessage = GetMessage("no", (string)stepContext.Values["language"]);
            return await stepContext.PromptAsync(nameof(ConfirmPrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(confirmationQuestion),
                    // if this was a ConfirmPrompt instead of a ChoicePrompt I would set the locale so the confirmation answers are in the user's language:
                    // stepContext.Context.Activity.Locale = Lang.TransformToIso639((string)stepContext.Values["language"]);
                    // and I would have passed this as a prompt option:
                    // RecognizeLanguage = stepContext.Context.Activity.Locale
                    Choices = CreateChoicesWithNormalizedSynonyms([yesMessage, noMessage])
                }, cancellationToken);
        }
        /// <summary>
        /// Processes the last step of the waterfall dialog.
        /// </summary>
        /// <param name="stepContext">The current step context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task<DialogTurnResult> LastStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string foundChoiceValue = ((FoundChoice)stepContext.Result).Value;
            string yesMessage = GetMessage("yes", (string)stepContext.Values["language"]);
            string noMessage = GetMessage("no", (string)stepContext.Values["language"]);
            bool confirmationSucceded;
            if (GetNonNormalizedChoice(foundChoiceValue, [yesMessage]) != null)
                confirmationSucceded = true;
            else if (GetNonNormalizedChoice(foundChoiceValue, [noMessage]) != null)
                confirmationSucceded = false;
            else
                throw new Exception("The confirmation step for the UserProfile dialog failed");

            if (!confirmationSucceded)
            {
                // End the current dialog and start a new instance of UserProfileDialog
                string retryMsg = GetMessage("retryDataCollection", (string)stepContext.Values["language"]);
                await stepContext.Context.SendActivityAsync(retryMsg, retryMsg, InputHints.IgnoringInput, cancellationToken);
                await stepContext.EndDialogAsync(null, cancellationToken);
                stepContext.Context.Activity.Locale = Lang.TransformToIso639((string)stepContext.Values["language"]);
                return await stepContext.BeginDialogAsync(nameof(UserProfileDialog), null, cancellationToken);
            }
            UserProfile user = await GetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken);
            user.LocalDateAppointment = (DateTime)stepContext.Values["dateTime"];
            user.UtcDateAppointment = (DateTime)stepContext.Values["utcDateTime"];
            user.UserProfileDialogComplete = true;
            user.Language = (string)stepContext.Values["language"];
            user.Id = stepContext.Context.Activity.From.Id;
            bool userWasSavedInDb = await user.SetUserProfile(_userProfileAccessor, stepContext.Context, cancellationToken, true);

            // Creating reminders for the user
            await ReminderScheduler.CreateRemindersAsync(user);

            List<Activity> messages = [];
            if ((bool)stepContext.Values["isChangingAppointment"])
            {
                string stringMessage = GetMessage(userWasSavedInDb ? "appointmentChanged" : "settingsNotSaved", user.Language);
                messages.Add(MessageFactory.Text(stringMessage, stringMessage, InputHints.AcceptingInput));
            }
            else
            {
                if (!userWasSavedInDb) // Tell the user that the settings were not saved because of a problem
                {
                    string stringMessage = GetMessage("settingsNotSaved", user.Language);
                    messages.Add(MessageFactory.Text(stringMessage, stringMessage, InputHints.IgnoringInput));
                }
                string[] stringMessages = [GetMessage("introduceSituation", user.Language), GetMessage("waitForMessage", user.Language)];
                messages.Add(MessageFactory.Text(stringMessages[0], stringMessages[0], InputHints.IgnoringInput));
                messages.Add(MessageFactory.Text(stringMessages[1], stringMessages[1], InputHints.ExpectingInput));
            }
            await stepContext.Context.SendActivitiesAsync(messages.ToArray(), cancellationToken);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        /// <summary>
        /// Validates the language prompt input.
        /// </summary>
        /// <param name="promptContext">The prompt validation context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation with a boolean result.</returns>
        private static Task<bool> LanguagePromptValidator(PromptValidatorContext<FoundChoice> promptContext, CancellationToken cancellationToken)
        {
            return Task.FromResult(promptContext.Recognized.Succeeded);
        }

        /// <summary>
        /// Validates the month prompt input.
        /// </summary>
        /// <param name="promptContext">The prompt validation context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation with a boolean result.</returns>
        private static Task<bool> MonthPromptValidator(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            if (!promptContext.Recognized.Succeeded || !int.TryParse(promptContext.Recognized.Value, out int month)) // Recognition attempt failed or invalid date format
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(month > 0 && month < 13); // Month out of range
        }

        /// <summary>
        /// Validates the day prompt input.
        /// </summary>
        /// <param name="promptContext">The prompt validation context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation with a boolean result.</returns>
        private Task<bool> DayPromptValidator(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            if (!promptContext.Recognized.Succeeded || !int.TryParse(promptContext.Recognized.Value, out int day))
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(day >= 1 && day <= GetMaxDaysInMonth(_chosen_month, false));
        }

        /// <summary>
        /// Validates the time prompt input.
        /// </summary>
        /// <param name="promptContext">The prompt validation context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation with a boolean result.</returns>
        private static Task<bool> TimePromptValidator(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            if (!promptContext.Recognized.Succeeded) // Recognition attempt failed 
            {
                return Task.FromResult(false);
            }
            if (int.TryParse(promptContext.Recognized.Value, out int hour) && hour >= 0 && hour < 24)
            {
                promptContext.Recognized.Value = new TimeOnly(hour, 0).ToString(SHORTENED_TIME_FORMAT);
            }
            if (!TimeOnly.TryParseExact(promptContext.Recognized.Value, SHORTENED_TIME_FORMAT, out TimeOnly time)) // Invalid date format
            {
                return Task.FromResult(false);
            }
            bool validDateRange = time >= new TimeOnly(FROM_HOUR_FOR_APPOINTMENT, 0) && time <= new TimeOnly(TO_HOUR_FOR_APPOINTMENT, 0);
            return Task.FromResult(validDateRange);
        }

        /// <summary>
        /// Validates the time zone prompt input.
        /// </summary>
        /// <param name="promptContext">The prompt validation context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation with a boolean result.</returns>
        private static Task<bool> TimeZonePromptValidator(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            if (!promptContext.Recognized.Succeeded) // Recognition attempt failed 
            {
                return Task.FromResult(false);
            }
            if (int.TryParse(promptContext.Recognized.Value, out int hour) && hour >= 0 && hour < 24)
            {
                promptContext.Recognized.Value = new TimeOnly(hour, 0).ToString(SHORTENED_TIME_FORMAT);
            }
            if (!TimeOnly.TryParseExact(promptContext.Recognized.Value, SHORTENED_TIME_FORMAT, out TimeOnly userNowTime)) // Cannot be parsed into a TimeOnly
            {
                return Task.FromResult(false);
            }
            promptContext.Recognized.Value = GetTimeZoneId(userNowTime.Hour, userNowTime.Minute, DateTime.UtcNow);
            return Task.FromResult(true);
        }

        /// <summary>
        /// ChoicePrompt validator for the confirmation step that acts like a ConfirmPrompt.
        /// </summary>
        /// <param name="promptContext">The prompt validation context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation with a boolean result.</returns>
        private Task<bool> ConfirmPromptValidator(PromptValidatorContext<FoundChoice> promptContext, CancellationToken cancellationToken)
        {
            return Task.FromResult(promptContext.Recognized.Succeeded);
        }

        /// <summary>
        /// Compares the local time of the user with the UTC time to get the time zone taking into account localMinute may be off by 1 minute.
        /// </summary>
        /// <param name="localHour">The local hour.</param>
        /// <param name="localMinute">The local minute.</param>
        /// <param name="utcTime">The UTC time.</param>
        /// <returns>An id of a time zone compatible with the times given.</returns>
        public static string GetTimeZoneId(int localHour, int localMinute, DateTime utcTime)
        {
            // This works based on the fact that there are only time zones with X, X.25, X.5 and X.75 hours away from UTC
            static DateTime GetRoundedDateTime(DateTime time)
            {
                static int RoundMinutes(int number) => number switch
                {
                    <= 7 => 0,
                    <= 22 => 15,
                    <= 37 => 30,
                    <= 52 => 45,
                    _ => 60,
                };
                int minutes = RoundMinutes(time.Minute);
                if (minutes == 60)
                {
                    time.AddHours(1);
                    minutes = 0;
                }
                return new DateTime(time.Year, time.Month, time.Day, time.Hour, minutes, 0);
            }

            DateTime utcNowDateTime = GetRoundedDateTime(utcTime);
            TimeOnly utcNowTime = new(utcNowDateTime.Hour, utcNowDateTime.Minute, 0);
            LinkedList<string> matchingTimeZones = [];
            foreach (int addedMinutes in new int[] { 0, 1 })
            {
                IEnumerable<DateTime> possibleLocalUserNowDateTimes = new DateTime[] {
                    GetRoundedDateTime(utcNowDateTime.AddDays(-1).Date.AddHours(localHour).AddMinutes(localMinute + addedMinutes)),
                    GetRoundedDateTime(utcNowDateTime.AddDays(0).Date.AddHours(localHour).AddMinutes(localMinute + addedMinutes)),
                    GetRoundedDateTime(utcNowDateTime.AddDays(1).Date.AddHours(localHour).AddMinutes(localMinute + addedMinutes))
                }.Where(d =>
                {
                    TimeSpan sub = d - utcNowDateTime;
                    return sub.Days == 0 || (sub.Hours == 0 && sub.Days == 1); // Do not allow more than 24 hours of offset because that does not exist on Earth
                });

                IEnumerable<TimeSpan> possibleUtcOffsets = possibleLocalUserNowDateTimes
                    .Select(d => d - utcNowDateTime);

                // Iterate through all the time zones
                foreach (var timeZone in TimeZoneInfo.GetSystemTimeZones())
                {
                    DateTime localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcNowDateTime, timeZone);
                    TimeSpan currentTimeZoneOffset = timeZone.GetUtcOffset(localDateTime);
                    // Adjust the comparison logic based on whether DST is active
                    if (possibleUtcOffsets.Contains(currentTimeZoneOffset))
                    {
                        // Prioritize time zones with DST, which is almost all of the western world
                        // and since the bot is made for Germany it is more likely that the user is in one of those time zones.
                        // This can cause a problem if the user is in a time zone without DST and it is currently active,
                        // but the solution would be to ask for the time zone or the city that the user lives in and that is too invasive
                        if (timeZone.IsDaylightSavingTime(localDateTime))
                        {
                            matchingTimeZones.AddFirst(timeZone.Id);
                        }
                        else
                        {
                            matchingTimeZones.AddLast(timeZone.Id);
                        }
                    }
                }
                if (matchingTimeZones.Count > 0) // If there are matches, break the loop
                {
                    break;
                }
                // If there were no matches assume that the user was some seconds late to write the current time
                // meaning that one minute less of the actual time slipped into the received input
            }

            // Set the time zone to retrieve it in the next method
            // I could show the possibilities to the user and ask him but I think it is better to warn him
            // assume 1 and warn him about the change of hour by Daylight saving time (DST) or other influence
            return matchingTimeZones.Count > 0
                ? matchingTimeZones.First()
                : null;
        }

        /// <summary>
        /// Calculates the number of days in the month in this or next year.
        /// </summary>
        /// <param name="month">The month to calculate days for.</param>
        /// <param name="considerOnlyCurrentYear">Whether to consider only the current year.</param>
        /// <returns>The maximum number of days in the given month.</returns>
        private static int GetMaxDaysInMonth(int month, bool considerOnlyCurrentYear)
        {
            DateTime now = DateTime.Now;
            // Give the days in month of both the month in this year and the next year
            // in case it is a month which changes days between years
            int daysInMonth = DateTime.DaysInMonth(now.Year, month);
            if (!considerOnlyCurrentYear)
            {
                daysInMonth = Math.Max(DateTime.DaysInMonth(now.Year + 1, month), daysInMonth);
            }
            return daysInMonth;
        }

        /// <summary>
        /// Gets the appointment date based on the given month and day.
        /// </summary>
        /// <param name="month">The month of the appointment.</param>
        /// <param name="day">The day of the appointment.</param>
        /// <returns>The appointment date.</returns>
        private static DateOnly GetAppointmentDate(int month, int day)
        {
            DateOnly now = new(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
            int year = now.Year;
            if (day > GetMaxDaysInMonth(month, true)) // If day is 29 and the month February is not in a leap year
            {
                return new DateOnly(year + 1, month, day); // This code works because a leap year does not occur twice in a row
            }
            DateOnly appointment = new(year, month, day);
            if (appointment < now)
            {
                appointment = appointment.AddYears(1);
            }
            return appointment;
        }
    }
}
