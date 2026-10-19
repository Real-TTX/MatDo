using System.Net;
using System.Text;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatDo.Controls;

/// <summary>
/// The one code base of the buttons under a create or edit page, from positive to negative: <c>Save</c>, <c>Back</c>, a space, <c>Delete</c> (FORM-01, LIST-08).
/// <c>&lt;md-buttons save="Save note" back="/Notes" delete-handler="Delete" delete-confirm="Delete this note?" /&gt;</c>. A button without its attribute is left out.
/// Delete is a submit button of the same form that skips the validation (<c>formnovalidate</c>) and asks first (<c>data-confirm</c>); its address keeps the query of the
/// page (<c>?id=5&amp;handler=Delete</c>): the id of the item is in the query, and a bare <c>?handler=</c> would drop it.
/// Other buttons of the row (Complete, Share ...) are the content of the element: they stand between Back and the space before Delete.
/// On a phone every button but the first shows its icon only (<c>md-btn-collapse</c>, the label stays for screen readers and as the tooltip), so the row fits on one line.
/// </summary>
[HtmlTargetElement("md-buttons")]
public sealed class ButtonRowTagHelper : TagHelper
{
    [HtmlAttributeName("save")] public string? Save { get; set; }

    [HtmlAttributeName("save-icon")] public string SaveIcon { get; set; } = "save";

    [HtmlAttributeName("back")] public string? Back { get; set; }

    [HtmlAttributeName("back-label")] public string BackLabel { get; set; } = Localization.Text.T("Common.Back");

    [HtmlAttributeName("delete-handler")] public string? DeleteHandler { get; set; }

    [HtmlAttributeName("delete-label")] public string DeleteLabel { get; set; } = Localization.Text.T("Common.Delete");

    [HtmlAttributeName("delete-confirm")] public string? DeleteConfirm { get; set; }

    [HtmlAttributeName("delete-ok")] public string? DeleteOk { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>The address of a handler of this page with the query the page has (without a handler of its own).</summary>
    public static string HandlerUrl(HttpRequest request, string handler)
    {
        var pairs = request.Query
            .Where(q => !string.Equals(q.Key, "handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v ?? string.Empty)}"))
            .Append($"handler={Uri.EscapeDataString(handler)}");
        return request.Path + "?" + string.Join('&', pairs);
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var extra = (await output.GetChildContentAsync()).GetContent();
        output.TagName = null;
        var sb = new StringBuilder("<div class=\"mat-btn-row md-btn-row\">");
        if (Save is not null)
        {
            sb.Append($"<button type=\"submit\" class=\"mat-btn mat-btn--primary\">{Ui.Icon(SaveIcon)}<span>{E(Save)}</span></button>");
        }

        if (Back is not null)
        {
            sb.Append($"<a class=\"mat-btn mat-btn--secondary md-btn-collapse\" href=\"{E(Back)}\" aria-label=\"{E(BackLabel)}\" data-tip=\"{E(BackLabel)}\">{Ui.Icon("back")}<span>{E(BackLabel)}</span></a>");
        }

        sb.Append(extra);
        if (!string.IsNullOrEmpty(DeleteHandler))
        {
            var action = HandlerUrl(ViewContext.HttpContext.Request, DeleteHandler);
            sb.Append("<span class=\"mat-btn-row__spacer\"></span>");
            sb.Append($"<button type=\"submit\" class=\"mat-btn mat-btn--danger md-btn-collapse\" formnovalidate formaction=\"{E(action)}\" aria-label=\"{E(DeleteLabel)}\" data-tip=\"{E(DeleteLabel)}\" data-confirm=\"{E(DeleteConfirm ?? Localization.Text.T("Common.DeleteConfirm"))}\" data-confirm-ok=\"{E(DeleteOk ?? Localization.Text.T("Common.Delete"))}\">{Ui.Icon("trash")}<span>{E(DeleteLabel)}</span></button>");
        }

        sb.Append("</div>");
        output.Content.SetHtmlContent(sb.ToString());
    }
}
