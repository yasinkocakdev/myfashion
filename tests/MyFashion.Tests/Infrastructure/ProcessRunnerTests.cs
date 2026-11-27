using System.Text;
using MyFashion.Infrastructure.Processes;

namespace MyFashion.Tests.Infrastructure;

public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task Arguments_are_passed_literally_without_a_shell()
    {
        var result = await _runner.RunAsync(
            "/bin/echo",
            ["a; rm -rf ~", "$(whoami)", "`id`", "x y"],
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("a; rm -rf ~ $(whoami) `id` x y\n", Encoding.UTF8.GetString(result.StandardOutput));
    }

    [Fact]
    public async Task Non_zero_exit_code_and_stderr_are_reported()
    {
        var result = await _runner.RunAsync(
            "/bin/ls",
            ["/definitely/not/here"],
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task Timeout_kills_the_process()
    {
        var started = DateTime.UtcNow;

        await Assert.ThrowsAsync<TimeoutException>(() => _runner.RunAsync(
            "/bin/sleep",
            ["30"],
            TimeSpan.FromMilliseconds(300),
            TestContext.Current.CancellationToken));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cancellation_kills_the_process()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(
            "/bin/sleep",
            ["30"],
            TimeSpan.FromSeconds(60),
            cts.Token));
    }

    [Fact]
    public async Task Missing_executable_gives_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<ExternalToolException>(() => _runner.RunAsync(
            "/no/such/tool",
            [],
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));

        Assert.Contains("/no/such/tool", ex.Message, StringComparison.Ordinal);
    }
}
