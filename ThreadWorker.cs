// ThreadWorker.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 线程工作者 - 一次性进程执行管理。
/// 负责启动 claude 进程、异步读取 stdout/stderr、超时处理、资源清理。
/// stdout 使用 BaseStream 异步字节读取（解决管道缓冲问题），stderr 使用 ErrorDataReceived 事件。
/// </summary>
public class ThreadWorker : IDisposable
{
    private Process connectedProcess;
    private bool isConnected = false;
    private string processId = "";
    private bool isDisposed = false;

    // 事件回调
    public event Action<string> OnMessageReceived;
    public event Action<string> OnErrorReceived;
    public event Action<string> OnSystemMessage;
    public event Action<int> OnProcessCompleted;

    public bool IsConnected => isConnected;
    public string ProcessId => processId;

    /// <summary>
    /// 一次性执行进程并等待完成。
    /// stdout 使用 BaseStream 异步字节读取（不等待行边界），stderr 使用 ErrorDataReceived 事件，
    /// 使用 Exited 事件 + TaskCompletionSource 等待退出（无线程阻塞）。
    /// </summary>
    /// <param name="path">可执行文件路径</param>
    /// <param name="args">命令行参数</param>
    /// <param name="timeoutMs">超时时间（毫秒），默认 180 秒</param>
    /// <returns>进程退出代码；-1 表示超时或被终止</returns>
    public async Task<int> RunOneShot(string path, string args, int timeoutMs = 180000)
    {
        // 确保没有遗留进程
        KillProcess();

        if (string.IsNullOrEmpty(path))
        {
            OnSystemMessage?.Invoke("进程路径不能为空");
            return -1;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            // 设置环境变量尝试强制行缓冲输出（解决管道缓冲问题）
            startInfo.EnvironmentVariables["FORCE_COLOR"] = "1";
            startInfo.EnvironmentVariables["CLAUDE_NO_BUFFER"] = "1";

            connectedProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var exitTcs = new TaskCompletionSource<int>();
            var outputDrainTcs = new TaskCompletionSource<bool>();
            var errorDrainTcs = new TaskCompletionSource<bool>();

            connectedProcess.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    OnErrorReceived?.Invoke(e.Data);
                else
                    errorDrainTcs.TrySetResult(true);
            };

            connectedProcess.Exited += (sender, e) =>
            {
                try
                {
                    exitTcs.TrySetResult(connectedProcess.ExitCode);
                }
                catch
                {
                    exitTcs.TrySetResult(-1);
                }
            };

            connectedProcess.Start();
            ReadStdoutStreamAsync(connectedProcess.StandardOutput.BaseStream, outputDrainTcs);
            connectedProcess.BeginErrorReadLine();

            isConnected = true;
            processId = connectedProcess.Id.ToString();

            OnSystemMessage?.Invoke($"进程已启动 (PID: {processId})");

            int exitCode;

            // 等待进程退出或超时（使用 Task.WhenAny，无线程阻塞）
            var exitTask = exitTcs.Task;
            var timeoutTask = Task.Delay(timeoutMs);

            if (await Task.WhenAny(exitTask, timeoutTask) == exitTask)
            {
                // 进程正常退出
                exitCode = exitTask.Result;

                // 等待输出流排空（等待 null 事件或超时）
                try
                {
                    await Task.WhenAny(
                        Task.WhenAll(outputDrainTcs.Task, errorDrainTcs.Task),
                        Task.Delay(5000)
                    );
                }
                catch { }

                OnSystemMessage?.Invoke($"进程已退出，退出代码: {exitCode}");
            }
            else
            {
                // 超时
                OnSystemMessage?.Invoke($"进程超时 ({timeoutMs / 1000}s)，正在终止...");
                try
                {
                    if (connectedProcess != null && !connectedProcess.HasExited)
                        connectedProcess.Kill();
                }
                catch { }
                exitCode = -1;
            }

            CleanupResources();
            OnProcessCompleted?.Invoke(exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            OnErrorReceived?.Invoke($"RunOneShot 失败: {ex.Message}");
            OnSystemMessage?.Invoke($"执行失败: {ex.Message}");
            CleanupResources();
            OnProcessCompleted?.Invoke(-1);
            return -1;
        }
    }

    /// <summary>
    /// 从进程 stdout BaseStream 异步读取字节，不等待行边界。
    /// 即使 CLI 使用块缓冲，缓冲刷新时也能立即获取数据，
    /// 解决 RedirectStandardOutput 管道导致的块缓冲问题。
    /// </summary>
    private async void ReadStdoutStreamAsync(Stream stream, TaskCompletionSource<bool> drainTcs)
    {
        try
        {
            byte[] buffer = new byte[4096];
            var lineBuilder = new System.Text.StringBuilder();

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    // EOF — 刷新剩余不完整行
                    if (lineBuilder.Length > 0)
                    {
                        string remaining = lineBuilder.ToString();
                        OnMessageReceived?.Invoke(remaining);
                    }
                    break;
                }

                string chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                int start = 0;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (chunk[i] == '\n')
                    {
                        lineBuilder.Append(chunk, start, i - start);
                        string line = lineBuilder.ToString().TrimEnd('\r');
                        OnMessageReceived?.Invoke(line);
                        lineBuilder.Clear();
                        start = i + 1;
                    }
                }
                // 追加剩余部分（不完整的行）
                if (start < chunk.Length)
                {
                    lineBuilder.Append(chunk, start, chunk.Length - start);
                }
            }
        }
        catch (Exception ex)
        {
            OnErrorReceived?.Invoke($"Stdout read error: {ex.Message}");
        }
        finally
        {
            drainTcs.TrySetResult(true);
        }
    }

    /// <summary>
    /// 强制终止当前进程
    /// </summary>
    public void KillProcess()
    {
        try
        {
            if (connectedProcess != null && !connectedProcess.HasExited)
            {
                connectedProcess.Kill();
                connectedProcess.WaitForExit(1000);
                OnSystemMessage?.Invoke("进程已被终止");
            }
        }
        catch { }

        CleanupResources();
    }

    /// <summary>
    /// 检查进程是否仍在运行
    /// </summary>
    public bool IsProcessRunning()
    {
        if (!isConnected || connectedProcess == null)
            return false;

        try
        {
            return !connectedProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    #region Cleanup

    private void CleanupResources()
    {
        try
        {
            if (connectedProcess != null)
            {
                try { connectedProcess.CancelOutputRead(); } catch { }
                try { connectedProcess.CancelErrorRead(); } catch { }
                connectedProcess.Dispose();
            }
        }
        catch { }

        connectedProcess = null;
        isConnected = false;
        processId = "";
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        KillProcess();

        GC.SuppressFinalize(this);
    }

    ~ThreadWorker()
    {
        Dispose();
    }

    #endregion
}
