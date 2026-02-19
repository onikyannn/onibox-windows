namespace Onibox.Models;

public sealed class Settings
{
    public string? ConfigUrl { get; set; }
    public string? LastConfigPath { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public bool AutoStart { get; set; }
    public InboundMode InboundMode { get; set; } = InboundMode.Proxy;
}
