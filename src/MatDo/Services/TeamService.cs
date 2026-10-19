using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

public sealed record TeamInfo(long Id, string Name, string? Description, string Color, TeamRole Role, int Members, int Projects);

public sealed record MemberInfo(long Id, long UserId, string DisplayName, string Email, TeamRole Role);

/// <summary>
/// Teams: a group of people that share projects. Whoever creates a team is its owner; an owner adds people by the address of their account, removes them, and
/// deletes the team. A member sees and changes every project of the team, and can leave. A team keeps at least one owner.
/// </summary>
public sealed class TeamService(AppDbContext db)
{
    public async Task<List<TeamInfo>> ListAsync(long userId)
    {
        var rows = await db.TeamMembers.Where(m => m.UserId == userId && m.Team != null)
            .Select(m => new
            {
                m.Team!.Id, m.Team.Name, m.Team.Description, m.Team.Color, m.Role,
                Members = db.TeamMembers.Count(x => x.TeamId == m.TeamId),
                Projects = db.Projects.Count(p => p.TeamId == m.TeamId),
            })
            .AsNoTracking().ToListAsync();
        return rows.Select(r => new TeamInfo(r.Id, r.Name, r.Description, r.Color, r.Role, r.Members, r.Projects))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The team, when the person is a member (else null: a team of others answers 404).</summary>
    public async Task<(Team Team, TeamRole Role)?> FindAsync(long userId, long id)
    {
        var member = await db.TeamMembers.Include(m => m.Team).FirstOrDefaultAsync(m => m.TeamId == id && m.UserId == userId && m.Team != null);
        return member is null ? null : (member.Team!, member.Role);
    }

    public async Task<List<MemberInfo>> MembersAsync(long teamId)
    {
        var rows = await db.TeamMembers.Where(m => m.TeamId == teamId && m.User != null)
            .Select(m => new { m.Id, m.UserId, m.User!.DisplayName, m.User.Email, m.Role }).AsNoTracking().ToListAsync();
        return rows.Select(r => new MemberInfo(r.Id, r.UserId, r.DisplayName, r.Email, r.Role))
            .OrderBy(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Team> CreateAsync(long userId, string name, string? description, string color)
    {
        var team = new Team { Name = name.Trim(), Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(), Color = color };
        team.Members.Add(new TeamMember { UserId = userId, Role = TeamRole.Owner });
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return team;
    }

    /// <summary>Adds the person with this address. The answer is the same for an address without an account and one that is in the team already, so nobody learns who has an account.</summary>
    public async Task<string?> AddMemberAsync(long teamId, string email)
    {
        email = AccountService.Normalize(email);
        if (!AccountService.IsEmail(email))
        {
            return "Bitte eine gültige E-Mail-Adresse eingeben.";
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user is null || await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == user.Id))
        {
            return "Diese Person kann nicht hinzugefügt werden. Sie braucht ein MatDo-Konto mit genau dieser Adresse und darf noch nicht Mitglied sein.";
        }

        db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = user.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SetRoleAsync(long teamId, long memberId, TeamRole role)
    {
        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId);
        if (member is null)
        {
            return "Dieses Mitglied gibt es nicht.";
        }

        if (member.Role == TeamRole.Owner && role != TeamRole.Owner && await db.TeamMembers.CountAsync(m => m.TeamId == teamId && m.Role == TeamRole.Owner) == 1)
        {
            return "Ein Team braucht mindestens einen Eigentümer.";
        }

        member.Role = role;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Removes a member (or the person themself: leaving). Their tasks stay; tasks given to them lose the assignee.</summary>
    public async Task<string?> RemoveMemberAsync(long teamId, long memberId)
    {
        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId);
        if (member is null)
        {
            return "Dieses Mitglied gibt es nicht.";
        }

        if (member.Role == TeamRole.Owner && await db.TeamMembers.CountAsync(m => m.TeamId == teamId && m.Role == TeamRole.Owner) == 1)
        {
            return "Der letzte Eigentümer kann das Team nicht verlassen. Bitte zuerst jemand anderen zum Eigentümer machen oder das Team löschen.";
        }

        var projectIds = db.Projects.Where(p => p.TeamId == teamId).Select(p => p.Id);
        await db.Tasks.Where(t => projectIds.Contains(t.ProjectId) && t.AssigneeId == member.UserId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (long?)null));
        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Deletes the team. Its projects are not lost: they become personal projects of the owner who deletes it.</summary>
    public async Task DeleteAsync(long userId, Team team)
    {
        var projects = await db.Projects.Where(p => p.TeamId == team.Id).ToListAsync();
        foreach (var p in projects)
        {
            p.TeamId = null;
            p.OwnerId = userId;
        }

        await db.Tasks.Where(t => projects.Select(p => p.Id).Contains(t.ProjectId)).ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (long?)null));
        db.TeamMembers.RemoveRange(await db.TeamMembers.Where(m => m.TeamId == team.Id).ToListAsync());
        db.Teams.Remove(team);
        await db.SaveChangesAsync();
    }
}
