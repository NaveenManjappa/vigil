using Vigil.Domain.Datasets;
using Vigil.Simulator.Personas;

namespace Vigil.Simulator;

/// <summary>
/// <c>generate [--seed 42] [--holders 1000] [--out datasets]</c>: writes a labelled Dataset to a file.
/// It never contacts the API, so Labels cannot leak into detection.
/// </summary>
public static class GenerateCommand
{
    public const string Name = "generate";

    public static string Run(string[] args)
    {
        var seed = int.Parse(Option(args, "--seed") ?? "42");
        var holders = int.Parse(Option(args, "--holders") ?? "1000");
        var folder = Option(args, "--out") ?? "datasets";

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"seed-{seed}-holders-{holders}.jsonl");
        using var file = File.Create(path);
        Dataset.Write(DatasetGenerator.Generate(seed, holders), file);
        return path;
    }

    /// <summary>The value after <paramref name="name"/>, or null when it is absent; shared with evaluation's commands.</summary>
    public static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0) return null;
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
        return args[i + 1];
    }
}
