using MatDo.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatDo.Pages;

/// <summary>The inbox is the project page of the person's inbox.</summary>
public sealed class InboxModel(ProjectService projects) : AppPageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var inbox = await projects.InboxAsync(UserId);
        return LocalRedirect($"/Projects/View?id={inbox.Id}{(Request.QueryString.HasValue ? "&" + Request.QueryString.Value![1..] : string.Empty)}");
    }
}
