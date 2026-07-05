using Xunit;

namespace F1.E2E.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class E2EFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> IsEnabled = new(() => E2eOptions.FromEnvironment().Enabled);

    public E2EFactAttribute()
    {
        if (IsEnabled.Value)
        {
            return;
        }

        Skip = "E2E_BASE_URL is not set or E2E_REQUIRED is not true.";
    }
}