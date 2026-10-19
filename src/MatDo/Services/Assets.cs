using System.Text;

namespace MatDo.Services;

/// <summary>The two icon sprites (the toolkit's and the app's own), which must be in the page for <c>&lt;use href="#..."&gt;</c>.</summary>
public sealed class Assets
{
    public Assets(IWebHostEnvironment env)
    {
        Sprite = Read(env, "matui/matui-icons.svg") + Read(env, "img/app-icons.svg");
        IconNames = System.Text.RegularExpressions.Regex.Matches(Sprite, "id=\"mat-i-([a-z0-9-]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
    }

    public string Sprite { get; }

    public IReadOnlySet<string> IconNames { get; }

    private static string Read(IWebHostEnvironment env, string path)
    {
        var file = env.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
