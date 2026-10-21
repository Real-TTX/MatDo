using MatDo.Localization;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatDo.Pages.Admin;

/// <summary>The settings of the installation (registration, public address) and what is in it; the same values as the platform API (<see cref="InstanceService"/>).</summary>
public sealed class SystemModel(InstanceService instance) : AppPageModel
{
    [BindProperty] public bool RegistrationOpen { get; set; }

    [BindProperty] public string PublicUrl { get; set; } = string.Empty;

    public InstanceSnapshot Counts { get; private set; } = null!;

    public string Database { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var (open, url) = await instance.SettingsAsync();
        RegistrationOpen = open;
        PublicUrl = url ?? string.Empty;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await instance.SaveSettingsAsync(RegistrationOpen, PublicUrl) is { } problem)
        {
            ModelState.AddModelError(nameof(PublicUrl), problem);
            await LoadAsync();
            return Page();
        }

        Flash(Text.T("Common.Saved"));
        return LocalRedirect("/Admin/System");
    }

    private async Task LoadAsync()
    {
        Counts = await instance.SnapshotAsync();
        Database = instance.DatabaseName();
    }
}
