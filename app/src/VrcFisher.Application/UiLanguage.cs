namespace VrcFisher.Application;

public static class UiLanguage
{
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";
    public const string TraditionalChinese = "zh-TW";
    public const string Japanese = "ja-JP";
    public const string Korean = "ko-KR";
    public const string Spanish = "es-ES";
    public const string French = "fr-FR";
    public const string German = "de-DE";
    public const string BrazilianPortuguese = "pt-BR";
    public const string Russian = "ru-RU";
    public const string Italian = "it-IT";
    public const string Polish = "pl-PL";
    public const string Turkish = "tr-TR";
    public const string Dutch = "nl-NL";
    public const string Czech = "cs-CZ";
    public const string Hungarian = "hu-HU";
    public const string Ukrainian = "uk-UA";
    public const string Thai = "th-TH";
    public const string Swedish = "sv-SE";
    public const string Finnish = "fi-FI";

    public static readonly IReadOnlyList<UiLanguageDefinition> Languages =
    [
        new(English, "English"),
        new(SimplifiedChinese, "简体中文"),
        new(TraditionalChinese, "繁體中文"),
        new(Japanese, "日本語"),
        new(Korean, "한국어"),
        new(Spanish, "Español"),
        new(French, "Français"),
        new(German, "Deutsch"),
        new(BrazilianPortuguese, "Português (Brasil)"),
        new(Russian, "Русский"),
        new(Italian, "Italiano"),
        new(Polish, "Polski"),
        new(Turkish, "Türkçe"),
        new(Dutch, "Nederlands"),
        new(Czech, "Čeština"),
        new(Hungarian, "Magyar"),
        new(Ukrainian, "Українська"),
        new(Thai, "ไทย"),
        new(Swedish, "Svenska"),
        new(Finnish, "Suomi")
    ];

    public static readonly IReadOnlyList<string> Preferences =
        Languages.Select(language => language.Code).ToArray();

    public static string Resolve(string preference) =>
        Languages.Any(language => language.Code == preference)
            ? preference
            : English;

}

public sealed record UiLanguageDefinition(string Code, string NativeName);
