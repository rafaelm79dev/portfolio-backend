using Microsoft.Extensions.Options;

namespace ApiUsers.Tests;

/// <summary>
/// Asserts that the application refuses to start because of an invalid configuration value, and why.
/// A bare "any exception" check would also pass for an unrelated failure (Docker, connection string...).
/// </summary>
internal static class StartupAssert
{
    /// <param name="start">Starts the host, for example <c>() => factory.CreateClient()</c>.</param>
    /// <param name="expectedMessagePart">Text the validation message must contain, normally the configuration key.</param>
    public static OptionsValidationException FailsOptionsValidation(Func<HttpClient> start, string expectedMessagePart)
    {
        var error = Record.Exception(start);
        Assert.True(error is not null, "Expected the application to fail at startup, but it started.");

        // CreateClient can wrap the original exception, so look through the whole chain.
        var validation = Unwrap(error).OfType<OptionsValidationException>().FirstOrDefault();
        Assert.True(validation is not null,
            $"Expected OptionsValidationException, but startup failed with: {error}");
        Assert.Contains(expectedMessagePart, validation.Message);
        return validation;
    }

    private static IEnumerable<Exception> Unwrap(Exception error)
    {
        yield return error;

        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.SelectMany(Unwrap))
            {
                yield return inner;
            }
        }
        else if (error.InnerException is not null)
        {
            foreach (var inner in Unwrap(error.InnerException))
            {
                yield return inner;
            }
        }
    }
}
