namespace MatDo.Pages.Admin;

/// <summary>
/// The connections of MatDo to other apps (the Mat app model): the Connections area of every app (<c>&lt;mat-connections /&gt;</c> of MatPlatform: what MatDo uses,
/// how each was set, the form for the values by hand, what it adds to a home screen). MatDo uses only the sign-in of MatIdentity, which its variables set; a broker
/// such as Mattiverse sets them by itself. Like every page under /Admin it is for administrators (the policy AdminOnly), and so is its form
/// (<c>MatPlatformOptions.ConnectionsAccess</c> in Program.cs).
/// </summary>
public sealed class ConnectionsModel : AppPageModel
{
}
