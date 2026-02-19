using System.Text.Json;
using System.Text.Json.Serialization;

namespace Onibox.Models;

[JsonConverter(typeof(InboundModeJsonConverter))]
public enum InboundMode
{
    Proxy = 0,
    Tun = 1
}

public static class InboundModeExtensions
{
    public static string ToInboundType(this InboundMode mode) => mode switch
    {
        InboundMode.Proxy => "mixed",
        InboundMode.Tun => "tun",
        _ => "mixed"
    };

    public static string ToStorageValue(this InboundMode mode) => mode switch
    {
        InboundMode.Proxy => "proxy",
        InboundMode.Tun => "tun",
        _ => "proxy"
    };
}

public sealed class InboundModeJsonConverter : JsonConverter<InboundMode>
{
    public override InboundMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            return Parse(raw);
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
        {
            return value switch
            {
                1 => InboundMode.Tun,
                _ => InboundMode.Proxy
            };
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return InboundMode.Proxy;
        }

        throw new JsonException($"Unsupported inbound mode token: {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, InboundMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToStorageValue());

    private static InboundMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InboundMode.Proxy;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "tun" => InboundMode.Tun,
            "mixed" => InboundMode.Proxy,
            _ => InboundMode.Proxy
        };
    }
}
