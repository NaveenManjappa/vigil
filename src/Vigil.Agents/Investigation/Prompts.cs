namespace Vigil.Agents;

/// <summary>
/// The agents' instructions, from the versioned files in <c>config/prompts</c>. They are embedded in this assembly, so a
/// prompt cannot go missing at runtime or drift from the Prompt Version recorded with it.
/// </summary>
public static class Prompts
{
    /// <param name="promptVersion">The file name without extension, e.g. <c>investigator.v1</c>.</param>
    public static string Load(string promptVersion)
    {
        using var stream = typeof(Prompts).Assembly.GetManifestResourceStream($"prompts.{promptVersion}.md")
            ?? throw new InvalidOperationException($"No prompt file config/prompts/{promptVersion}.md is embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
