# Colpabot

Colpabot is a chatbot developed as part of a bachelor thesis, built using the [Microsoft Bot Framework](https://dev.botframework.com). It is designed to assist patients undergoing colonoscopy preparation.

## Features

- **User Persistent Data**: Initiates conversation by asking for language preference and colonoscopy date, storing this information for personalized interactions.
- **Natural Language Processing**: Matches user prompts to a predefined Q&A database, applying various operations for accurate responses.
- **Image Support**: Sends relevant images alongside certain answers.
- **Reminders**: Provides timely reminders about important aspects, like diet before the colonoscopy.
- **Command System**: Offers various commands to enhance user interaction. Type `/help` for more information.
- **Multi-platform**: 
  - Local testing with Bot Framework Emulator
  - Scalable deployment on Azure
  - Integration with Telegram (chat [here](t.me/Colprepbot) - Telegram account required)

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) version 6.0

  ```bash
  # Check your dotnet version
  dotnet --version
  ```
- [Infobip's WhatsApp adapter](https://github.com/BotBuilderCommunity/botbuilder-community-dotnet/tree/develop/libraries/Bot.Builder.Community.Adapters.Infobip.WhatsApp) version 4.13.5 for the integration with WhatsApp
- [Fastenshtein](https://github.com/DanHarltey/Fastenshtein) version 1.0.10 for Levenshtein distance calculation
- [Quartz](https://www.nuget.org/packages/Quartz) version 3.13.0 for scheduling reminders
- [sqlite-net-sqlcipher](https://www.nuget.org/packages/sqlite-net-sqlcipher) version 1.9.172 for encrypting the database
- [SixLabors's ImageSharp](https://sixlabors.com/products/imagesharp/) version 3.1.5 for detecting image sizes
- Optional prerequisites:
  - [DeepL's free API](https://www.deepl.com/en/pro#developer) key
    
    After obtaining the key, create a `deepl_key.txt` file in the same directory as the README and paste the key there.

  - To deploy to Azure, you will need:
    - `MicrosoftAppId`
    - `MicrosoftAppPassword` 
    
    These values should be added to `appsettings.json`. You will also need to modify the `BotUrl` in this file.
  
  - For WhatsApp integration via InfoBip, create an InfoBip account and retrieve the following:
    - `InfobipApiBaseUrl`
    - `InfobipApiKey`
    - `InfobipAppSecret`
    - `InfobipWhatsAppNumber`
    - `InfobipWhatsAppScenarioKey`

    For more details, refer to [this GitHub repository](https://github.com/BotBuilderCommunity/botbuilder-community-dotnet/tree/develop/libraries/Bot.Builder.Community.Adapters.Infobip.WhatsApp) and [this documentation](https://www.infobip.com/docs/integrations/microsoft-bot-framework).

## Running the Bot

- Choose one of the following methods:

  **Option A: Command Line**

  ```bash
  # Navigate to project folder (ColpaBot)
  cd ColpaBot

  # Run the bot
  dotnet run
  ```

  **Option B: Visual Studio**

  - Launch Visual Studio
  - File -> Open -> Project/Solution
  - Navigate to `ColpaBot` folder
  - Select `ColpaBot.csproj` file
  - Press `F5` to run the project

## Testing

### Bot Framework Emulator

1. Download and install the [Bot Framework Emulator](https://github.com/microsoft/botframework-emulator/releases)
2. Launch the emulator
3. Go to File -> Open Bot
4. Enter the Bot URL:
    - Usually `http://localhost:3978/api/messages` when debugging
    - May be `http://localhost:5000/api/messages` after building and executing
5. If `MicrosoftAppId` and `MicrosoftAppPassword` are set in `appsettings.json`, include them with the Bot URL. These are values obtained from the Azure portal.

## Customization

### Adding Questions and Answers

1. Navigate to `Resources/QuestionsAndAnswers/`
2. Open the appropriate language file (e.g., `questions_and_answers_EN-GB.tsv` for English)
3. Add new rows with questions and answers
    - You can use `.` to copy the previous answer
    - To attach an image to the message, add `ImageName` in the actions column
      - This image should be in the `Images` folder with `Name` as its file name.
      - To show one of many images with a message, create a folder and with the images you want to show. The names of these images do not matter, but the folder's name should be the `Name` in the `ImageName`
    - When using actions, you should take into account the specific rules that the order of actions must follow.
      - Image[...] must be the first one in the general section.
      - CopyQuestion[...] must be the first one in the specific sections. Something to note is that this action copies both the question AND the non-image actions of that entry.
      - DontAnswer must be the last one in all the sections.
4. Save the file
5. Optional: Translate to other languages:
    - Ensure Python and the `deepl` library are installed
    - Run `python translate.py` in the script's directory

### Modifying Reminders

1. Open `Resources/reminders.tsv`
2. Follow the file's structure to add or modify reminders
3. For new messages, add them to `Resources/bot_messages.tsv`
4. Run the translation script

## Deployment

The bot was deployed using a GitHub workflow with Visual Studio.

### Deploying to Azure

1. Login to Visual Studio with your Microsoft Azure account
2. Go to the project's GitHub repository
3. Click on Actions
4. Select the workflow
5. Click on "Run workflow"

For more details, see [Deploy your bot to Azure](https://aka.ms/azuredeployment).

## Additional Resources

- [Bot Framework Documentation](https://docs.botframework.com)
- [Bot Basics](https://docs.microsoft.com/azure/bot-service/bot-builder-basics?view=azure-bot-service-4.0)
- [Azure Bot Service Documentation](https://docs.microsoft.com/azure/bot-service/?view=azure-bot-service-4.0)
- [Azure Portal](https://portal.azure.com)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contact

[Email](mailto:juanarturoabaurreacalafell@gmail.com)