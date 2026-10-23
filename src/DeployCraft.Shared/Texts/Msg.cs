using System.Globalization;
using System.Resources;

namespace DeployCraft.Server;

/// <summary>
/// Messages written by the service, in French (Msg.resx) or English (Msg.en.resx), following the current UI
/// culture: the console's language for a request (Accept-Language), otherwise the server's Windows language
/// (deployments and schedules running in the background). Shared by the service and console through DeployCraft.Shared.
/// Every key exists in both files (checked by the tests).
/// </summary>
public static class Msg
{
    public const string French = "fr", English = "en";

    private static readonly ResourceManager Texts = new(typeof(Msg));

    /// <summary>Text of a key; the key itself between brackets if it is missing, so the gap shows.</summary>
    public static string Get(string key) => Texts.GetString(key) ?? $"[{key}]";

    /// <summary>Text with placeholders {0}, {1}… filled in.</summary>
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>French or English: the first supported language of an Accept-Language header or culture name, else null.</summary>
    public static string? Supported(string? languages) =>
        languages?.Split(',').Select(l => l.Split(';')[0].Trim().Split('-')[0].ToLowerInvariant())
            .FirstOrDefault(l => l is French or English);

    /// <summary>Language of the service's own messages: the server's Windows language when it is French, else English.</summary>
    public static string ServerLanguage => Supported(CultureInfo.InstalledUICulture.Name) == French ? French : English;
}
