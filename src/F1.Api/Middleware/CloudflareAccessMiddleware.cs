using F1.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace F1.Api.Middleware
{
    public class CloudflareAccessMiddleware
    {
        private const string UnauthorizedResponseMessage = "Unauthorized.";
        private readonly RequestDelegate _next;

        private readonly IConfiguration _configuration;
        private readonly ICloudflareJwtValidator _jwtValidator;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILogger<CloudflareAccessMiddleware> _logger;
        private readonly string _adminGroupClaimType;
        private readonly HashSet<string> _adminGroups;
        private readonly HashSet<string> _adminEmails;
        private readonly bool _enableTestServiceTokenFallback;
        private readonly HashSet<string> _testServiceTokenSubjects;
        private readonly HashSet<string> _testServiceTokenAdminSubjects;
        private readonly string _testServiceTokenEmailDomain;

        public CloudflareAccessMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            ICloudflareJwtValidator jwtValidator,
            IHostEnvironment hostEnvironment,
            ILogger<CloudflareAccessMiddleware>? logger = null)
        {
            _next = next;
            _configuration = configuration;
            _jwtValidator = jwtValidator;
            _hostEnvironment = hostEnvironment;
            _logger = logger ?? NullLogger<CloudflareAccessMiddleware>.Instance;
            _adminGroupClaimType = _configuration["CloudflareAccess:AdminGroupClaimType"] ?? "groups";
            _adminGroups = LoadConfiguredValues(_configuration, "CloudflareAccess:AdminGroups");
            _adminEmails = LoadConfiguredValues(_configuration, "CloudflareAccess:AdminEmails");
            _enableTestServiceTokenFallback = _configuration.GetValue<bool>("CloudflareAccess:EnableTestServiceTokenFallback");
            _testServiceTokenSubjects = LoadConfiguredValues(_configuration, "CloudflareAccess:TestServiceTokenSubjectAllowlist");
            _testServiceTokenAdminSubjects = LoadConfiguredValues(_configuration, "CloudflareAccess:TestServiceTokenAdminSubjectAllowlist");
            _testServiceTokenEmailDomain = _configuration["CloudflareAccess:TestServiceTokenEmailDomain"] ?? "test.local";
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var devSettings = _configuration.GetSection("DevSettings");
            if (_hostEnvironment.IsDevelopment() && devSettings.Exists() && devSettings.GetValue<bool>("SimulateCloudflare"))
            {
                var mockEmail = devSettings.GetValue<string>("MockEmail");
                if (!string.IsNullOrEmpty(mockEmail))
                {
                    var mockGroups = LoadConfiguredValues(_configuration, "DevSettings:MockGroups");
                    context.User = BuildPrincipal(mockEmail, mockEmail.Split('@')[0], "dev-mock-user", mockGroups, _adminGroupClaimType, _adminGroups, _adminEmails);
                    _logger.LogInformation(
                        "CloudflareAuthEvent: EventName={EventName} ReasonCode={ReasonCode} Path={Path} Method={Method} RequestId={RequestId} Environment={Environment}",
                        "CloudflareAuthEvent", "simulate_cloudflare_used",
                        context.Request.Path.Value, 
                        SanitiseForLogging(context.Request.Method),
                        context.TraceIdentifier, _hostEnvironment.EnvironmentName);
                    await _next(context);
                    return;
                }
            }

            var hasJwtHeader = context.Request.Headers.ContainsKey("Cf-Access-Jwt-Assertion");
            var jwtAssertion = hasJwtHeader
                ? context.Request.Headers["Cf-Access-Jwt-Assertion"].FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(jwtAssertion))
            {
                // Never allow legacy plaintext header identity outside development.
                if (_hostEnvironment.IsDevelopment() && _configuration.GetValue<bool>("CloudflareAccess:AllowLegacyHeaderBypass"))
                {
                    var fallbackEmail = context.Request.Headers["Cf-Access-Authenticated-User-Email"].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(fallbackEmail))
                    {
                        context.User = BuildPrincipal(
                            fallbackEmail,
                            fallbackEmail.Split('@')[0],
                            context.Request.Headers["Cf-Access-Authenticated-User-Id"].FirstOrDefault(),
                            LoadHeaderValues(context.Request.Headers["Cf-Access-Mock-Groups"].FirstOrDefault()),
                            _adminGroupClaimType,
                            _adminGroups,
                            _adminEmails
                        );
                        _logger.LogWarning(
                            "CloudflareAuthEvent: EventName={EventName} ReasonCode={ReasonCode} Path={Path} Method={Method} StatusCode={StatusCode} RequestId={RequestId} TraceId={TraceId} Environment={Environment} HasJwtHeader={HasJwtHeader} KidPresent={KidPresent} RemoteIp={RemoteIp}",
                            "CloudflareAuthEvent", "legacy_bypass_used",
                            context.Request.Path.Value, 
                            SanitiseForLogging(context.Request.Method),
                            200,
                            context.TraceIdentifier, Activity.Current?.Id,
                            _hostEnvironment.EnvironmentName, hasJwtHeader, false,
                            context.Connection.RemoteIpAddress?.ToString());
                        await _next(context);
                        return;
                    }
                }

                var reasonCode = hasJwtHeader ? "malformed_jwt" : "missing_jwt_header";
                LogAuthFailure(context, reasonCode, hasJwtHeader: hasJwtHeader, kidPresent: false);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync(UnauthorizedResponseMessage);
                return;
            }

            var validation = await _jwtValidator.ValidateAsync(jwtAssertion, context.RequestAborted);
            if (!validation.IsValid || validation.Principal is null)
            {
                LogAuthFailure(context, validation.ReasonCode, hasJwtHeader: true, kidPresent: validation.KidPresent, exception: validation.Exception);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync(UnauthorizedResponseMessage);
                return;
            }

            var email = validation.Principal.FindFirst("email")?.Value
                ?? validation.Principal.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrWhiteSpace(email))
            {
                if (TryBuildTestServiceTokenPrincipal(validation.Principal, out var fallbackPrincipal, out var fallbackSubject, out var fallbackIsAdmin))
                {
                    context.User = fallbackPrincipal;
                    Exception? downstreamException = null;
                    try
                    {
                        await _next(context);
                    }
                    catch (Exception ex)
                    {
                        downstreamException = ex;
                        throw;
                    }
                    finally
                    {
                        var statusCode = downstreamException is null
                            ? context.Response.StatusCode
                            : StatusCodes.Status500InternalServerError;
                        _logger.LogWarning(
                            downstreamException,
                            "CloudflareAuthEvent: EventName={EventName} ReasonCode={ReasonCode} Path={Path} Method={Method} StatusCode={StatusCode} RequestId={RequestId} TraceId={TraceId} Environment={Environment} ServiceTokenSubject={ServiceTokenSubject} FallbackIdentityUsed={FallbackIdentityUsed} IsAdmin={IsAdmin}",
                            "CloudflareAuthEvent",
                            "service_token_email_fallback_used",
                            context.Request.Path.Value,
                            SanitiseForLogging(context.Request.Method),
                            statusCode,
                            context.TraceIdentifier,
                            Activity.Current?.Id,
                            _hostEnvironment.EnvironmentName,
                            fallbackSubject,
                            true,
                            fallbackIsAdmin);
                    }
                    return;
                }

                LogAuthFailure(context, "missing_email_claim", hasJwtHeader: true, kidPresent: true);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync(UnauthorizedResponseMessage);
                return;
            }

            var name = validation.Principal.FindFirst("name")?.Value
                ?? validation.Principal.FindFirst(ClaimTypes.Name)?.Value;
            var subject = validation.Principal.FindFirst("sub")?.Value
                ?? validation.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var groups = ExtractGroupValues(validation.Principal, _adminGroupClaimType);
            var incomingClaimTypes = validation.Principal.Claims
                .Select(claim => claim.Type)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            context.User = BuildPrincipal(email, name, subject, groups, _adminGroupClaimType, _adminGroups, _adminEmails);

            _logger.LogDebug(
                "Cloudflare auth resolved Subject={Subject}, IncomingClaimTypes={IncomingClaimTypes}, ExtractedGroups={ExtractedGroups}, ConfiguredAdminGroups={ConfiguredAdminGroups}, IsAdmin={IsAdmin}",
                subject ?? string.Empty,
                incomingClaimTypes,
                groups,
                _adminGroups.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                context.User.IsInRole("Admin"));

            await _next(context);
        }

        private void LogAuthFailure(
            HttpContext context,
            string reasonCode,
            bool hasJwtHeader,
            bool kidPresent,
            int statusCode = StatusCodes.Status401Unauthorized,
            Exception? exception = null)
        {
            _logger.LogWarning(
                exception,
                "CloudflareAuthFailure: eventName={eventName} reasonCode={reasonCode} Path={Path} Method={Method} StatusCode={StatusCode} RequestId={RequestId} TraceId={TraceId} Environment={Environment} HasJwtHeader={HasJwtHeader} KidPresent={KidPresent} RemoteIp={RemoteIp}",
                "auth_failure",
                reasonCode,
                context.Request.Path.Value,
                SanitiseForLogging(context.Request.Method),
                statusCode,
                context.TraceIdentifier,
                Activity.Current?.Id,
                _hostEnvironment.EnvironmentName,
                hasJwtHeader,
                kidPresent,
                context.Connection.RemoteIpAddress?.ToString());
        }

        private static ClaimsPrincipal BuildPrincipal(
            string email,
            string? name,
            string? userId,
            IEnumerable<string> groups,
            string adminGroupClaimType,
            IReadOnlySet<string> adminGroups,
            IReadOnlySet<string> adminEmails)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Name, !string.IsNullOrWhiteSpace(name) ? name : email.Split('@')[0])
            };

            if (!string.IsNullOrWhiteSpace(userId))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            }

            var resolvedGroups = groups
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Select(group => group.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in resolvedGroups)
            {
                claims.Add(new Claim(adminGroupClaimType, group));
            }

            var isAdminByGroup = resolvedGroups.Any(group => adminGroups.Contains(group));
            var isAdminByEmail = adminEmails.Contains(email.Trim());

            if (isAdminByGroup || isAdminByEmail)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cloudflare"));
        }

        private static HashSet<string> LoadConfiguredValues(IConfiguration configuration, string sectionPath)
        {
            var section = configuration.GetSection(sectionPath);
            var children = section.GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim());

            var scalar = section.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];

            return children
                .Concat(scalar)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private bool TryBuildTestServiceTokenPrincipal(
            ClaimsPrincipal validatedPrincipal,
            out ClaimsPrincipal fallbackPrincipal,
            out string subject,
            out bool isAdmin)
        {
            fallbackPrincipal = new ClaimsPrincipal();
            subject = string.Empty;
            isAdmin = false;

            if (!_hostEnvironment.IsEnvironment("Test") || !_enableTestServiceTokenFallback)
            {
                return false;
            }

            var tokenIdentifiers = GetServiceTokenIdentifiers(validatedPrincipal);
            if (_testServiceTokenSubjects.Count == 0 || tokenIdentifiers.Count == 0)
            {
                return false;
            }

            var matchedIdentifier = tokenIdentifiers
                .FirstOrDefault(identifier => _testServiceTokenSubjects.Contains(identifier));
            if (string.IsNullOrWhiteSpace(matchedIdentifier))
            {
                return false;
            }

            subject = matchedIdentifier;

            isAdmin = _testServiceTokenAdminSubjects.Contains(subject);

            var name = validatedPrincipal.FindFirst("name")?.Value
                ?? validatedPrincipal.FindFirst("common_name")?.Value
                ?? "Service Token";
            var email = BuildSyntheticServiceTokenEmail(subject, _testServiceTokenEmailDomain);

            fallbackPrincipal = BuildServiceTokenPrincipal(email, name, subject, isAdmin);
            return true;
        }

        private static IReadOnlyList<string> GetServiceTokenIdentifiers(ClaimsPrincipal principal)
        {
            return new[]
            {
                principal.FindFirst("sub")?.Value,
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                principal.FindFirst("common_name")?.Value
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        }

        private static ClaimsPrincipal BuildServiceTokenPrincipal(string email, string name, string subject, bool isAdmin)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Name, name),
                new(ClaimTypes.NameIdentifier, subject)
            };

            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "CloudflareServiceTokenFallback"));
        }

        private static string BuildSyntheticServiceTokenEmail(string subject, string domain)
        {
            var safeLocalPart = new string(subject
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
                .ToArray());

            if (string.IsNullOrWhiteSpace(safeLocalPart))
            {
                safeLocalPart = "service-token";
            }

            var safeDomain = string.IsNullOrWhiteSpace(domain) ? "test.local" : domain.Trim();
            return $"{safeLocalPart}@{safeDomain}";
        }

        private static IReadOnlyList<string> LoadHeaderValues(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return [];
            }

            return rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static IReadOnlyList<string> ExtractGroupValues(ClaimsPrincipal principal, string configuredClaimType)
        {
            var claimTypes = new[]
            {
                configuredClaimType,
                "groups",
                "group",
                ClaimTypes.GroupSid
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            var claims = new List<string>();
            foreach (var claimType in claimTypes)
            {
                claims.AddRange(ExtractClaimValues(principal, claimType));
            }

            return claims
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<string> ExtractClaimValues(ClaimsPrincipal principal, string claimType)
        {
            var claims = principal.FindAll(claimType)
                .SelectMany(claim => ExpandClaimValue(claim.Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return claims;
        }

        private static IEnumerable<string> ExpandClaimValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<string[]>(trimmed);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }
                catch (JsonException)
                {
                    // Intentionally ignore JSON parse failures and fall back to treating the claim
                    // value as a comma-separated string or single value below. This avoids breaking
                    // authorization when claims are not strictly JSON array-shaped.
                }
            }

            if (trimmed.Contains(',', StringComparison.Ordinal))
            {
                return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return [trimmed];
        }

                /// <summary>
        /// Returns a log-safe representation of a value by removing newline characters.
        /// This helps prevent log forging via user-controlled input.
        /// </summary>
        private static string SanitiseForLogging(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
        }
    }
}
