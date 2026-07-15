namespace GoldenHour.Api.Application;

public sealed class PromptTemplateStore(IWebHostEnvironment environment)
{
    public string Read(string versionedName)
    {
        if (versionedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || versionedName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid prompt resource name.", nameof(versionedName));
        }

        var path = Path.Combine(environment.ContentRootPath, "Prompts", versionedName);
        return File.ReadAllText(path);
    }
}
