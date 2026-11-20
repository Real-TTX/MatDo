using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatDo.Pages.Teams;

/// <summary>One row of the cleaning plan, for the partial: the team, the slot, and the week to come back to after checking it off.</summary>
public sealed record PutzplanRow(long TeamId, PutzplanEntry Entry, string? From);

/// <summary>
/// The cleaning plan ("Putzplan") of a team: one week, person by person, of the repeating tasks that rotate — who is on each this week, a check-off for the one that
/// is due now, and what was done already (who did it). The weeks ahead are a preview of the rotation, the weeks before show what was done.
/// </summary>
public sealed class PutzplanModel(TeamService teams, TaskService tasks) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    public Team Team { get; private set; } = null!;

    public DateOnly WeekStart { get; private set; }

    public DateOnly WeekEnd { get; private set; }

    public bool AnyPlan { get; private set; }

    public bool IsThisWeek => WeekStart == MondayOf(Today);

    public long MeId => UserId;

    public List<MemberInfo> Members { get; private set; } = [];

    public List<PutzplanEntry> Entries { get; private set; } = [];

    /// <summary>The entries of a member, earliest first.</summary>
    public List<PutzplanEntry> EntriesOf(long userId) => Entries.Where(e => e.AssigneeId == userId).ToList();

    /// <summary>Entries whose turn-holder is not (or no longer) a member of the team.</summary>
    public List<PutzplanEntry> Unassigned => Entries.Where(e => e.AssigneeId is not { } a || Members.All(m => m.UserId != a)).ToList();

    public string WeekUrl(DateOnly monday) => monday == MondayOf(Today) ? $"/Teams/Putzplan?id={Id}" : $"/Teams/Putzplan?id={Id}&from={Dates.Iso(monday)}";

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public async Task<IActionResult> OnGetAsync(string? from) => await LoadAsync(from) ? Page() : NotFound();

    public async Task<IActionResult> OnPostCompleteAsync(long taskId, string? from)
    {
        if (!await LoadAsync(from))
        {
            return NotFound();
        }

        var (_, next) = await tasks.CompleteAsync(UserId, taskId, Today);
        Flash(next is { } n ? Text.T("Tasks.NextDue", Dates.Relative(n, Today)) : Text.T("Common.Saved"));
        return LocalRedirect(WeekUrl(WeekStart));
    }

    private async Task<bool> LoadAsync(string? from)
    {
        if (await teams.FindAsync(UserId, Id) is not { } found)
        {
            return false;
        }

        Team = found.Team;
        var thisMonday = MondayOf(Today);
        WeekStart = Dates.ParseIso(from) is { } f ? MondayOf(f) : thisMonday;
        WeekEnd = WeekStart.AddDays(6);
        Members = await teams.MembersAsync(Id);
        Entries = await tasks.PutzplanAsync(UserId, Id, WeekStart, WeekEnd);
        AnyPlan = await tasks.HasPutzplanAsync(UserId, Id);
        return true;
    }
}
