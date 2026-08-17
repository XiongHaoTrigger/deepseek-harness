using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DeepSeekHarness.Desktop;

public interface IDshWebServer
{
    Task<Uri> StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

public sealed class DshWebServer : IDshWebServer
{
    private readonly string repositoryRoot;
    private Process? process;
    private WindowsProcessJob? job;

    public DshWebServer(string repositoryRoot)
    {
        this.repositoryRoot = repositoryRoot;
    }

    public async Task<Uri> StartAsync(CancellationToken cancellationToken)
    {
        await StopAsync();
        var output = new WebServerStartupOutput();
        var startedProcess = new Process { StartInfo = CreateStartInfo(repositoryRoot), EnableRaisingEvents = true };
        var startedJob = WindowsProcessJob.Create();

        if (!startedProcess.Start())
        {
            startedProcess.Dispose();
            startedJob.Dispose();
            throw new InvalidOperationException("无法启动 pnpm.cmd。");
        }

        try
        {
            startedJob.Assign(startedProcess);
        }
        catch
        {
            startedProcess.Kill(entireProcessTree: true);
            startedProcess.Dispose();
            startedJob.Dispose();
            throw;
        }

        process = startedProcess;
        job = startedJob;
        var stdoutPump = PumpOutputAsync(startedProcess.StandardOutput, output.ConsumeStandardOutputAsync);
        var stderrPump = PumpOutputAsync(startedProcess.StandardError, output.ConsumeStandardErrorAsync);

        try
        {
            var started = output.WaitForUrlAsync();
            var exited = startedProcess.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(started, exited);
            if (completed == started)
            {
                return await started;
            }

            await exited;
            await Task.WhenAll(stdoutPump, stderrPump);
            throw new InvalidOperationException(output.FailureMessage);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        var activeProcess = Interlocked.Exchange(ref process, null);
        var activeJob = Interlocked.Exchange(ref job, null);

        try
        {
            activeJob?.Dispose();
            if (activeProcess is not null && !activeProcess.HasExited)
            {
                activeProcess.Kill(entireProcessTree: true);
            }

            if (activeProcess is not null)
            {
                await activeProcess.WaitForExitAsync();
            }
        }
        finally
        {
            activeProcess?.Dispose();
        }
    }

    private static ProcessStartInfo CreateStartInfo(string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("/d");
        info.ArgumentList.Add("/s");
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("pnpm.cmd dsh web --port 0");
        return info;
    }

    private static async Task PumpOutputAsync(StreamReader reader, Func<string, Task> consume)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await consume(line);
        }
    }
}

internal sealed class WindowsProcessJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly SafeFileHandle handle;

    private WindowsProcessJob(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    public static WindowsProcessJob Create()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 Windows Job Object。");
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法配置 Windows Job Object。");
        }

        return new WindowsProcessJob(handle);
    }

    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将 pnpm.cmd 加入 Windows Job Object。");
        }
    }

    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}

public sealed class WebServerStartupOutput
{
    private readonly TaskCompletionSource<Uri> url = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder standardError = new();

    public string FailureMessage => standardError.Length == 0
        ? "DeepSeek Harness 未输出 Web 地址。"
        : $"DeepSeek Harness 未输出 Web 地址。{Environment.NewLine}{Environment.NewLine}{standardError}";

    public Task ConsumeStandardOutputAsync(string line)
    {
        if (TryParseWebUrl(line, out var parsedUrl))
        {
            url.TrySetResult(parsedUrl);
        }

        return Task.CompletedTask;
    }

    public Task ConsumeStandardErrorAsync(string line)
    {
        if (standardError.Length > 0)
        {
            standardError.AppendLine();
        }

        standardError.Append(line);
        return Task.CompletedTask;
    }

    public Task<Uri> WaitForUrlAsync() => url.Task;

    public static bool TryParseWebUrl(string line, out Uri url)
    {
        const string prefix = "dsh web: ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !Uri.TryCreate(line[prefix.Length..], UriKind.Absolute, out var parsedUrl))
        {
            url = null!;
            return false;
        }

        if (parsedUrl.Scheme != Uri.UriSchemeHttp || parsedUrl.Host != "127.0.0.1" || parsedUrl.Port is <= 0 or > 65535)
        {
            url = null!;
            return false;
        }

        url = parsedUrl;
        return true;
    }
}
