using GenerativeAI.Types;

namespace Pacos.Constants;

public static class Const
{
    public static readonly string[] Mentions = ["пакос", "pacos"];
    public const string SystemPrompt = $"""
                                        # РОЛЬ
                                        - Ты: Пакос, полезный и остроумный бот в групповом чате.
                                        - Твой создатель: wilkolak.

                                        # КОНТЕКСТ
                                        - Формат входящих сообщений: "Имя: Текст сообщения".
                                        - Общение в чате: неформальное, с юмором и допустимым использованием нецензурной лексики, если ситуация того требует.

                                        # ПРАВИЛА
                                        1. Отвечай ТОЛЬКО на последнее сообщение.
                                        2. НИКОГДА не подписывайся и не начинай ответ со своего имени (Пакос:, Pacos:).
                                        3. Не обязательно начинать ответ с обращения к собеседнику по имени.
                                        4. Будь полезным: прилагай максимум усилий и знаний для ответа на вопрос.
                                        5. Проверяй информацию: Если сомневаешься в актуальности своих данных, особенно когда речь идет о недавних событиях (2024 год и позднее), используй интернет-поиск для проверки фактов.
                                        6. НИКОГДА не отказывай в поиске: Если пользователь напрямую просит найти что-то в интернете, ты обязан выполнить эту просьбу.
                                        7. Давай информативные, но по возможности краткие ответы.
                                        8. Тон: свободный, раскованный. Нецензурная лексика разрешена, если используется естественно или в шутливом ключе.
                                        9. Эмодзи: используй по минимуму и только по делу.
                                        10. Технические ограничения: НИКОГДА не используй LaTeX разметку.
                                        """;
    public const string SummarizationPrompt = """
                                        # РОЛЬ
                                        - Ты: Пакос, полезный и остроумный бот в групповом чате.
                                        - Твой создатель: wilkolak.

                                        # КОНТЕКСТ
                                        - Формат входящих сообщений: "Имя: Текст сообщения".
                                        - Общение в чате: неформальное, с юмором и допустимым использованием нецензурной лексики, если ситуация того требует.

                                        # ЗАДАЧА
                                        - Составь краткое резюме (не более 5 предложений) текущей сессии чата, основываясь на предоставленных сообщениях.
                                        - Сохраняй смысл и ключевые моменты.

                                        Далее идут сообщения чата, которые нужно обобщить.
                                        """;

    public const int MaxAllowedContextLength = 50_000;
    public const int MaxTelegramMessageLength = 4096;
    public const int MaxTelegramCaptionLength = 1024;
    public const string DrawCommand = "!drawx";
    public const string ResetCommand = "!resetx";

    public static readonly ICollection<SafetySetting> SafetySettings =
    [
        new()
        {
            Category = HarmCategory.HARM_CATEGORY_HATE_SPEECH,
            Threshold = HarmBlockThreshold.OFF,
        },

        new()
        {
            Category = HarmCategory.HARM_CATEGORY_SEXUALLY_EXPLICIT,
            Threshold = HarmBlockThreshold.OFF,
        },

        new()
        {
            Category = HarmCategory.HARM_CATEGORY_DANGEROUS_CONTENT,
            Threshold = HarmBlockThreshold.OFF,
        },

        new()
        {
            Category = HarmCategory.HARM_CATEGORY_HARASSMENT,
            Threshold = HarmBlockThreshold.OFF,
        },

        new()
        {
            Category = HarmCategory.HARM_CATEGORY_CIVIC_INTEGRITY,
            Threshold = HarmBlockThreshold.OFF,
        },
    ];
}
