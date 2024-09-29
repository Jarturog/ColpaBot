using Bot.Builder.Community.Adapters.Infobip.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using System.Threading.Tasks;

namespace ColpaBot.Controllers
{
    [Route("api/infobip/whatsapp")]
    [ApiController]
    public class InfobipWhatsappController(InfobipWhatsAppAdapter adapter, IBot bot) : ControllerBase
    {
        [HttpPost]
        public async Task PostAsync()
        {
            // Delegate the processing of the HTTP POST to the adapter. The adapter will invoke the bot.
            await adapter.ProcessAsync(Request, Response, bot);
        }
    }
}
