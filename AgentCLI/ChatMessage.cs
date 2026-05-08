// ChatMessage.cs
using System;

/// <summary>
/// 对话消息角色枚举
/// </summary>
public enum MessageRole
{
    User,
    Claude,
    System
}

/// <summary>
/// 对话消息数据模型
/// </summary>
[Serializable]
public class ChatMessage
{
    public MessageRole Role { get; set; }
    public string Content { get; set; }
    public DateTime Timestamp { get; set; }

    [NonSerialized] public string NonTextContent;
    [NonSerialized] public bool DetailsFoldoutOpen;

    public ChatMessage()
    {
        Timestamp = DateTime.Now;
    }

    public ChatMessage(MessageRole role, string content)
    {
        Role = role;
        Content = content;
        Timestamp = DateTime.Now;
    }
}
