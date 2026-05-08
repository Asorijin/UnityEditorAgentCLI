// AgentCLI.cs
using UnityEngine;
using UnityEditor;
using System;

/// <summary>
/// 上下文模式：提供上下文（拼接历史对话）或不提供上下文（仅发送当前问题）。
/// </summary>
public enum ContextMode
{
    WithContext,
    WithoutContext
}

/// <summary>
/// Claude Code Chat 编辑器窗口。
/// 每次提问启动一次性 claude 进程，将对话上下文嵌入 prompt。
/// 支持流式显示 Claude 回复，角色区分消息气泡，上下文管理。
/// </summary>
public class AgentCLI : EditorWindow
{
    #region State

    private ThreadWorker threadWorker;
    private ContextManager contextManager;

    private string userInput = "";
    private string streamingText = "";
    private string streamingNonTextContent = "";
    private bool streamingDetailsFoldout;
    private bool isProcessing = false;
    private Vector2 chatScrollPosition;
    private bool showScrollNotification = false;
    private float estimatedContentHeight = 0f;
    private float cachedChatLabelBottomY = 0f;

    // 持久化设置
    private string claudePath;
    private ContextMode contextMode = ContextMode.WithContext;
    private const int maxContextKB = 100;

    // 重绘节流
    private DateTime lastRepaintTime = DateTime.MinValue;
    private const double MinRepaintIntervalMs = 10.0; // 100fps

    // 缓存的 Texture 和 GUIStyle
    private Texture2D userTex;
    private Texture2D claudeTex;
    private Texture2D systemTex;
    private GUIStyle userBubbleStyle;
    private GUIStyle claudeBubbleStyle;
    private GUIStyle systemBubbleStyle;
    private bool stylesInitialized = false;

    private const int MaxPromptChars = 30000;

    #endregion

    #region Unity Lifecycle

    [MenuItem("Tools/Claude Code Chat")]
    public static void ShowWindow()
    {
        var window = GetWindow<AgentCLI>("Claude Code Chat");
        window.minSize = new Vector2(500, 400);
        window.Show();
    }

    private void OnEnable()
    {
        threadWorker = new ThreadWorker();
        contextManager = new ContextManager();
        LoadSettings();
    }

    private void OnDisable()
    {
        // 确保无事件残留
        UnsubscribeWorkerEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeWorkerEvents();

        try
        {
            threadWorker?.KillProcess();
        }
        catch { }

        threadWorker?.Dispose();
        threadWorker = null;

        CleanupStyles();
    }

    #endregion

    #region OnGUI

    private void OnGUI()
    {
        InitStyles();

        DrawTitle();
        EditorGUILayout.Space(4);
        DrawChatArea();
        EditorGUILayout.Space(4);
        DrawInputArea();
        EditorGUILayout.Space(2);
        DrawStatusBar();
    }

    #endregion

    #region Draw Methods

    private void DrawTitle()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Claude Code Chat", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        string[] contextModeOptions = { "提供上下文", "不提供上下文" };
        int selectedIndex = contextMode == ContextMode.WithContext ? 0 : 1;
        int newIndex = EditorGUILayout.Popup(selectedIndex, contextModeOptions, GUILayout.Width(110));
        if (newIndex != selectedIndex)
        {
            contextMode = newIndex == 0 ? ContextMode.WithContext : ContextMode.WithoutContext;
            SaveContextMode();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawChatArea()
    {
        EditorGUILayout.LabelField("Conversation", EditorStyles.boldLabel);

        // Capture label bottom position once during Repaint for height calculation
        if (Event.current.type == EventType.Repaint)
        {
            cachedChatLabelBottomY = GUILayoutUtility.GetLastRect().yMax;
        }

        // Calculate available height: window height minus label bottom minus reserved bottom area
        float bottomReserved = 155f;
        float availableHeight = Mathf.Max(80f, position.height - cachedChatLabelBottomY - bottomReserved);

        // Scroll view fills the calculated space
        chatScrollPosition = EditorGUILayout.BeginScrollView(chatScrollPosition,
            GUILayout.Height(availableHeight), GUILayout.ExpandWidth(true));

        // Render all confirmed messages
        foreach (var msg in contextManager.Messages)
        {
            DrawMessageBubble(msg);
            EditorGUILayout.Space(2);
        }

        // Render streaming reply (in-progress)
        if (isProcessing && (!string.IsNullOrEmpty(streamingText) || !string.IsNullOrEmpty(streamingNonTextContent)))
        {
            DrawStreamingBubble();
            EditorGUILayout.Space(2);
        }

        // Measure content height for scroll-notification logic
        Rect contentEndMarker = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            estimatedContentHeight = contentEndMarker.y;
        }

        EditorGUILayout.EndScrollView();

        // --- Scroll notification logic ---
        if (Event.current.type == EventType.Repaint)
        {
            Rect scrollViewRect = GUILayoutUtility.GetLastRect();
            float viewportHeight = scrollViewRect.height;
            float maxScrollY = Mathf.Max(0f, estimatedContentHeight - viewportHeight);
            bool atBottom = chatScrollPosition.y >= maxScrollY - 10f;

            if (!isProcessing)
            {
                showScrollNotification = false;
            }
            else if (atBottom)
            {
                showScrollNotification = false;
            }
            else
            {
                showScrollNotification = true;
            }
        }

        // Notification banner: "New message ↓" — user can click to scroll to bottom
        if (showScrollNotification)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New message ↓", EditorStyles.miniButton, GUILayout.Width(120)))
            {
                chatScrollPosition = new Vector2(0, float.MaxValue);
                showScrollNotification = false;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawMessageBubble(ChatMessage msg)
    {
        GUIStyle style = GetBubbleStyle(msg.Role);
        string roleLabel = GetRoleLabel(msg.Role);
        bool isUser = msg.Role == MessageRole.User;
        bool isSystem = msg.Role == MessageRole.System;

        EditorGUILayout.BeginHorizontal();

        if (!isSystem)
        {
            if (isUser)
                GUILayout.FlexibleSpace();
        }

        float maxWidth = position.width * 0.80f;
        if (isSystem)
            maxWidth = position.width * 0.92f;

        EditorGUILayout.BeginVertical(style, GUILayout.MaxWidth(maxWidth));

        var roleStyle = new GUIStyle(EditorStyles.boldLabel);
        roleStyle.normal.textColor = isUser ? Color.white : (isSystem ? Color.gray : Color.white);
        EditorGUILayout.LabelField(roleLabel, roleStyle);

        var contentStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        contentStyle.normal.textColor = Color.white;
        EditorGUILayout.LabelField(msg.Content, contentStyle);

        if (msg.Role == MessageRole.Claude)
        {
            bool detailsOpen = msg.DetailsFoldoutOpen;
            DrawFoldoutsInBubble(msg.NonTextContent, ref detailsOpen);
            msg.DetailsFoldoutOpen = detailsOpen;
        }

        var timeStyle = new GUIStyle(EditorStyles.miniLabel);
        timeStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        EditorGUILayout.LabelField(msg.Timestamp.ToString("HH:mm:ss"), timeStyle);

        EditorGUILayout.EndVertical();

        if (!isSystem)
        {
            if (!isUser)
                GUILayout.FlexibleSpace();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawStreamingBubble()
    {
        GUIStyle style = claudeBubbleStyle;
        float maxWidth = position.width * 0.80f;

        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(style, GUILayout.MaxWidth(maxWidth));

        var roleStyle = new GUIStyle(EditorStyles.boldLabel);
        roleStyle.normal.textColor = Color.white;
        EditorGUILayout.LabelField("Claude (typing...)", roleStyle);

        var contentStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        contentStyle.normal.textColor = Color.white;
        EditorGUILayout.LabelField(streamingText, contentStyle);

        DrawFoldoutsInBubble(streamingNonTextContent, ref streamingDetailsFoldout);

        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawFoldoutsInBubble(string nonTextContent, ref bool detailsFoldout)
    {
        if (string.IsNullOrEmpty(nonTextContent))
            return;

        detailsFoldout = EditorGUILayout.Foldout(detailsFoldout, "Details", true);
        if (detailsFoldout)
        {
            EditorGUI.indentLevel++;
            var detailStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
            detailStyle.normal.textColor = new Color(0.65f, 0.65f, 0.75f, 1f);
            detailStyle.fontStyle = FontStyle.Italic;
            EditorGUILayout.LabelField(nonTextContent, detailStyle, GUILayout.MaxWidth(position.width * 0.72f));
            EditorGUI.indentLevel--;
        }
    }

    private void DrawInputArea()
    {
        EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);

        GUI.SetNextControlName("ChatInput");
        userInput = EditorGUILayout.TextArea(userInput, GUILayout.Height(60));

        if (Event.current.type == EventType.KeyDown
            && Event.current.keyCode == KeyCode.Return
            && !Event.current.shift)
        {
            if (GUI.GetNameOfFocusedControl() == "ChatInput")
            {
                SendMessage();
                Event.current.Use();
            }
        }

        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !isProcessing && !string.IsNullOrEmpty(userInput.Trim());
        if (GUILayout.Button("Send", GUILayout.Height(28)))
        {
            SendMessage();
        }
        GUI.enabled = true;

        if (GUILayout.Button("Clear", GUILayout.Height(28)))
        {
            ClearConversation();
        }

        GUI.enabled = isProcessing;
        if (GUILayout.Button("Stop", GUILayout.Height(28)))
        {
            StopProcessing();
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        GUI.color = isProcessing ? new Color(1f, 0.85f, 0.2f) : Color.green;
        EditorGUILayout.LabelField(isProcessing ? "● Thinking..." : "● Ready",
            GUILayout.Width(isProcessing ? 100 : 60));
        GUI.color = Color.white;

        int currentBytes = contextManager.GetCurrentContextBytes();
        float currentKB = currentBytes / 1024f;
        float maxKB = contextManager.MaxContextKB;
        EditorGUILayout.LabelField(
            $"Context: {currentKB:F1}KB / {maxKB:F0}KB",
            GUILayout.MinWidth(150));

        GUILayout.FlexibleSpace();

        EditorGUILayout.LabelField($"Messages: {contextManager.MessageCount}",
            GUILayout.Width(120));

        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region Actions

    private async void SendMessage()
    {
        if (isProcessing)
            return;

        string question = userInput.Trim();
        if (string.IsNullOrEmpty(question))
            return;

        userInput = "";
        isProcessing = true;
        streamingText = "";
        streamingNonTextContent = "";
        streamingDetailsFoldout = false;

        contextManager.AddMessage(new ChatMessage(MessageRole.User, question));

        string contextString = contextMode == ContextMode.WithContext
            ? contextManager.BuildContextString(question)
            : $"Current question: {question}";

        if (contextString.Length > MaxPromptChars)
        {
            contextManager.AddMessage(new ChatMessage(MessageRole.System,
                $"Error: Prompt too long ({contextString.Length} chars). Max: {MaxPromptChars}."));
            isProcessing = false;
            Repaint();
            return;
        }

        string escapedPrompt = contextString.Replace("\"", "\"\"");

        string resolvedPath = string.IsNullOrEmpty(claudePath) ? "claude" : claudePath;

        string args = $"-p \"{escapedPrompt}\" --verbose --output-format stream-json --include-partial-messages --dangerously-skip-permissions";

        SubscribeWorkerEvents();

        try
        {
            int exitCode = await threadWorker.RunOneShot(resolvedPath, args, 180000);

            EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                if (!string.IsNullOrEmpty(streamingText) || !string.IsNullOrEmpty(streamingNonTextContent))
                {
                    var msg = new ChatMessage(MessageRole.Claude, streamingText);
                    if (!string.IsNullOrEmpty(streamingNonTextContent))
                        msg.NonTextContent = streamingNonTextContent;
                    contextManager.AddMessage(msg);
                }
                else if (exitCode != 0)
                {
                    contextManager.AddMessage(new ChatMessage(MessageRole.System,
                        $"Claude exited with code {exitCode}. See Unity Console for details."));
                }

                streamingText = "";
                streamingNonTextContent = "";
                streamingDetailsFoldout = false;
                isProcessing = false;
                Repaint();
            };
        }
        finally
        {
            UnsubscribeWorkerEvents();
        }
    }

    private void ClearConversation()
    {
        contextManager.ClearMessages();
        streamingText = "";
        streamingNonTextContent = "";
        streamingDetailsFoldout = false;
        userInput = "";
        isProcessing = false;
        showScrollNotification = false;
        Repaint();
    }

    private void StopProcessing()
    {
        if (!isProcessing)
            return;

        threadWorker?.KillProcess();
        // OnProcessCompleted 事件会清理状态
    }

    #endregion

    #region Worker Event Handlers

    private void SubscribeWorkerEvents()
    {
        threadWorker.OnMessageReceived += OnClaudeOutput;
        threadWorker.OnErrorReceived += OnClaudeError;
        threadWorker.OnProcessCompleted += OnClaudeCompleted;
    }

    private void UnsubscribeWorkerEvents()
    {
        if (threadWorker == null)
            return;

        threadWorker.OnMessageReceived -= OnClaudeOutput;
        threadWorker.OnErrorReceived -= OnClaudeError;
        threadWorker.OnProcessCompleted -= OnClaudeCompleted;
    }

    private void OnClaudeOutput(string line)
    {
        string text = StreamJsonParser.ExtractText(line);
        string nonText = StreamJsonParser.ExtractNonTextContent(line);
        if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(nonText))
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                if (!string.IsNullOrEmpty(text))
                    streamingText += text;

                if (!string.IsNullOrEmpty(nonText))
                {
                    
                    streamingNonTextContent += nonText;
                }

                ThrottledRepaint();
            };
        }
    }

    private void OnClaudeError(string error)
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            Debug.LogWarning($"[Claude stderr] {error}");
        };
    }

    private void OnClaudeCompleted(int exitCode)
    {
        // 此事件在 OnMessageReceived 全部触发后调用。
        // RunOneShot 在返回前已等待输出任务排空，但 delayCall 可能尚在队列中。
        // 由于 delayCall 按注册顺序执行，此处不做额外处理；
        // 最终化在 SendMessage 的 await 后 delayCall 中完成。
    }

    #endregion

    #region Repaint Throttling

    /// <summary>
    /// 流式输出期间节流重绘：每秒最多约 60 次
    /// </summary>
    private void ThrottledRepaint()
    {
        var now = DateTime.Now;
        if ((now - lastRepaintTime).TotalMilliseconds >= MinRepaintIntervalMs)
        {
            lastRepaintTime = now;
            Repaint();
        }
    }

    #endregion

    #region Styles & Textures

    private void InitStyles()
    {
        if (stylesInitialized)
            return;

        // 用户消息 - 蓝色调
        userTex = MakeSolidTex(new Color(0.18f, 0.35f, 0.70f, 0.75f));
        userBubbleStyle = new GUIStyle(EditorStyles.helpBox)
        {
            normal = { background = userTex },
            wordWrap = true,
            padding = new RectOffset(8, 8, 6, 6),
            margin = new RectOffset(4, 4, 2, 2)
        };

        // Claude 消息 - 绿色调
        claudeTex = MakeSolidTex(new Color(0.15f, 0.55f, 0.25f, 0.75f));
        claudeBubbleStyle = new GUIStyle(EditorStyles.helpBox)
        {
            normal = { background = claudeTex },
            wordWrap = true,
            padding = new RectOffset(8, 8, 6, 6),
            margin = new RectOffset(4, 4, 2, 2)
        };

        // 系统消息 - 灰色调
        systemTex = MakeSolidTex(new Color(0.30f, 0.30f, 0.35f, 0.75f));
        systemBubbleStyle = new GUIStyle(EditorStyles.helpBox)
        {
            normal = { background = systemTex },
            wordWrap = true,
            padding = new RectOffset(8, 8, 6, 6),
            margin = new RectOffset(4, 4, 2, 2),
            alignment = TextAnchor.MiddleCenter
        };

        stylesInitialized = true;
    }

    private void CleanupStyles()
    {
        if (userTex != null) { DestroyImmediate(userTex); userTex = null; }
        if (claudeTex != null) { DestroyImmediate(claudeTex); claudeTex = null; }
        if (systemTex != null) { DestroyImmediate(systemTex); systemTex = null; }
        userBubbleStyle = null;
        claudeBubbleStyle = null;
        systemBubbleStyle = null;
        stylesInitialized = false;
    }

    private GUIStyle GetBubbleStyle(MessageRole role)
    {
        switch (role)
        {
            case MessageRole.User: return userBubbleStyle;
            case MessageRole.Claude: return claudeBubbleStyle;
            case MessageRole.System: return systemBubbleStyle;
            default: return systemBubbleStyle;
        }
    }

    private string GetRoleLabel(MessageRole role)
    {
        switch (role)
        {
            case MessageRole.User: return "You";
            case MessageRole.Claude: return "Claude";
            case MessageRole.System: return "System";
            default: return "?";
        }
    }

    /// <summary>
    /// 创建纯色 2x2 Texture2D 作为 GUIStyle background
    /// </summary>
    private Texture2D MakeSolidTex(Color color)
    {
        var tex = new Texture2D(2, 2);
        Color[] pixels = { color, color, color, color };
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    #endregion

    #region Settings Persistence

    private void LoadSettings()
    {
        claudePath = EditorPrefs.GetString("ClaudeChat_ClaudePath", "claude");
        contextMode = (ContextMode)EditorPrefs.GetInt("ClaudeChat_ContextMode", 0);

        // 同步固定值到 ContextManager
        contextManager.MaxContextKB = maxContextKB;
    }

    private void SaveContextMode()
    {
        EditorPrefs.SetInt("ClaudeChat_ContextMode", (int)contextMode);
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString("ClaudeChat_ClaudePath", claudePath);
        contextManager.MaxContextKB = maxContextKB;
    }

    #endregion
}
