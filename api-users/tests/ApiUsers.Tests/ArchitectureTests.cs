using ApiUsers.Application.Auth;
using ApiUsers.Domain;
using ApiUsers.Infrastructure.Data;

namespace ApiUsers.Tests;

public class ArchitectureTests
{
    private static IEnumerable<string> ReferencedNames(Type typeInAssembly) =>
        typeInAssembly.Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    [Fact]
    public void Domain_has_no_project_dependencies()
    {
        var references = ReferencedNames(typeof(Roles));

        Assert.DoesNotContain(references, n => n.StartsWith("ApiUsers."));
    }

    [Fact]
    public void Application_depends_only_on_domain()
    {
        var references = ReferencedNames(typeof(AuthService));

        // Besides the framework, Application may only reference Domain.
        Assert.All(references.Where(n => n.StartsWith("ApiUsers.")), n => Assert.Equal("ApiUsers.Domain", n));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var references = ReferencedNames(typeof(AppDbContext));

        Assert.DoesNotContain(references, n => n.StartsWith("ApiUsers.Api"));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_migrations()
    {
        var references = ReferencedNames(typeof(AppDbContext));

        Assert.DoesNotContain(references, n => n.StartsWith("ApiUsers.Migrations"));
    }
}
