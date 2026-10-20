using MatDo.Api;
using MatDo.Data;
using MatDo.Services.Notifications;

namespace MatDo.Tests;

public sealed class NotificationTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc:def")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAA")]
    [InlineData("https://web.push.apple.com/QGx2")]
    [InlineData("https://wns2-db5p.notify.windows.com/w/?token=BQYAAAB")]
    public void The_push_services_of_the_browsers_are_accepted(string endpoint) => Assert.True(PushService.IsPushService(endpoint));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")] // not https
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")] // another port
    [InlineData("https://localhost/push")]
    [InlineData("https://10.0.0.5/push")]
    [InlineData("https://evil.example.com/fcm.googleapis.com")]
    [InlineData("https://fcm.googleapis.com.evil.example.com/x")]
    [InlineData("https://notpush.apple.com.example.org/x")]
    public void Any_other_address_is_refused_so_the_server_never_posts_where_a_user_points_it(string? endpoint) => Assert.False(PushService.IsPushService(endpoint));

    [Fact]
    public void A_floating_time_is_the_moment_in_the_zone_of_the_person()
    {
        // 09:00 in Berlin is 08:00 UTC in winter and 07:00 UTC in summer
        Assert.Equal(new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc), NotificationService.Moment(new DateOnly(2026, 1, 15), new TimeOnly(9, 0), "Europe/Berlin"));
        Assert.Equal(new DateTime(2026, 7, 15, 7, 0, 0, DateTimeKind.Utc), NotificationService.Moment(new DateOnly(2026, 7, 15), new TimeOnly(9, 0), "Europe/Berlin"));
        Assert.Equal(new DateTime(2026, 7, 15, 13, 0, 0, DateTimeKind.Utc), NotificationService.Moment(new DateOnly(2026, 7, 15), new TimeOnly(9, 0), "America/New_York"));
    }

    [Fact]
    public void A_time_the_clock_skips_is_taken_an_hour_later()
    {
        // 29 March 2026, 02:30 does not exist in Berlin (02:00 → 03:00): 03:30 CEST = 01:30 UTC
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), NotificationService.Moment(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), "Europe/Berlin"));
    }

    [Fact]
    public void The_person_reminded_is_the_assignee_else_the_owner_of_a_personal_project_else_who_created_the_team_task()
    {
        var personal = new Project { OwnerId = 1 };
        var team = new Project { OwnerId = 1, TeamId = 9 };
        Assert.Equal(5, NotificationService.Recipient(new TaskItem { AssigneeId = 5, Project = team, CreateUserId = 2 }));
        Assert.Equal(1, NotificationService.Recipient(new TaskItem { Project = personal, CreateUserId = 2 }));
        Assert.Equal(2, NotificationService.Recipient(new TaskItem { Project = team, CreateUserId = 2 }));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "iPhone · Safari")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36", "Android · Chrome")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0", "Windows · Edge")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.5; rv:131.0) Gecko/20100101 Firefox/131.0", "Mac · Firefox")]
    public void A_device_is_named_by_its_system_and_browser(string agent, string name) => Assert.Equal(name, PushApi.DeviceName(agent));
}
