using System.Globalization;
using MatDo.Controls;
using MatDo.Data;
using MatDo.Localization;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account;

/// <summary>A device of the person that receives notifications, for the list on the page.</summary>
public sealed record DeviceRow(long Id, string Name, DateTime Added, DateTime? LastSent);

/// <summary>
/// The notifications: this device on or off (push.js asks the browser and subscribes), when a task with a time reminds, the daily overview, and the
/// devices that receive them. The messages are sent by <see cref="Services.Notifications.NotificationService"/>.
/// </summary>
public sealed class NotificationsModel(AppDbContext db) : AppPageModel
{
    /// <summary>"off" or the minutes before the time of a task.</summary>
    [BindProperty] public string Reminder { get; set; } = "0";

    [BindProperty] public bool Digest { get; set; }

    [BindProperty] public string? DigestTime { get; set; }

    public List<DeviceRow> Devices { get; private set; } = [];

    public string Zone => Me.TimeZone;

    public List<Choice> ReminderChoices { get; } =
    [
        new("off", Text.T("Notifications.Reminder.Off")), new("0", Text.T("Notifications.Reminder.AtTime")),
        new("10", Text.T("Notifications.Reminder.Before", 10)), new("30", Text.T("Notifications.Reminder.Before", 30)),
        new("60", Text.T("Notifications.Reminder.HourBefore")),
    ];

    public async Task OnGetAsync()
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
        Reminder = user.ReminderMinutes is { } m ? m.ToString(CultureInfo.InvariantCulture) : "off";
        Digest = user.DigestTime is not null;
        DigestTime = Dates.Time(user.DigestTime ?? new TimeOnly(8, 0));
        await LoadDevicesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var time = Dates.ParseTime(DigestTime);
        if (Digest && time is null)
        {
            ModelState.AddModelError(nameof(DigestTime), Text.T("Notifications.Digest.TimeRequired"));
        }

        if (!ModelState.IsValid)
        {
            await LoadDevicesAsync();
            return Page();
        }

        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        user.ReminderMinutes = Reminder is "0" or "10" or "30" or "60" ? int.Parse(Reminder, CultureInfo.InvariantCulture) : null;
        var digest = Digest ? time : null;
        if (digest != user.DigestTime)
        {
            user.LastDigestDate = null; // a new time: today's overview may still come
        }

        user.DigestTime = digest;
        await db.SaveChangesAsync();
        Flash(Text.T("Common.Saved"));
        return LocalRedirect("/Account/Notifications");
    }

    /// <summary>A device no longer receives anything (the browser keeps its permission; switching on there again subscribes anew).</summary>
    public async Task<IActionResult> OnPostRemoveAsync(long id)
    {
        await db.PushDevices.Where(d => d.Id == id && d.UserId == UserId).ExecuteDeleteAsync();
        Flash(Text.T("Notifications.Device.Removed"));
        return LocalRedirect("/Account/Notifications");
    }

    private async Task LoadDevicesAsync()
        => Devices = await db.PushDevices.Where(d => d.UserId == UserId).OrderByDescending(d => d.CreateDate)
            .Select(d => new DeviceRow(d.Id, d.Name, d.CreateDate, d.LastSentDate)).AsNoTracking().ToListAsync();
}
