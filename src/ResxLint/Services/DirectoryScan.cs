namespace ResxLint.Services;

/// <summary>
/// Recursive file scanning shared by the CLI lint engine and the web project-discovery
/// endpoints. Prunes build/vcs/dependency folders during the walk (instead of after,
/// which still pays the cost of descending into them) and silently skips directories it
/// can't access — Windows user profiles are full of junctions like "Ambiente de Impressão" /
/// "Printer Shortcuts" that throw UnauthorizedAccessException on enumeration.
/// </summary>
static class DirectoryScan
{
    static readonly string[] PrunedDirNames = ["bin", "obj", "node_modules", ".git", ".vs", "packages"];

    public static List<string> EnumerateFilesPruned(string root, string searchPattern)
    {
        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            try
            {
                results.AddRange(Directory.EnumerateFiles(dir, searchPattern, SearchOption.TopDirectoryOnly));

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (PrunedDirNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    stack.Push(sub);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }

        return results;
    }
}
