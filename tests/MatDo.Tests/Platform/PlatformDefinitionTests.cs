using MatDo.Platform;
using MatPlatform;

namespace MatDo.Tests.Platform;

/// <summary>
/// The definition of MatDo (no database needed): valid for MatPlatform's validator in both languages against MatDo's copy of the MatUi sprite, also with a public
/// address; contract 2 with what MatDo consumes and what it adds to a home screen.
/// </summary>
public sealed class PlatformDefinitionTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void The_description_is_valid(string language)
    {
        var sprite = MatPlatformValidator.ReadSprite(Repo.Sprite);
        Assert.Empty(MatPlatformValidator.Validate(MatPlatformBuilder.Build(MatDoPlatform.Describe, language), sprite));
        Assert.Empty(MatPlatformValidator.Validate(MatPlatformBuilder.Build(o => MatDoPlatform.Describe(o, "https://todo.example.com"), language), sprite));
    }

    [Fact]
    public void MatDo_consumes_only_the_sign_in_and_contributes_its_launcher_views_and_two_admin_widgets()
    {
        var definition = MatPlatformBuilder.Build(MatDoPlatform.Describe, "en");
        Assert.Equal(MatApiDefinition.ContractVersion, definition.Contract);
        Assert.Empty(definition.Provides);
        var identity = Assert.Single(definition.Consumes);
        Assert.Equal(MatCapabilities.IdentityOidc, identity.Capability);
        Assert.True(identity.Optional);

        var contributes = definition.Contributes!;
        var launcher = Assert.Single(contributes.Launchers);
        Assert.Equal(("tasks", "MatDo", "/Today"), (launcher.Id, launcher.Label, launcher.Url));
        Assert.True(File.Exists(Path.Combine(Repo.Root, "src", "MatDo", "wwwroot", launcher.Icon.TrimStart('/'))), launcher.Icon);

        // the quick add (pwa.js opens it on ?quick=1) and the views of the menu, all pages of MatDo
        Assert.Equal(["/Today?quick=1", "/Today", "/Upcoming", "/Inbox"], contributes.QuickActions.Select(q => q.Url));
        Assert.All(contributes.QuickActions, q => Assert.Equal("tasks", q.Launcher));
        Assert.All(
            contributes.QuickActions.Select(q => q.Url!.Split('?')[0]).Distinct(),
            url => Assert.True(File.Exists(Path.Combine(Repo.Root, "src", "MatDo", "Pages", url.TrimStart('/') + ".cshtml")), url));

        // widgets only from the tiles of the overview, only for administrators (the figures of the whole installation)
        Assert.Equal(["tasks", "users"], contributes.Widgets.Select(w => w.Id));
        Assert.All(contributes.Widgets, w => Assert.Equal(MatAudience.Admin, w.Audience));
        Assert.Equal(["openTasks", "overdueTasks", "doneLast7Days"], contributes.Widgets[0].Items);
        Assert.All(contributes.Widgets.SelectMany(w => w.Items), key => Assert.Contains(definition.State, s => s.Key == key));
    }

    [Fact]
    public void MAT_PUBLIC_URL_fixes_the_public_address()
    {
        Assert.Null(MatPlatformBuilder.Build(MatDoPlatform.Describe, "en").Configure.Single(f => f.Key == "publicUrl").Fixed);
        var field = MatPlatformBuilder.Build(o => MatDoPlatform.Describe(o, "https://todo.example.com"), "en").Configure.Single(f => f.Key == "publicUrl");
        Assert.True(field.Fixed);
        Assert.Contains("MAT_PUBLIC_URL", field.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void The_app_is_the_one_of_the_registry_with_its_icon()
    {
        var definition = MatPlatformBuilder.Build(MatDoPlatform.Describe, "de");
        Assert.Equal("matdo", definition.App.Id);
        Assert.Equal("MatDo", definition.App.Name);
        Assert.Equal("#d03d33", definition.App.Accent);
        Assert.Equal("/Today", definition.App.Home);
        Assert.True(File.Exists(Path.Combine(Repo.Root, "src", "MatDo", "wwwroot", definition.App.Icon!.TrimStart('/'))), definition.App.Icon);
    }

    [Fact]
    public void Two_pages_real_tiles_few_settings_and_the_roles_of_the_app()
    {
        var definition = MatPlatformBuilder.Build(MatDoPlatform.Describe, "de");

        Assert.Equal(["overview", "settings"], definition.Pages.Select(p => p.Id));
        Assert.Equal(
            ["users", "teams", "projects", "openTasks", "overdueTasks", "doneLast7Days", "webhooks", "pushDevices", "registration", "lastActivity"],
            definition.State.Select(s => s.Key));
        Assert.Equal(["registrationOpen", "publicUrl"], definition.Configure.Select(f => f.Key));
        Assert.InRange(definition.Configure.Count, 1, 10);
        Assert.DoesNotContain(definition.Configure, f => f.Type == "secret");

        Assert.Equal(["openApp", "openUsers", "openConnections", "openSystem", "cleanUp"], definition.Actions.Select(a => a.Id));
        Assert.Equal(["/Today", "/Admin/Users", "/Admin/Connections", "/Admin/System", null], definition.Actions.Select(a => a.Url));
        var cleanUp = definition.Actions.Single(a => a.Id == "cleanUp");
        Assert.Equal("post", cleanUp.Kind);
        Assert.False(string.IsNullOrEmpty(cleanUp.Confirm));

        // UserRole: the administrator first.
        Assert.Equal(["admin", "user"], definition.Roles!.Select(r => r.Key));
    }

    [Fact]
    public void The_texts_follow_the_language()
    {
        var de = MatPlatformBuilder.Build(MatDoPlatform.Describe, "de");
        var en = MatPlatformBuilder.Build(MatDoPlatform.Describe, "en");

        Assert.Equal("Übersicht", de.Pages[0].Label);
        Assert.Equal("Overview", en.Pages[0].Label);
        Assert.Equal("Aktive Benutzer", de.State[0].Label);
        Assert.Equal("Active users", en.State[0].Label);
        Assert.Equal("Benutzer", de.Roles![1].Label);
        Assert.Equal("User", en.Roles![1].Label);
    }
}
