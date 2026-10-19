using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Sections;

/// <summary>Rename, move and delete a section. Deleting it keeps its tasks: they move to the part of the project without a section.</summary>
public sealed class EditModel(AppDbContext db, ProjectService projects) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public int SortOrder { get; set; }

    public Project Project { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is not { } section)
        {
            return NotFound();
        }

        Name = section.Name;
        SortOrder = section.SortOrder;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await LoadAsync() is not { } section)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            ModelState.AddModelError(nameof(Name), Text.T("Common.NameRequired"));
            return Page();
        }

        section.Name = Name.Trim().Length > 120 ? Name.Trim()[..120] : Name.Trim();
        section.SortOrder = Math.Clamp(SortOrder, 0, 10000);
        await db.SaveChangesAsync();
        Flash(Text.T("Common.Saved"));
        return LocalRedirect($"/Projects/View?id={section.ProjectId}");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (await LoadAsync() is not { } section)
        {
            return NotFound();
        }

        await db.Tasks.Where(t => t.SectionId == section.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.SectionId, (long?)null));
        db.Sections.Remove(section);
        await db.SaveChangesAsync();
        Flash(Text.T("Sections.Deleted", section.Name));
        return LocalRedirect($"/Projects/View?id={section.ProjectId}");
    }

    private async Task<Section?> LoadAsync()
    {
        var ids = projects.VisibleIds(UserId);
        var section = await db.Sections.Include(s => s.Project).FirstOrDefaultAsync(s => s.Id == Id && ids.Contains(s.ProjectId));
        if (section is not null)
        {
            Project = section.Project!;
            ViewData["ActiveProject"] = section.ProjectId;
        }

        return section;
    }
}
