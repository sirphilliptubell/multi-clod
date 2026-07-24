namespace MultiClod.App.Activation;

/// <summary>
/// Which of the app's launch-time activation paths a request came from - see
/// <see cref="ActivationRequestQueue"/>.
/// </summary>
public enum ActivationRequestKind
{
    FromHere,
    Deeplink,
}

/// <summary>
/// A single activation request carried through <see cref="ActivationRequestQueue"/>: either a
/// "from-here" working directory (Kind == FromHere, Payload is the directory) or a deeplink source
/// (Kind == Deeplink, Payload is the raw url/path parsed from a multi-clod:// launch). A null
/// ActivationRequest? (not a case of this type) still means "just come to foreground" - see
/// ActivationRequestQueue's own remarks.
/// </summary>
public readonly record struct ActivationRequest(ActivationRequestKind Kind, string Payload)
{
    public static ActivationRequest FromHere(string directory) => new(ActivationRequestKind.FromHere, directory);

    public static ActivationRequest Deeplink(string source) => new(ActivationRequestKind.Deeplink, source);
}
