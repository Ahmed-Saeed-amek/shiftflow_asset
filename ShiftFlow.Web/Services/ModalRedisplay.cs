using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ShiftFlow.Web.Services;

/// <summary>The small reference-data screens (Order Types, Block Reasons, Maintenance Action Types,
/// Asset Categories, Asset Action Types) edit rows in a modal and redirect back to the list on a
/// validation failure, which used to close the modal and discard everything the user typed. The
/// POST captures its submitted values here; the list view hands them to modal-editor.js, which
/// reopens the modal pre-filled with them and shows the error. A successful save clears them.</summary>
public static class ModalRedisplay
{
    private const string ValuesKey = "ModalValues";
    private const string ModeKey = "ModalMode";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Capture(ITempDataDictionary tempData, object values, bool isEdit)
    {
        tempData[ValuesKey] = JsonSerializer.Serialize(values, Options);
        tempData[ModeKey] = isEdit ? "edit" : "create";
    }

    public static void Clear(ITempDataDictionary tempData)
    {
        tempData.Remove(ValuesKey);
        tempData.Remove(ModeKey);
    }

    /// <summary>The captured payload as a ready-to-emit JS object literal, or "null". Uses Peek so
    /// the layout's own TempData["Error"] toast still renders.</summary>
    public static string ReopenScript(ITempDataDictionary tempData)
    {
        if (tempData.Peek(ValuesKey) is not string json) return "null";
        var mode = tempData.Peek(ModeKey) as string ?? "create";
        var error = tempData.Peek("Error") as string;
        return $"{{ mode: {JsonSerializer.Serialize(mode)}, values: {json}, error: {JsonSerializer.Serialize(error)} }}";
    }
}
