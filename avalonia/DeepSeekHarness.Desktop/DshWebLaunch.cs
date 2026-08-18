namespace DeepSeekHarness.Desktop;

public sealed class DshWebLaunch
{
    private DshWebLaunch(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        FileName = fileName;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        EnvironmentVariables = environmentVariables;
    }

    public string FileName { get; }

    public string WorkingDirectory { get; }

    public IReadOnlyList<string> Arguments { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    public static DshWebLaunch ForDevelopment(string repositoryRoot)
    {
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"DeepSeek Harness 仓库目录不存在：{repositoryRoot}");
        }

        return new DshWebLaunch(
            "cmd.exe",
            repositoryRoot,
            ["/d", "/s", "/c", "pnpm.cmd dsh web --port 0"],
            new Dictionary<string, string>());
    }

    public static DshWebLaunch ForAttachedRuntime(string applicationDirectory)
    {
        var runtimeDirectory = Path.Combine(applicationDirectory, "runtime");
        var nodeExecutable = Path.Combine(runtimeDirectory, "node.exe");
        var entryFile = Path.Combine(runtimeDirectory, "dsh-web-entry.js");
        if (!File.Exists(nodeExecutable))
        {
            throw new FileNotFoundException("DeepSeek Harness 附属运行时不完整：未找到 node.exe。请重新安装应用程序。", nodeExecutable);
        }

        if (!File.Exists(entryFile))
        {
            throw new FileNotFoundException("DeepSeek Harness 附属运行时不完整：未找到 dsh-web-entry.js。请重新安装应用程序。", entryFile);
        }

        var dshHome = Environment.GetEnvironmentVariable("DSH_HOME");
        if (string.IsNullOrWhiteSpace(dshHome))
        {
            dshHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarness",
                "dsh");
        }
        return new DshWebLaunch(
            nodeExecutable,
            runtimeDirectory,
            [entryFile, "web", "--port", "0"],
            new Dictionary<string, string> { ["DSH_HOME"] = dshHome });
    }
}

internal static class DshWebLaunchResolver
{
    public static DshWebLaunch Resolve()
    {
#if DEBUG
        return DshWebLaunch.ForDevelopment(RepositoryRoot.Find());
#else
        return DshWebLaunch.ForAttachedRuntime(AppContext.BaseDirectory);
#endif
    }
}
