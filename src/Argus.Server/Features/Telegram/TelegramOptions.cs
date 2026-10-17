using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Argus:Telegram";

    /// <summary>The Bot API; tests point this at a fake.</summary>
    [Required, Url]
    public string ApiUrl { get; set; } = "https://api.telegram.org";

    /// <summary>Ask Telegram for button presses and messages to the bot (tests switch this off and hand updates in).</summary>
    public bool BackgroundPolling { get; set; } = true;
}
