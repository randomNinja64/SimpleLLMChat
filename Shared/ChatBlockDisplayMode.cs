/// <summary>
/// How thinking, tool-call, and tool-output blocks appear in the chat UI.
/// </summary>
public enum ChatBlockDisplayMode
{
    /// <summary>Full content in an expanded Expander (or full plain stub where applicable).</summary>
    Shown,

    /// <summary>Full content in a collapsed Expander by default.</summary>
    Collapsed,

    /// <summary>
    /// Short residual stub only: thought-for-N, name-only tool call,
    /// or tool-output exit code — not a full blank omission.
    /// </summary>
    Hidden
}
