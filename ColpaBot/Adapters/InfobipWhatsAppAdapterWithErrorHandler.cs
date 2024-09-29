using Bot.Builder.Community.Adapters.Infobip.WhatsApp;
using Microsoft.Bot.Builder;
using Microsoft.Extensions.Logging;
using static ColpaBot.Adapters.AdapterUtilities;

namespace ColpaBot.Adapters
{
    public class InfobipWhatsAppAdapterWithErrorHandler : InfobipWhatsAppAdapter
    {
        public InfobipWhatsAppAdapterWithErrorHandler(InfobipWhatsAppAdapterOptions infobipWhatsAppOptions, IInfobipWhatsAppClient infobipWhatsAppClient, ILogger<InfobipWhatsAppAdapterWithErrorHandler> logger, UserState userState)
            : base(infobipWhatsAppOptions, infobipWhatsAppClient, logger)
        {
            OnTurnError = async (turnContext, exception) => await OnTurnErrorFunction(turnContext, exception, logger, userState);
        }
    }
}