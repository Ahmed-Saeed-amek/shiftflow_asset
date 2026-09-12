using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ShiftFlow.Application.AI;

/// <summary>Rich-reply payloads the chat client renders underneath the assistant's text. Tools
/// attach one (or an array) to their result under a property named <c>ui</c>; the orchestrator
/// strips that property before the tool result reaches the model (see
/// <see cref="AiToolResultEnvelope"/>) and returns the collected attachments alongside the answer
/// text, so the model never spends tokens on presentation data it didn't need to read.
///
/// Every record carries a read-only <c>Kind</c> discriminator and is serialized with the ASP.NET
/// Core web defaults (camelCase), which is exactly the contract the frontend is built against.
/// Records are deliberately not a polymorphic hierarchy: <c>ui</c> is always typed <c>object</c>
/// so System.Text.Json serializes the runtime type and no [JsonDerivedType] plumbing is needed.</summary>
public sealed record AiTableAttachment
{
    public string Kind => "table";
    public string? Title { get; init; }
    public IReadOnlyList<AiTableColumn> Columns { get; init; } = [];
    /// <summary>One dictionary per row, keyed by column key. Two reserved keys the client
    /// understands: <c>_url</c> (app-relative link for the row) and <c>_status</c> (raw status /
    /// stage string it renders as a badge). Dictionary keys are serialized verbatim — the web
    /// defaults only camelCase property names, not dictionary keys — so the underscores survive.</summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = [];
}

public sealed record AiTableColumn(string Key, string Label);

public sealed record AiCardsAttachment
{
    public string Kind => "cards";
    public IReadOnlyList<AiCard> Items { get; init; } = [];
}

public sealed record AiCard
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public AiBadge? Badge { get; init; }
    public IReadOnlyList<AiField> Fields { get; init; } = [];
    public string? Url { get; init; }
}

public sealed record AiBadge(string Text, string? Status = null);
public sealed record AiField(string Label, string Value);

public sealed record AiLinksAttachment
{
    public string Kind => "links";
    public IReadOnlyList<AiLinkItem> Items { get; init; } = [];
}

public sealed record AiLinkItem(string Label, string Url, string? Icon = null);

public sealed record AiDownloadAttachment
{
    public string Kind => "download";
    public string Label { get; init; } = "";
    public string Url { get; init; } = "";
}

/// <summary>Rendered as a confirm/cancel prompt. <see cref="Token"/> is the key into
/// <see cref="IPendingActionStore"/>; the client posts it back to /AiAssistant/Confirm or
/// /AiAssistant/Dismiss.</summary>
public sealed record AiConfirmAttachment
{
    public string Kind => "confirm";
    public string Token { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public IReadOnlyList<string>? Items { get; init; }
    public string ActionLabel { get; init; } = "";
    public bool? Danger { get; init; }
}

/// <summary>What one assistant turn produced: the text to speak/show plus every attachment the
/// tools emitted along the way, in call order. Attachments are already-serialized JsonNodes so the
/// controller can hand them straight to the JSON result without a second, differently-configured
/// serialization pass.</summary>
public sealed record AiRunResult(string Text, List<object> Attachments);

/// <summary>Splits a tool's return value into (what the model sees, what the client renders).
/// Pure and static specifically so it can be unit-tested without an LLM, a DbContext, or an
/// orchestrator instance.</summary>
public static class AiToolResultEnvelope
{
    public const string UiPropertyName = "ui";

    /// <summary>Serializes <paramref name="result"/>, removes its top-level <c>ui</c> property if
    /// present, and returns the remaining JSON (what goes into the ToolChatMessage) together with
    /// the attachments that were carried there. A <c>ui</c> array contributes each of its elements;
    /// a null <c>ui</c> contributes nothing. Anything that isn't a JSON object is passed through
    /// untouched.</summary>
    public static (string Json, List<object> Attachments) Strip(object? result, JsonSerializerOptions options)
    {
        var attachments = new List<object>();
        var node = JsonSerializer.SerializeToNode(result, options);

        if (node is JsonObject obj && obj.ContainsKey(UiPropertyName))
        {
            // Copy before removing: a JsonNode keeps a parent pointer, and re-serializing one that
            // still belongs to another document throws. Parsing the copy detaches it cleanly.
            var ui = obj[UiPropertyName]?.ToJsonString(options);
            obj.Remove(UiPropertyName);

            var uiNode = ui is null ? null : JsonNode.Parse(ui);
            if (uiNode is JsonArray arr)
            {
                foreach (var item in arr.ToList())
                {
                    if (item is null) continue;
                    arr.Remove(item);
                    attachments.Add(item);
                }
            }
            else if (uiNode is not null)
            {
                attachments.Add(uiNode);
            }
        }

        return (node?.ToJsonString(options) ?? "null", attachments);
    }
}
