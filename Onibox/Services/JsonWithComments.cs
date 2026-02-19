using System.Text.Json;
using System.Text.Json.Nodes;

namespace Onibox.Services;

public static class JsonWithComments
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static JsonNode? ParseNode(string json)
        => JsonNode.Parse(json, documentOptions: DocumentOptions);

    public static JsonDocument ParseDocument(string json)
        => JsonDocument.Parse(json, DocumentOptions);
}
