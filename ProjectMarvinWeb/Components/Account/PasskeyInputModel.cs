namespace ProjectMarvin.Components.Account
{
  /// <summary>What the browser sends back from a passkey prompt (see Shared/PasskeySubmit).</summary>
  public class PasskeyInputModel
  {
    public string? CredentialJson { get; set; }
    public string? Error { get; set; }
  }
}
