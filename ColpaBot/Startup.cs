using Bot.Builder.Community.Adapters.Infobip.WhatsApp;
using ColpaBot.Adapters;
using ColpaBot.Bots;
using ColpaBot.Dialogs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System;
using ColpaBot.DataManagement;

namespace ColpaBot
{
    public class Startup(IConfiguration configuration)
    {
        private readonly IConfiguration _configuration = configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient().AddControllers().AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.MaxDepth = HttpHelper.BotMessageSerializerSettings.MaxDepth;
            });

            // Create the Bot Framework Authentication to be used with the Bot Adapter.
            services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

            // Create the Bot Adapter with error handling enabled.
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            // Add Infobip Adapter with error handler and its dependencies
            services.AddSingleton<InfobipWhatsAppAdapterOptions>();
            services.AddSingleton<IInfobipWhatsAppClient, InfobipWhatsAppClient>();
            services.AddSingleton<InfobipWhatsAppAdapter, InfobipWhatsAppAdapterWithErrorHandler>();

            // Create a global hashset for our ConversationReferences
            services.AddSingleton<ConcurrentDictionary<string, ConversationReference>>();

            // Create the storage we'll be using for User and Conversation state. (Memory is great for testing purposes.)
            services.AddSingleton<IStorage, MemoryStorage>();

            // Create the User state. (Used in this bot's Dialog implementation.)
            services.AddSingleton<UserState>();

            // Create the Conversation state. (Used by the Dialog system itself.)
            services.AddSingleton<ConversationState>();

            // The Dialogs that will be run by the bot.
            services.AddSingleton<UserProfileDialog>();
            services.AddSingleton<ShowQuestionsDialog>();
            services.AddSingleton<ChangeAppointmentDialog>();

            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
            services.AddTransient<IBot, ColpaBot<UserProfileDialog, ShowQuestionsDialog, ChangeAppointmentDialog>>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles()
                .UseStaticFiles() // wwwroot is only used in the development version, maybe it would be good to delete this line
                .UseRouting()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });

            // Initialize the classes
            // I decided to initialize them with a static method instead of using the class constructor because
            // I wanted to have more control over the initialization order and since I need one for ReminderScheduler,
            // I made all of the initializations the same way to mantain consistency
            DataUtilites.Initialize();
            Lang.Initialize();
            BotMessages.Initialize();
            Qna.Initialize();
            SynonymManager.Initialize();
            ReminderScheduler.Initialize(_configuration);
            UserProfile.Initialize();
            ColpaBot<UserProfileDialog, ShowQuestionsDialog, ChangeAppointmentDialog>.Initialize();
            
            // app.UseHttpsRedirection();
        }
    }
}
