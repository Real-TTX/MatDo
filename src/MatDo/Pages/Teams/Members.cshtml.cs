using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatDo.Pages.Teams;

/// <summary>The members of a team: an owner adds people by their address, changes roles and removes members; every member sees the list and can leave.</summary>
public sealed class MembersModel(TeamService teams, ProjectService projects) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    [BindProperty] public string Email { get; set; } = string.Empty;

    public Team Team { get; private set; } = null!;

    public TeamRole MyRole { get; private set; }

    public List<MemberInfo> Members { get; private set; } = [];

    public List<ProjectInfo> Projects { get; private set; } = [];

    public bool IsOwner => MyRole == TeamRole.Owner;

    public long MeId => UserId;

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (!await LoadAsync() || !IsOwner)
        {
            return NotFound();
        }

        var error = await teams.AddMemberAsync(Id, Email);
        if (error is not null)
        {
            ModelState.AddModelError(nameof(Email), error);
            return Page();
        }

        Flash(Text.T("Teams.Members.Added", Email.Trim()));
        return LocalRedirect($"/Teams/Members?id={Id}");
    }

    public async Task<IActionResult> OnPostRoleAsync(long memberId, TeamRole role)
    {
        if (!await LoadAsync() || !IsOwner)
        {
            return NotFound();
        }

        var error = await teams.SetRoleAsync(Id, memberId, role);
        if (error is null)
        {
            Flash(Text.T("Teams.Members.RoleChanged"));
        }
        else
        {
            FlashError(error);
        }

        return LocalRedirect($"/Teams/Members?id={Id}");
    }

    public async Task<IActionResult> OnPostRemoveAsync(long memberId)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var self = Members.FirstOrDefault(m => m.Id == memberId)?.UserId == UserId;
        if (!IsOwner && !self)
        {
            return NotFound();
        }

        var error = await teams.RemoveMemberAsync(Id, memberId);
        if (error is not null)
        {
            FlashError(error);
            return LocalRedirect($"/Teams/Members?id={Id}");
        }

        Flash(self ? Text.T("Teams.Members.Left", Team.Name) : Text.T("Teams.Members.Removed"));
        return LocalRedirect(self ? "/Teams" : $"/Teams/Members?id={Id}");
    }

    private async Task<bool> LoadAsync()
    {
        if (await teams.FindAsync(UserId, Id) is not { } found)
        {
            return false;
        }

        (Team, MyRole) = found;
        Members = await teams.MembersAsync(Id);
        Projects = (await projects.ListAsync(UserId)).Where(p => p.TeamId == Id).ToList();
        return true;
    }
}
