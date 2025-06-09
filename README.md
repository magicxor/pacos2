# Pacos Telegram Bot

Pacos is a .NET-based Telegram bot designed to interact in group chats. It leverages generative AI for chat responses and image generation.

*The alpaca was scientifically described by Carl Linnaeus in his System of Nature (1758) under the Latin name Camelus pacos.*

## Features

- **AI-Powered Chat**: Responds to mentions (e.g., "pacos", "пакос") or direct messages using Google's Gemini Pro model. It maintains a chat history for context-aware conversations.
- **Image Generation**:
    - **Text-to-Image**: Generate images from textual descriptions using the `!drawx <prompt>` command.
    - **Image-to-Image**: Modify existing images by replying to a message containing an image (or sending an image directly with the command) using `!drawx <prompt>`.
- **Chat Management**:
    - **Reset History**: Users can clear the bot's memory for a specific chat with the `!resetx` command.
- **Content Moderation**:
    - **Word Filter**: Filters messages against a list of banned words (defined in `banwords.txt`).
- **Language Identification**: Detects the language of incoming messages to potentially tailor responses (using `NTextCat` with `Core14.profile.xml`).
- **Asynchronous Processing**: Handles incoming Telegram updates and AI interactions asynchronously using a background task queue to ensure responsiveness.

## Core Technologies

- **Framework**: .NET (Worker Service)
- **Telegram API**: `Telegram.Bot` library
- **Generative AI (Chat)**: `Microsoft.Extensions.AI` with Google's Gemini Pro model (`gemini-2.5-pro-preview-06-05`)
- **Generative AI (Image)**: Direct integration with Google's Generative AI for image model (`gemini-2.0-flash-preview-image-generation`)
- **Logging**: NLog (configured via `nlog.config`)
- **Configuration**: Standard .NET configuration (e.g., `appsettings.json`, environment variables)
- **Language Detection**: `NTextCat`

## Configuration

The bot requires the following configuration settings, typically provided via environment variables or an `appsettings.json` file under the `Pacos` section:

- `TelegramBotApiKey`: Your Telegram Bot API token.
- `GoogleCloudApiKey`: Your Google Cloud API key for accessing generative AI services.
- `AllowedChatIds`: An array of Telegram chat IDs where the bot is permitted to operate.
- `AllowedLanguageCodes`: An array of ISO 639-3 language codes (e.g., "eng", "rus") that the bot primarily interacts with.

## Key Components

- **`Program.cs`**: Entry point, sets up dependency injection, configuration, and services.
- **`Worker.cs`**: Main background service that initializes and runs the `TelegramBotService`.
- **`TelegramBotService.cs`**: Handles all interactions with the Telegram API, including receiving updates, processing commands, and sending messages/photos.
- **`ChatService.cs`**: Manages chat history and interacts with the AI chat client to generate text responses.
- **`GenerativeModelService.cs`**: Interacts with the Google AI models for both text-to-image and image-to-image generation.
- **`PacosOptions.cs`**: Defines the structure for application configuration.
- **`Const.cs`**: Contains global constants, including AI system prompts, command names, and various limits.
- **Background Tasks (`BackgroundTaskQueue.cs`, `QueuedHostedService.cs`)**: Manages asynchronous execution of tasks.
- **`WordFilter.cs`**: Service for checking messages against a list of banned words.

## Setup and Running

1.  Ensure you have the .NET SDK installed.
2.  Configure the required API keys and settings (see **Configuration** section).
3.  Create `banwords.txt` (if needed for word filtering) and `Core14.profile.xml` (for NTextCat language identification) in the application's root directory.
4.  Run the application:
    ```bash
    dotnet run
    ```

## Bot Commands

- `pacos, <message>`: Engage in a conversation with the bot.
- `!drawx <prompt>`: Generate an image based on the provided text prompt.
- `!drawx <prompt>` (replying to an image or with an image): Modify the existing image based on the prompt.
- `!resetx`: Clear the bot's chat history for the current chat.
