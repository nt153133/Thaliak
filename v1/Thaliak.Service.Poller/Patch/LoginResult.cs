namespace Thaliak.Service.Poller.Patch;

public class LoginResult
{
    public LoginState State { get; set; }
    public PatchListEntry[] PendingPatches { get; set; } = [];
    public OauthLoginResult OauthLogin { get; set; } = new();
    public string? UniqueId { get; set; }
}
