// StreamJsonParser.cs
using System;
using UnityEngine;

/// <summary>
/// Claude CLI streaming JSON wrapper.
/// Every stdout line is: {"type":"stream_event","event":{...},"session_id":"...","parent_tool_use_id":null,"uuid":"..."}
/// </summary>
[Serializable]
public class StreamEventWrapper
{
    public string type;                  // always "stream_event"
    public StreamEvent @event;           // the inner event (C# keyword escaped with @)
    public string session_id;
    public string parent_tool_use_id;    // null when event is not spawned by a tool
    public string uuid;
}

/// <summary>
/// Inner streaming event from Claude CLI. Maps to the "event" field inside the wrapper.
/// </summary>
[Serializable]
public class StreamEvent
{
    public string type;                  // "content_block_start" | "content_block_delta" | "content_block_stop" | "message_start" | "message_delta" | "message_stop" | "tool_result" | "result"
    public string subtype;               // for "result" events: "success" | "error"
    public int index;
    public ContentBlock content_block;
    public Delta delta;
    public MessageInfo message;

    // tool_result fields
    public string content;
    public string tool_use_id;
    public bool is_error;
}

[Serializable]
public class ContentBlock
{
    public string type;     // "text" | "thinking" | "tool_use"
    public string text;
    public string thinking;
    public string name;
    public string id;
}

[Serializable]
public class Delta
{
    public string type;             // "text_delta" | "thinking_delta" | "input_json_delta" | "signature_delta"
    public string text;
    public string thinking;
    public string partial_json;
    public string signature;
}

[Serializable]
public class MessageInfo
{
    public string id;
    public string model;
    public string role;
    public string stop_reason;
    public string stop_sequence;
    public ContentBlock[] content;
    public UsageInfo usage;
}

[Serializable]
public class UsageInfo
{
    public int input_tokens;
    public int output_tokens;
}

public static class StreamJsonParser
{
    /// <summary>
    /// Extract visible text from text_delta delta events and text content_block_start events.
    /// </summary>
    public static string ExtractText(string jsonLine)
    {
        if (string.IsNullOrEmpty(jsonLine))
            return "";

        StreamEvent evt;
        if (!TryUnwrapEvent(jsonLine, out evt))
            return "";

        if (evt.type == "content_block_delta" && evt.delta != null && evt.delta.type == "text_delta")
            return evt.delta.text ?? "";

        if (evt.type == "content_block_start" && evt.content_block != null && evt.content_block.type == "text")
            return evt.content_block.text ?? "";

        return "";
    }

    /// <summary>
    /// Extract non-text content (thinking, tool calls, tool results) for UI "Details" foldout.
    /// Returns null when the event contains no non-text content.
    /// </summary>
    public static string ExtractNonTextContent(string jsonLine)
    {
        if (string.IsNullOrEmpty(jsonLine))
            return null;

        StreamEvent evt;
        if (!TryUnwrapEvent(jsonLine, out evt))
            return null;

        switch (evt.type)
        {
            case "content_block_start":
                return "";
            case "content_block_delta":
                return ExtractBlockDelta(evt);
            case "tool_result":
                return "";
        }

        return null;
    }

    private static string ExtractBlockDelta(StreamEvent evt)
    {
        var delta = evt.delta;
        if (delta == null) return null;

        switch (delta.type)
        {
            case "text_delta":
                return null;

            case "thinking_delta":
                if (!string.IsNullOrEmpty(delta.thinking))
                    return delta.thinking
                        ;
                return null;

            case "input_json_delta":
                if (!string.IsNullOrEmpty(delta.partial_json))
                    return delta.partial_json;
                return null;

        }

        return null;
    }

    #region Public Helpers

    public static string GetEventType(string jsonLine)
    {
        if (string.IsNullOrEmpty(jsonLine)) return "";
        StreamEvent evt;
        if (TryUnwrapEvent(jsonLine, out evt))
            return evt.type ?? "";
        return "";
    }

    public static bool IsResultError(string jsonLine)
    {
        StreamEvent evt;
        if (TryUnwrapEvent(jsonLine, out evt) && evt.type == "result")
            return evt.subtype == "error";
        return false;
    }

    public static string ExtractToolResult(string jsonLine)
    {
        if (string.IsNullOrEmpty(jsonLine)) return "";
        StreamEvent evt;
        if (TryUnwrapEvent(jsonLine, out evt))
            return evt.content ?? "";
        return "";
    }

    #endregion

    #region Internal

    private static bool TryUnwrapEvent(string jsonLine, out StreamEvent evt)
    {
        evt = null;
        try
        {
            var wrapper = JsonUtility.FromJson<StreamEventWrapper>(jsonLine);
            if (wrapper == null || wrapper.@event == null)
                return false;
            evt = wrapper.@event;
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
