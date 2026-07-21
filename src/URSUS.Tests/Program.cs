using System.Reflection;
using System.Runtime.Loader;

namespace URSUS.Tests;

internal static class Program
{
    private static int Main()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, $"{name.Name}.dll");
            return File.Exists(candidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        };

        var failures = new List<string>();
        var tests = typeof(Program).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<TestAttribute>() != null)
            .OrderBy(method => method.DeclaringType?.Name)
            .ThenBy(method => method.Name)
            .ToList();

        foreach (var test in tests)
        {
            string name = $"{test.DeclaringType?.Name}.{test.Name}";
            try
            {
                test.Invoke(null, null);
                Console.WriteLine($"PASS {name}");
            }
            catch (TargetInvocationException ex)
            {
                failures.Add($"FAIL {name}: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"FAIL {name}: {ex.Message}");
            }
        }

        foreach (string failure in failures)
            Console.Error.WriteLine(failure);

        Console.WriteLine($"Executed {tests.Count} tests: {tests.Count - failures.Count} passed, {failures.Count} failed.");
        return failures.Count == 0 ? 0 : 1;
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute : Attribute
{
}

internal static class AssertEx
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    public static void False(bool condition, string? message = null)
        => True(!condition, message ?? "Expected false.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
    }

    public static void Near(double expected, double actual, double tolerance = 1e-9)
    {
        if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected} ± {tolerance}, got {actual}.");
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, got {ex.GetType().Name}.", ex);
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
