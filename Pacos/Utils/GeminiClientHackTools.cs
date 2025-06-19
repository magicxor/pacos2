using System.Reflection;
using GenerativeAI;
using GenerativeAI.Microsoft;

namespace Pacos.Utils;

public static class GeminiClientHackTools
{
    public static void ReplaceModel(
        this GenerativeAIChatClient chatClient,
        GenerativeModel model,
        ILogger logger)
    {
        try
        {
            const string modelFieldName = nameof(chatClient.model);

            var chatClientType = chatClient.GetType();

            var backingField = chatClientType.GetField(
                $"<{modelFieldName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (backingField != null)
            {
                backingField.SetValue(chatClient, model);
                logger.LogInformation("Successfully set the backing field '{FieldName}' to the provided model", modelFieldName);
            }
            else
            {
                logger.LogWarning("Backing field '{FieldName}' not found in type '{TypeName}'", modelFieldName, chatClientType.FullName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while trying to set the backing field for model in chat client");
        }
    }
}
