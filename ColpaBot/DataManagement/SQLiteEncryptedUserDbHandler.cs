// This constant and everything inside its condition should not be used in production.
#define PRESERVE_TESTING

using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColpaBot.DataManagement
{
    public class SQLiteEncryptedUserDbHandler
    {
        /// <summary>
        /// Any random string will do, but it should be the same for all instances of the database.
        /// </summary>
        /// <remarks>
        /// This one was generated in PowerShell with the following command:
        /// <code>
        /// # Generate key with 256 bits (32 bytes) with a random number generator
        /// $key = New-Object byte[] 32
        /// $randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        /// $randomNumberGenerator.GetBytes($key)
        /// # Convert to hex
        /// $keyHex = [BitConverter]::ToString($key).Replace("-", "")
        /// # Show the key
        /// $keyHex
        /// </code>
        /// </remarks>
        private const string ENCRYPTION_KEY = "3E506AC7E480F0931EE5B3A36D745614842E4041CCC6B9081C0D12D0A607323F"; // Store this in a secure place like Azure Key Vault

        private const string DB_PATH = "user_data.db"; // Path to the database file

        // Constant names for database table and columns
        private const string _USER_TABLE = "user",
            _USER_ID_COLUMN = "user_id",
            _USER_LANG_COLUMN = "user_lang",
            _USER_APPOINTMENT_COLUMN = "user_local_appointment",
            _USER_UTC_APPOINTMENT_COLUMN = "user_utc_appointment",
            _USER_IS_INITIALIZED_COLUMN = "user_is_initialized";

#if PRESERVE_TESTING
        // More attributes for the user. This table should be deleted in production.
        // Maintaining both tables separately for clarity on which one should be deleted.
        private const string _TEST_TABLE = "test",
            _TEST_USER_ID_COLUMN = "test_user_id",
            _TEST_ALG_COLUMN = "test_alg",
            _TEST_IS_DEBUGGING = "test_is_debugging";
#endif

        /// <summary>
        /// A row of the database representing the data of a UserProfile which should be stored in the database.
        /// </summary>
        [Table(_USER_TABLE)]
        private class UserProfileDb
        {
            /// <summary>
            /// Gets or sets the user ID.
            /// </summary>
            [PrimaryKey]
            [Column(_USER_ID_COLUMN)]
            public string User_id { get; set; }

            /// <summary>
            /// Gets or sets the user's language preference.
            /// </summary>
            [Column(_USER_LANG_COLUMN)]
            public string User_lang { get; set; }

            /// <summary>
            /// Gets or sets the local date and time of the user's appointment.
            /// </summary>
            [Column(_USER_APPOINTMENT_COLUMN)]
            public DateTime User_local_appointment { get; set; }

            /// <summary>
            /// Gets or sets the UTC date and time of the user's appointment.
            /// </summary>
            [Column(_USER_UTC_APPOINTMENT_COLUMN)]
            public DateTime User_utc_appointment { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the user's profile has been initialized.
            /// </summary>
            [Column(_USER_IS_INITIALIZED_COLUMN)]
            public bool User_is_initialized { get; set; }

            /// <summary>
            /// Converts the UserProfileDb instance to a UserProfile.
            /// </summary>
            /// <returns>A UserProfile instance.</returns>
            public UserProfile ToUserProfile()
            {
                return new UserProfile(User_id, User_lang, User_local_appointment, User_utc_appointment, User_is_initialized);
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="UserProfileDb"/> class.
            /// </summary>
            public UserProfileDb() { } // Needed to call Query<UserProfileDb>
        }

#if PRESERVE_TESTING
        /// <summary>
        /// A row of the database representing test attributes for a user.
        /// </summary>
        [Table(_TEST_TABLE)]
        private class TestUserAttributesDb
        {
            /// <summary>
            /// Gets or sets the test user ID.
            /// </summary>
            [PrimaryKey]
            [Column(_TEST_USER_ID_COLUMN)]
            public string User_id { get; set; }

            /// <summary>
            /// Gets or sets the algorithm used by the user.
            /// </summary>
            [Column(_TEST_ALG_COLUMN)]
            public int User_alg { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the user is debugging.
            /// </summary>
            [Column(_TEST_IS_DEBUGGING)]
            public bool User_is_debugging { get; set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="TestUserAttributesDb"/> class.
            /// </summary>
            public TestUserAttributesDb() { } // Needed to call Query<TestUserAttributes>
        }
#endif

        /// <summary>
        /// Initializes the database and creates the user table if it does not exist.
        /// </summary>
        static SQLiteEncryptedUserDbHandler()
        {
            using SQLiteConnection connection = CreateConnection(); // Create a connection to the database
            connection.CreateTable<UserProfileDb>(); // Create the user profile table
#if PRESERVE_TESTING
            connection.CreateTable<TestUserAttributesDb>(); // Create the test user attributes table
#endif
        }

        /// <summary>
        /// Opens a connection to the encrypted database.
        /// </summary>
        /// <returns>The SQLite connection.</returns>
        private static SQLiteConnection CreateConnection()
        {
            // Initialize the connection with SQLCipher encryption
            SQLiteConnectionString options = new(DB_PATH, true, ENCRYPTION_KEY);
            SQLiteConnection connection = new(options);
            return connection;
        }

        /// <summary>
        /// Saves a user into persistent memory, the database.
        /// </summary>
        /// <param name="user">Instance of the user's UserProfile.</param>
        /// <returns>True if there were no errors, false if there were.</returns>
        /// <exception cref="ArgumentNullException">The UserProfile cannot be null.</exception>
        public static bool SaveUserData(UserProfile user)
        {
            // Validate user input
            if (user == null) throw new ArgumentNullException(nameof(user), "User cannot be null.");
            if (user.Id == null) throw new ArgumentNullException(nameof(user.Id), "User ID cannot be null.");
            if (user.LocalDateAppointment == default) throw new ArgumentNullException(nameof(user.LocalDateAppointment), "User appointment date cannot be default.");
            if (user.UtcDateAppointment == default) throw new ArgumentNullException(nameof(user.UtcDateAppointment), "User UTC appointment date cannot be default.");
            if (user.Language == null) throw new ArgumentNullException(nameof(user.Language), "User language cannot be null.");

            try
            {
                using SQLiteConnection connection = CreateConnection(); // Create a connection to the database
                string query = $"INSERT OR REPLACE INTO {_USER_TABLE} " +
                    $"({_USER_ID_COLUMN}, {_USER_LANG_COLUMN}, {_USER_APPOINTMENT_COLUMN}, {_USER_UTC_APPOINTMENT_COLUMN}, {_USER_IS_INITIALIZED_COLUMN}) VALUES " +
                    "(?, ?, ?, ?, ?)";

                // Execute the insert or replace query for the user profile
                connection.Query<UserProfileDb>(query, user.Id, user.Language, user.LocalDateAppointment, user.UtcDateAppointment, user.UserProfileDialogComplete);
#if PRESERVE_TESTING
                // Execute the insert or replace query for test attributes if in testing mode
                connection.Query<TestUserAttributesDb>($"INSERT OR REPLACE INTO {_TEST_TABLE} " +
                    $"({_TEST_USER_ID_COLUMN}, {_TEST_ALG_COLUMN}, {_TEST_IS_DEBUGGING}) VALUES " +
                    "(?, ?, ?)", user.Id, user.CurrentAlgorithm, user.IsDebugging);
#endif
                return true; // Successful save
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error saving user data: " + e.Message + "\n" + e.StackTrace);
                return false; // Indicate failure
            }
        }

        /// <summary>
        /// Loads a user from the database with the specified user ID.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <returns>A UserProfile instance representing the user.</returns>
        /// <exception cref="ArgumentNullException">User ID cannot be null.</exception>
        public static UserProfile LoadUserData(string userId)
        {
            if (userId == null) throw new ArgumentNullException(nameof(userId), "User ID cannot be null.");

            try
            {
                using SQLiteConnection connection = CreateConnection(); // Create a connection to the database
                string query = $"SELECT * FROM {_USER_TABLE} WHERE {_USER_ID_COLUMN} = ?";

                // Retrieve the user profile based on user ID
                UserProfile result = connection.Query<UserProfileDb>(query, userId).FirstOrDefault()?.ToUserProfile();
#if PRESERVE_TESTING
                if (result != null) // Load test data if it exists
                {
                    TestUserAttributesDb testAttributes = connection.Query<TestUserAttributesDb>($"SELECT * FROM {_TEST_TABLE} WHERE {_TEST_USER_ID_COLUMN} = ?", userId).FirstOrDefault();
                    if (testAttributes != null)
                    {
                        result.CurrentAlgorithm = testAttributes.User_alg;
                        result.IsDebugging = testAttributes.User_is_debugging;
                    }
                }
#endif
                return result; // Return the loaded user profile or null if not found
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error loading user data: " + e.Message + "\n" + e.StackTrace);
                return null; // Indicate failure
            }
        }

        /// <summary>
        /// Loads the entire user table into a dictionary.
        /// </summary>
        /// <returns>A dictionary where the key is the user's ID and the value is the user's <see cref="UserProfile"/> instance.</returns>
        /// <exception cref="Exception">Thrown when there is an error loading user data.</exception>
        public static Dictionary<string, UserProfile> LoadAllUserData()
        {
            try
            {
                using SQLiteConnection connection = CreateConnection(); // Create a connection to the database
                string query = $"SELECT * FROM {_USER_TABLE}"; // Query to select all users

                // Load user profiles into a dictionary
                var users = connection
                    .Query<UserProfileDb>(query)
                    .Select(u => u.ToUserProfile())
                    .ToDictionary(u => u.Id);

#if PRESERVE_TESTING
                // Load test attributes and associate them with the user profiles
                var testAttributes = connection.Query<TestUserAttributesDb>($"SELECT * FROM {_TEST_TABLE}");
                foreach (var testAttribute in testAttributes)
                {
                    if (users.TryGetValue(testAttribute.User_id, out UserProfile user))
                    {
                        user.CurrentAlgorithm = testAttribute.User_alg; // Assign the user's algorithm
                        user.IsDebugging = testAttribute.User_is_debugging; // Assign the debugging flag
                    }
                }
#endif
                return users; // Return the dictionary of user profiles
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error loading all user data: " + e.Message + "\n" + e.StackTrace);
                return null; // Indicate failure
            }
        }

        /// <summary>
        /// Deletes the specified user from the database.
        /// </summary>
        /// <param name="userId">The user ID to be deleted.</param>
        /// <returns>True if deletion was successful; otherwise, false.</returns>
        public static bool DeleteUserData(string userId)
        {
            if (userId == null) throw new ArgumentNullException(nameof(userId), "User ID cannot be null.");

            try
            {
                using SQLiteConnection connection = CreateConnection();
                connection.Delete<UserProfileDb>(userId);
#if PRESERVE_TESTING
                connection.Delete<TestUserAttributesDb>(userId);
#endif
                return true; // Successful deletion
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error deleting user data: " + e.Message + "\n" + e.StackTrace);
                return false; // Indicate failure
            }
        }
    }
}