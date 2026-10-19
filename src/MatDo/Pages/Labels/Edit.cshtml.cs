using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Labels;

/// <summary>Create, rename and recolour a label; deleting it takes it off every task (the tasks stay).</summary>
public sealed class EditModel(AppDbContext db) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public string Color { get; set; } = "#808080";

    [BindProperty] public bool IsFavorite { get; set; }

    public bool IsNew => Id is null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == UserId);
            if (label is null)
            {
                return NotFound();
            }

            Name = label.Name;
            Color = label.Color;
            IsFavorite = label.IsFavorite;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Label? label = null;
        if (Id is { } id)
        {
            label = await db.Labels.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == UserId);
            if (label is null)
            {
                return NotFound();
            }
        }

        // a label is one word in the quick add (@name): spaces become hyphens
        var name = string.Join('-', Name.Trim().TrimStart('@').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), "Bitte einen Namen eingeben.");
            return Page();
        }

        var lower = name.ToLower();
        if (await db.Labels.AnyAsync(l => l.OwnerId == UserId && l.Name.ToLower() == lower && l.Id != (Id ?? 0)))
        {
            ModelState.AddModelError(nameof(Name), "Ein Label mit diesem Namen gibt es schon.");
            return Page();
        }

        if (label is null)
        {
            label = new Label { OwnerId = UserId };
            db.Labels.Add(label);
        }

        label.Name = name.Length > 60 ? name[..60] : name;
        label.Color = Ui.IsColour(Color) ? Color.ToLowerInvariant() : "#808080";
        label.IsFavorite = IsFavorite;
        await db.SaveChangesAsync();
        Flash(IsNew ? $"Label „{label.Name}“ angelegt." : "Gespeichert.");
        return LocalRedirect("/Labels");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == Id && l.OwnerId == UserId);
        if (label is null)
        {
            return NotFound();
        }

        await db.TaskLabels.Where(x => x.LabelId == label.Id).ExecuteDeleteAsync();
        db.Labels.Remove(label);
        await db.SaveChangesAsync();
        Flash($"Label „{label.Name}“ gelöscht.");
        return LocalRedirect("/Labels");
    }
}
