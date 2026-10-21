using MatDo.Platform;
using MatPlatform;

namespace MatDo.Tests.Platform;

/// <summary>The definition of MatDo (no database needed): valid for MatPlatform's validator in both languages against MatDo's copy of the MatUi sprite.</summary>
public sealed class PlatformDefinitionTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void The_description_is_valid(string language)
        => Assert.Empty(MatPlatformValidator.Validate(MatPlatformBuilder.Build(MatDoPlatform.Describe, language), MatPlatformValidator.ReadSprite(Repo.Sprite)));

    [Fact]
    public void The_app_is_the_one_of_the_registry_with_its_icon()
    {
        var definition = MatPlatformBuilder.Build(MatDoPlatform.Describe, "de");
        Assert.Equal("matdo", definition.App.Id);
        Assert.Equal("MatDo", definition.App.Name);
        Assert.Equal("#8300bc", definition.App.Accent);
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

        Assert.Equal(["openApp", "openUsers", "openSystem", "cleanUp"], definition.Actions.Select(a => a.Id));
        Assert.Equal(["/Today", "/Admin/Users", "/Admin/System", null], definition.Actions.Select(a => a.Url));
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
