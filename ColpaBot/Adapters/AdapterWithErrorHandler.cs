using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Logging;
using static ColpaBot.Adapters.AdapterUtilities;

namespace ColpaBot.Adapters
{
    public class AdapterWithErrorHandler : CloudAdapter
    {
        public AdapterWithErrorHandler(BotFrameworkAuthentication auth, ILogger<IBotFrameworkHttpAdapter> logger, UserState userState)
            : base(auth, logger)
        {
            OnTurnError = async (turnContext, exception) => await OnTurnErrorFunction(turnContext, exception, logger, userState);
        }

    }
}
