using System.Diagnostics;

namespace JMobile.Tests;

/// <summary>
/// A minimal test framework.
/// </summary>
/// <remarks>
/// The whole product has no external package dependencies, and the test suite keeps that
/// property: a checkout builds and tests with nothing but the .NET SDK. What a full framework
/// would add here — attribute discovery, parallelism, fixtures — is not worth the dependency for
/// a suite this shape, where every test is a plain call with a plain assertion.
/// </remarks>
public static class Test
{
    private sealed record TestCase(string Suite, string Name, Action Body);

    private static readonly List<TestCase> Cases = [];
    private static string _currentSuite = "general";

    /// <summary>Groups the tests registered after it.</summary>
    public static void Suite(string name) => _currentSuite = name;

    /// <summary>Registers one test.</summary>
    public static void Case(string name, Action body) => Cases.Add(new TestCase(_currentSuite, name, body));

    /// <summary>Runs every registered test and returns a process exit code.</summary>
    public static int RunAll(string? filter = null)
    {
        int passed = 0;
        var failures = new List<(string Name, Exception Error)>();
        var stopwatch = Stopwatch.StartNew();

        string? suite = null;
        foreach (TestCase test in Cases)
        {
            if (filter is not null &&
                !test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !test.Suite.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (suite != test.Suite)
            {
                suite = test.Suite;
                Console.WriteLine();
                Console.WriteLine($"  {suite}");
            }

            try
            {
                test.Body();
                passed++;
                Write("    pass  ", ConsoleColor.Green);
                Console.WriteLine(test.Name);
            }
            catch (Exception error)
            {
                failures.Add(($"{test.Suite} / {test.Name}", error));
                Write("    FAIL  ", ConsoleColor.Red);
                Console.WriteLine(test.Name);
            }
        }

        stopwatch.Stop();
        Console.WriteLine();

        foreach ((string name, Exception error) in failures)
        {
            Write("  FAILED  ", ConsoleColor.Red);
            Console.WriteLine(name);
            Console.WriteLine($"      {error.Message}");

            if (error.StackTrace is { } trace)
            {
                foreach (string line in trace.Split('\n').Take(4))
                    Console.WriteLine($"      {line.Trim()}");
            }
            Console.WriteLine();
        }

        Console.Write($"  {passed} passed");
        if (failures.Count > 0)
        {
            Console.Write(", ");
            Write($"{failures.Count} failed", ConsoleColor.Red);
        }
        Console.WriteLine($"  ({stopwatch.ElapsedMilliseconds} ms)");
        Console.WriteLine();

        return failures.Count == 0 ? 0 : 1;
    }

    private static void Write(string text, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    // ---------------------------------------------------------------------------------
    // Assertions
    // ---------------------------------------------------------------------------------

    public static void Equal<T>(T expected, T actual, string? because = null)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;

        throw new AssertionException(
            $"expected {Describe(expected)} but got {Describe(actual)}" +
            (because is null ? "" : $" ({because})"));
    }

    public static void NotEqual<T>(T unexpected, T actual, string? because = null)
    {
        if (!EqualityComparer<T>.Default.Equals(unexpected, actual)) return;

        throw new AssertionException(
            $"expected anything but {Describe(unexpected)}" + (because is null ? "" : $" ({because})"));
    }

    public static void True(bool condition, string because)
    {
        if (!condition) throw new AssertionException($"expected true: {because}");
    }

    public static void False(bool condition, string because)
    {
        if (condition) throw new AssertionException($"expected false: {because}");
    }

    public static void NotNull<T>(T? value, string because) where T : class
    {
        if (value is null) throw new AssertionException($"expected non-null: {because}");
    }

    public static void Null<T>(T? value, string because) where T : class
    {
        if (value is not null) throw new AssertionException($"expected null: {because}");
    }

    /// <summary>Asserts that an action throws, and returns the exception for further checks.</summary>
    public static TException Throws<TException>(Action body, string because) where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new AssertionException(
                $"expected {typeof(TException).Name} but got {other.GetType().Name}: {because}");
        }

        throw new AssertionException($"expected {typeof(TException).Name} but nothing was thrown: {because}");
    }

    private static string Describe<T>(T value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? "?",
    };
}

/// <summary>Raised when an assertion fails.</summary>
public sealed class AssertionException(string message) : Exception(message);
