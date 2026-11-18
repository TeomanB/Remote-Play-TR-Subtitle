using Xunit;

/// <summary>
/// The 1.4 tray icon was still the old red circle with "TR" because GDI cannot show a PNG-compressed ico.
/// The shipped app.ico must be classic BMP DIB frames, and no source file may draw that mark.
/// </summary>
public class AppIconTests
{
    [Fact]
    public void IcoFramesAreBmpAndSourceDoesNotDrawTr()
    {
        var root = RepoRoot();
        var ico = File.ReadAllBytes(Path.Combine(root, "src", "App", "app.ico"));
        Assert.True(ico.Length > 1000);
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        int count = BitConverter.ToUInt16(ico, 4);
        Assert.True(count >= 7);
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (i * 16);
            int offset = BitConverter.ToInt32(ico, entry + 12);
            Assert.NotEqual(0x89, ico[offset]);
            Assert.NotEqual((byte)'P', ico[offset + 1]);
            Assert.Equal(40, BitConverter.ToInt32(ico, offset));
        }

        foreach (var file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("DrawString(\"TR\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DrawString(\"TR\")", text, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AltyaziCevirici.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root");
    }
}
