using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatDo.Pages.Teams;

/// <summary>Create a team (the person becomes its owner), change it, delete it (its projects become personal projects of the owner who deletes it).</summary>
public sealed class EditModel(AppDbContext db, TeamService teams) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public string Description { get; set; } = string.Empty;

    [BindProperty] public string Color { get; set; } = Ui.Accent;

    public bool IsNew => Id is null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            if (await teams.FindAsync(UserId, id) is not { Role: TeamRole.Owner } found)
            {
                return NotFound();
            }

            Name = found.Team.Name;
            Description = found.Team.Description ?? string.Empty;
            Color = found.Team.Color;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), Text.T("Common.NameRequired"));
            return Page();
        }

        var color = Ui.IsColour(Color) ? Color.ToLowerInvariant() : Ui.Accent;
        if (Id is { } id)
        {
            if (await teams.FindAsync(UserId, id) is not { Role: TeamRole.Owner } found)
            {
                return NotFound();
            }

            found.Team.Name = name.Length > 120 ? name[..120] : name;
            found.Team.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            found.Team.Color = color;
            await db.SaveChangesAsync();
            Flash(Text.T("Common.Saved"));
            return LocalRedirect($"/Teams/Members?id={id}");
        }

        var team = await teams.CreateAsync(UserId, name.Length > 120 ? name[..120] : name, Description, color);
        Flash(Text.T("Teams.Created", team.Name));
        return LocalRedirect($"/Teams/Members?id={team.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is not { } id || await teams.FindAsync(UserId, id) is not { Role: TeamRole.Owner } found)
        {
            return NotFound();
        }

        await teams.DeleteAsync(UserId, found.Team);
        Flash(Text.T("Teams.Deleted", found.Team.Name));
        return LocalRedirect("/Teams");
    }
}
