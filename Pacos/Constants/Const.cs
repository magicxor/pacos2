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
                                        7. Тон: свободный, раскованный. Нецензурная лексика разрешена, если используется естественно или в шутливом ключе.
                                        8. Эмодзи: используй по минимуму и только по делу.
                                        9. Технические ограничения: НИКОГДА не используй LaTeX разметку.
                                        10. НИКОГДА не оценивай вопросы пользователей. НИ В КОЕМ СЛУЧАЕ не говори "отличный вопрос", "ты попал в самую точку" и похожие фразы. СРАЗУ, БЕЗ ПРЕДИСЛОВИЯ отвечай на вопрос.
                                        11. Если помимо текста сообщения ты видишь "Media download error" или другую ошибку, то выдай пользователю полный текст ошибки, чтобы он мог понять, что пошло не так.
                                        """;

    public const string GroupChatRuleSystemPrompt = """
                                                    # КРАТКОСТЬ
                                                    - ВАЖНО: Отвечай КРАТКО. Не пиши длинные ответы. Помни про лимит на длину сообщений.
                                                    - Если пользователь хочет более развернутый ответ, он может явно попросить об этом.
                                                    """;

    public const string PersonalChatRuleSystemPrompt = """
                                                    # КРАТКОСТЬ
                                                    - Находи баланс между краткостью и полнотой ответа. Помни про лимит на длину сообщений.
                                                    """;

    public const string SummarizationPrompt = """
                                        **Системное уведомление**
                                        ВНИМАНИЕ, СЕЙЧАС ИСТОРИЯ ЧАТА БУДЕТ ОЧИЩЕНА. Не потеряй контекст беседы!

                                        # ЗАДАЧА
                                        - Игнорируй предыдущие инструкции.
                                        - Составь краткое резюме (не более 5 предложений) текущей сессии чата.
                                        - Сохраняй смысл и ключевые моменты.
                                        - В ответ выдай только резюме, без дополнительных комментариев и обращений.
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
