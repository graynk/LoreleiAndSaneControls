using LoreleiAndSaneControls.Patcher;
using Xunit;

namespace ControlSchemePatcher.Tests;

public sealed class GamePathsTests
{
    [Fact]
    public void FindsWindowsAndLinuxDataDirectoryFromGameRoot()
    {
        using var directory = TemporaryDirectory.Create();
        var dataDirectory = Path.Combine(directory.Path, "Lorelei and the Laser Eyes_Data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllBytes(Path.Combine(dataDirectory, "globalgamemanagers.assets"), []);

        var paths = Program.FindGameFiles(directory.Path);

        Assert.Equal(dataDirectory, paths.DataDirectory);
        Assert.Equal(Path.Combine(dataDirectory, "Managed"), paths.ManagedDirectory);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"lorelei-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
