// ContextManager.cs
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 管理对话历史，控制上下文大小。
/// 超出限制时裁剪最旧的 User+Claude 消息对。
/// 提供格式化方法将历史转为 plain text 供 claude -p 参数使用。
/// </summary>
public class ContextManager
{
    private List<ChatMessage> messages = new List<ChatMessage>();

    private const int DefaultMaxContextKB = 100;
    public const int MinContextKB = 10;
    public int MaxContextKB { get; set; } = DefaultMaxContextKB;

    

    /// <summary>
    /// 对话消息列表（只读）
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages => messages;

    /// <summary>
    /// 消息总数
    /// </summary>
    public int MessageCount => messages.Count;

    /// <summary>
    /// 添加消息
    /// </summary>
    public void AddMessage(ChatMessage message)
    {
        messages.Add(message);
    }

    /// <summary>
    /// 清空所有消息
    /// </summary>
    public void ClearMessages()
    {
        messages.Clear();
    }

    /// <summary>
    /// 构建上下文字符串供 claude -p 参数使用。
    /// 格式：
    /// Previous conversation:
    /// User: <msg>
    /// Claude: <response>
    ///
    /// Current question: <new question>
    ///
    /// 如果超出最大上下文限制，自动裁剪最旧的 User+Claude 对。
    /// </summary>
    public string BuildContextString(string currentQuestion)
    {
        // 先构建一次
        string result = BuildFormattedString(currentQuestion);
        int maxBytes = MaxContextKB * 1024;

        // 如果超出限制，裁剪最旧的消息对
        while (result.Length > maxBytes && messages.Count >= 2)
        {
            TrimOldestPair();
            result = BuildFormattedString(currentQuestion);
        }

        return result;
    }

    /// <summary>
    /// 获取当前上下文的大致大小（字节），用于UI显示。
    /// 使用快速估算而非构建完整字符串。
    /// </summary>
    public int GetCurrentContextBytes()
    {
        int total = 0;
        foreach (var msg in messages)
        {
            if (msg.Role == MessageRole.User || msg.Role == MessageRole.Claude)
            {
                total += (msg.Content ?? "").Length + 30; // 内容 + 角色标签开销
            }
        }
        total += 60; // "Previous conversation:\n\nCurrent question: \n" 开销
        return total;
    }

    /// <summary>
    /// 构建格式化的上下文字符串（不裁剪）。
    /// 仅包含 User 和 Claude 消息，System 消息不入上下文。
    /// </summary>
    private string BuildFormattedString(string currentQuestion)
    {
        var sb = new StringBuilder();

        bool hasConversation = false;
        foreach (var msg in messages)
        {
            if (msg.Role == MessageRole.User || msg.Role == MessageRole.Claude)
            {
                hasConversation = true;
                break;
            }
        }

        if (hasConversation)
        {
            sb.AppendLine("Previous conversation:");
            foreach (var msg in messages)
            {
                switch (msg.Role)
                {
                    case MessageRole.User:
                        sb.AppendLine($"User: {msg.Content}");
                        break;
                    case MessageRole.Claude:
                        sb.AppendLine($"Claude: {msg.Content}");
                        break;
                    // System 消息不包含在上下文中
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine($"Current question: {currentQuestion}");
        return sb.ToString();
    }

    /// <summary>
    /// 裁剪最旧的一对 User+Claude 消息。
    /// 从列表开头移除前两个非 System 消息（通常就是最旧的一对）。
    /// </summary>
    private void TrimOldestPair()
    {
        int removed = 0;
        for (int i = 0; i < messages.Count && removed < 2; i++)
        {
            if (messages[i].Role == MessageRole.User || messages[i].Role == MessageRole.Claude)
            {
                messages.RemoveAt(i);
                i--; // 索引回退以继续从同一位置扫描
                removed++;
            }
        }
    }
}
