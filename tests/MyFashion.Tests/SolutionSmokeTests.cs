namespace MyFashion.Tests;

public sealed class SolutionSmokeTests
{
    [Fact]
    public void Core_assembly_has_no_project_dependencies()
    {
        var core = typeof(MyFashion.Core.AssemblyMarker).Assembly;

        var references = core.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain(references, name => name is not null && name.StartsWith("MyFashion.", StringComparison.Ordinal));
    }
}
