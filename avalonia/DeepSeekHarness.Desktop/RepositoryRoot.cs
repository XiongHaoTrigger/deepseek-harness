namespace DeepSeekHarness.Desktop;

public static class RepositoryRoot
{
    public static string Find()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "pnpm-workspace.yaml")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("请从 DeepSeek Harness 仓库根目录启动桌面程序。");
    }
}
