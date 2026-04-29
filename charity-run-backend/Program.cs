using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();


var dataFile = Path.Combine(AppContext.BaseDirectory, "runners.json");
var store = new RunnerStore(dataFile);
await store.LoadAsync();
await store.EnsureSeedDataAsync();

var sessions = new ConcurrentDictionary<string, int>();

var charityInfo = new CharityInfo(
    CharityName: "Supply the Spark",
    Description: "A fictitious nonprofit raising money for artist and graphic designer supplies.",
    RaceDate: "2026-06-19",
    Location: "East Walnut Street",
    Distance: "5K",
    PublicAnnouncement: "Join us for a community 5K to support local students.");

var publicRaceInfo = new PublicRaceInfo(
    RaceDate: "2026-06-19",
    Location: "East Walnut Street",
    Distance: "5K",
    StartTime: "12:00 PM",
    RegistrationNote: "Online registration is open until race week. No payment is collected in this demo project.");

var privateRaceInfo = new PrivateRaceInfo(
    RaceDate: "2026-06-19",
    StartLocation: "Waterfront Park Entrance",
    FinishLocation: "Main Lawn Stage Area",
    RouteDescription: "The route begins at the park entrance, follows the waterfront trail, loops through the historic district, and returns to the main lawn.",
    RouteMapUrl: "/images/route-map-placeholder.png");

var schedule = new List<ScheduleItem>
{
    new("7:00 AM", "Check-in Opens", "Registered runners can check in and pick up race materials."),
    new("8:00 AM", "Warm-up Session", "Group stretching and race reminders."),
    new("8:30 AM", "Race Starts", "The 5K begins at the park entrance."),
    new("9:30 AM", "Refreshments", "Water, fruit, and snacks available near the finish area."),
    new("10:00 AM", "Closing Remarks", "Thank-you message and charity update from organizers.")
};

app.MapGet("/", () => Results.Ok(new
{
    message = "Charity Run API is running.",
    docs = "/api-doc",
    test = "/test"
}));

// PUBLIC ENDPOINTS
app.MapGet("/api/public/charity", () => Results.Ok(charityInfo));
app.MapGet("/api/public/race", () => Results.Ok(publicRaceInfo));
app.MapGet("/api/public/schedule", () => Results.Ok(schedule));

// AUTH ENDPOINTS
app.MapPost("/api/auth/register", async (RegisterRequest request) =>
{
    await store.LoadAsync(); // added for storing new user information

    var errors = ValidateRegistration(request, store.Runners);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { errors });
    }

    var isFirstRunner = store.Runners.Count == 0;

    var runner = new Runner
    {
        Id = store.NextId(),
        Name = request.Name.Trim(),
        Email = request.Email.Trim(),
        Age = request.Age,
        EmergencyPhone = request.EmergencyPhone.Trim(),
        PasswordHash = PasswordHelper.HashPassword(request.Password),
        TeamName = NormalizeTeamName(request.TeamName),
        IsAdmin = isFirstRunner,
        CreatedAt = DateTime.UtcNow
    };

    store.Runners.Add(runner);
    await store.SaveAsync();

    return Results.Ok(new
    {
        message = isFirstRunner
            ? "Registration successful. You are the first registered runner and have been made an admin."
            : "Registration successful.",
        runner = runner.ToSafeDto()
    });
});

app.MapPost("/api/auth/login", (LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "Email and password are required." });
    }

    var runner = store.Runners.FirstOrDefault(r =>
        r.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase));

    if (runner is null || !PasswordHelper.VerifyPassword(request.Password, runner.PasswordHash))
    {
        return Results.Json(new
        {
            error = "Invalid email or password."
        }, statusCode: 401);
    }

    var token = Guid.NewGuid().ToString("N");
    sessions[token] = runner.Id;

    return Results.Ok(new
    {
        message = "Login successful.",
        token,
        runner = runner.ToSafeDto()
    });
});

app.MapPost("/api/auth/logout", (HttpContext http) =>
{
    var token = GetToken(http);
    if (!string.IsNullOrWhiteSpace(token))
    {
        sessions.TryRemove(token, out _);
    }

    return Results.Ok(new { message = "Logged out." });
});

// RUNNER ENDPOINTS
app.MapGet("/api/runners/me", (HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    return auth is null ? Results.Unauthorized() : Results.Ok(auth.ToSafeDto());
});

app.MapPut("/api/runners/me", async (UpdateMyProfileRequest request, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();

    var errors = ValidateProfileUpdate(request, auth.Id, store.Runners);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    auth.Name = request.Name.Trim();
    auth.Age = request.Age;
    auth.EmergencyPhone = request.EmergencyPhone.Trim();
    auth.TeamName = NormalizeTeamName(request.TeamName);

    if (!string.IsNullOrWhiteSpace(request.Email))
        auth.Email = request.Email.Trim();

    if (!string.IsNullOrWhiteSpace(request.Password))
        auth.PasswordHash = PasswordHelper.HashPassword(request.Password);

    await store.SaveAsync();

    return Results.Ok(new
    {
        message = "Profile updated.",
        runner = auth.ToSafeDto()
    });
});

app.MapGet("/api/runners/me/teammates", (HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(auth.TeamName))
        return Results.Ok(Array.Empty<object>());

    var teammates = store.Runners
        .Where(r => r.Id != auth.Id && string.Equals(r.TeamName, auth.TeamName, StringComparison.OrdinalIgnoreCase))
        .Select(r => new { r.Name })
        .ToList();

    return Results.Ok(teammates);
});

// PRIVATE RACE INFO
app.MapGet("/api/private/race-info", (HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    return auth is null ? Results.Unauthorized() : Results.Ok(privateRaceInfo);
});

app.MapGet("/api/private/schedule", (HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    return auth is null ? Results.Unauthorized() : Results.Ok(schedule);
});

// ADMIN ENDPOINTS
app.MapGet("/api/admin/runners", (HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();
    if (!auth.IsAdmin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    return Results.Ok(store.Runners
        .OrderBy(r => r.Id)
        .Select(r => r.ToSafeDto()));
});

app.MapGet("/api/admin/runners/{id:int}", (int id, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();
    if (!auth.IsAdmin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var runner = store.Runners.FirstOrDefault(r => r.Id == id);
    return runner is null
        ? Results.NotFound(new { error = "Runner not found." })
        : Results.Ok(runner.ToSafeDto());
});

app.MapPut("/api/admin/runners/{id:int}", async (int id, AdminUpdateRunnerRequest request, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();
    if (!auth.IsAdmin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var runner = store.Runners.FirstOrDefault(r => r.Id == id);
    if (runner is null)
        return Results.NotFound(new { error = "Runner not found." });

    var errors = ValidateAdminUpdate(request, id, store.Runners);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    runner.Name = request.Name.Trim();
    runner.Email = request.Email.Trim();
    runner.Age = request.Age;
    runner.EmergencyPhone = request.EmergencyPhone.Trim();
    runner.TeamName = NormalizeTeamName(request.TeamName);

    if (!string.IsNullOrWhiteSpace(request.Password))
        runner.PasswordHash = PasswordHelper.HashPassword(request.Password);

    if (request.IsAdmin.HasValue)
        runner.IsAdmin = request.IsAdmin.Value;

    await store.SaveAsync();

    return Results.Ok(new
    {
        message = "Runner updated.",
        runner = runner.ToSafeDto()
    });
});

app.MapDelete("/api/admin/runners/{id:int}", async (int id, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();
    if (!auth.IsAdmin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var runner = store.Runners.FirstOrDefault(r => r.Id == id);
    if (runner is null)
        return Results.NotFound(new { error = "Runner not found." });

    store.Runners.Remove(runner);
    await store.SaveAsync();

    var expiredTokens = sessions.Where(kvp => kvp.Value == id).Select(kvp => kvp.Key).ToList();
    foreach (var token in expiredTokens)
        sessions.TryRemove(token, out _);

    return Results.Ok(new { message = "Runner deleted." });
});

app.MapPost("/api/admin/runners/{id:int}/make-admin", async (int id, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();
    if (!auth.IsAdmin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var runner = store.Runners.FirstOrDefault(r => r.Id == id);
    if (runner is null)
        return Results.NotFound(new { error = "Runner not found." });

    runner.IsAdmin = true;
    await store.SaveAsync();

    return Results.Ok(new
    {
        message = $"{runner.Name} is now an admin.",
        runner = runner.ToSafeDto()
    });
});

// Added to handle removal of admin users
app.MapPost("/api/admin/runners/{id:int}/remove-admin", async (int id, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

    if (!auth.IsAdmin)
        return Results.Json(new { error = "Forbidden" }, statusCode: 403);

    var runner = store.Runners.FirstOrDefault(r => r.Id == id);
    if (runner is null)
        return Results.NotFound(new { error = "Runner not found." });

    runner.IsAdmin = false;

    await store.SaveAsync();

    return Results.Ok(new
    {
        message = $"{runner.Name} is no longer an admin.",
        runner = runner.ToSafeDto()
    });
});

app.MapGet("/api/admin/email-preview/{id:int}", (int id, HttpContext http) =>
{
    var auth = GetAuthenticatedRunner(http, sessions, store.Runners);
    if (auth is null)
        return Results.Unauthorized();
    if (!auth.IsAdmin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var runner = store.Runners.FirstOrDefault(r => r.Id == id);
    if (runner is null)
        return Results.NotFound(new { error = "Runner not found." });

    var subject = "Reminder: Your 5K Run is on June 19";
    var body = """
Hello {runner.Name},

This is a reminder that the {charityInfo.CharityName} 5K takes place on {privateRaceInfo.RaceDate}.

Start location: {privateRaceInfo.StartLocation}
Finish location: {privateRaceInfo.FinishLocation}
Start time: 8:30 AM

Please arrive early for check-in and warm-up.

Thank you for supporting our cause.
""";

    return Results.Ok(new { subject, body });
});

// SIMPLE DOCS PAGE
app.MapGet("/api-doc", () => Results.Content("""
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <title>Charity Run API Docs</title>
  <style>
    body {{ font-family: Arial, sans-serif; max-width: 960px; margin: 40px auto; padding: 0 16px; line-height: 1.5; }}
    code {{ background: #f4f4f4; padding: 2px 6px; border-radius: 4px; }}
    pre {{ background: #f4f4f4; padding: 12px; border-radius: 8px; overflow-x: auto; }}
    h2 {{ margin-top: 32px; }}
  </style>
</head>
<body>
  <h1>Charity Run API Docs</h1>
  <p>This lightweight .NET 10 backend is designed for a UI programming course project.</p>

  <h2>Public APIs</h2>
  <ul>
    <li><code>GET /api/public/charity</code></li>
    <li><code>GET /api/public/race</code></li>
    <li><code>GET /api/public/schedule</code></li>
  </ul>

  <h2>Auth APIs</h2>
  <ul>
    <li><code>POST /api/auth/register</code></li>
    <li><code>POST /api/auth/login</code></li>
    <li><code>POST /api/auth/logout</code></li>
  </ul>

  <h2>Runner APIs</h2>
  <ul>
    <li><code>GET /api/runners/me</code></li>
    <li><code>PUT /api/runners/me</code></li>
    <li><code>GET /api/runners/me/teammates</code></li>
  </ul>

  <h2>Private Race APIs</h2>
  <ul>
    <li><code>GET /api/private/race-info</code></li>
    <li><code>GET /api/private/schedule</code></li>
  </ul>

  <h2>Admin APIs</h2>
  <ul>
    <li><code>GET /api/admin/runners</code></li>
    <li><code>GET /api/admin/runners/{'{'}id{'}'}</code></li>
    <li><code>PUT /api/admin/runners/{'{'}id{'}'}</code></li>
    <li><code>DELETE /api/admin/runners/{'{'}id{'}'}</code></li>
    <li><code>POST /api/admin/runners/{'{'}id{'}'}/make-admin</code></li>
    <li><code>GET /api/admin/email-preview/{'{'}id{'}'}</code></li>
  </ul>

  <h2>Auth Header</h2>
  <pre>Authorization: Bearer YOUR_TOKEN</pre>

  <h2>Default Accounts</h2>
  <pre>admin@example.com / Admin123!
runner@example.com / Runner123!</pre>

  <p>Interactive test page: <a href="/test">/test</a></p>
</body>
</html>
""", "text/html"));

// SIMPLE TEST PAGE
app.MapGet("/test", () => Results.Content("""
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <title>Charity Run API Test</title>
  <style>
    body { font-family: Arial, sans-serif; max-width: 900px; margin: 40px auto; padding: 0 16px; }
    button { margin-right: 8px; margin-bottom: 8px; }
    input { margin-right: 8px; margin-bottom: 8px; padding: 6px; }
    pre { background: #f4f4f4; padding: 12px; border-radius: 8px; overflow-x: auto; }
  </style>
</head>
<body>
  <h1>Charity Run API Test Page</h1>
  <p>Default admin login: admin@example.com / Admin123!</p>

  <div>
    <input id="email" value="admin@example.com" />
    <input id="password" type="password" value="Admin123!" />
    <button onclick="login()">Login</button>
    <button onclick="getMe()">Get Me</button>
    <button onclick="getTeammates()">Get Teammates</button>
    <button onclick="getRunners()">Get Admin Runner List</button>
  </div>

  <pre id="output"></pre>

  <script>
    let token = "";
    const output = document.getElementById("output");

    async function login() {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: document.getElementById('email').value,
          password: document.getElementById('password').value
        })
      });
      const data = await res.json();
      token = data.token || '';
      output.textContent = JSON.stringify(data, null, 2);
    }

    async function getMe() {
      const res = await fetch('/api/runners/me', {
        headers: { Authorization: `Bearer ${token}` }
      });
      output.textContent = JSON.stringify(await res.json(), null, 2);
    }

    async function getTeammates() {
      const res = await fetch('/api/runners/me/teammates', {
        headers: { Authorization: `Bearer ${token}` }
      });
      output.textContent = JSON.stringify(await res.json(), null, 2);
    }

    async function getRunners() {
      const res = await fetch('/api/admin/runners', {
        headers: { Authorization: `Bearer ${token}` }
      });
      output.textContent = JSON.stringify(await res.json(), null, 2);
    }
  </script>
</body>
</html>
""", "text/html"));

app.Run();

static List<string> ValidateRegistration(RegisterRequest request, List<Runner> existingRunners)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(request.Name))
        errors.Add("Name is required.");

    if (string.IsNullOrWhiteSpace(request.Email))
        errors.Add("Email is required.");
    else if (existingRunners.Any(r => r.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
        errors.Add("Email already exists.");

    if (request.Age < 1 || request.Age > 120)
        errors.Add("Age must be between 1 and 120.");

    if (string.IsNullOrWhiteSpace(request.EmergencyPhone))
        errors.Add("Emergency phone is required.");

    if (string.IsNullOrWhiteSpace(request.Password))
        errors.Add("Password is required.");

    if (!string.IsNullOrWhiteSpace(request.TeamName) && !Regex.IsMatch(request.TeamName.Trim(), "^[A-Za-z0-9]+$"))
        errors.Add("Team name may contain letters and numbers only.");

    return errors;
}

static List<string> ValidateProfileUpdate(UpdateMyProfileRequest request, int currentRunnerId, List<Runner> existingRunners)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(request.Name))
        errors.Add("Name is required.");

    if (string.IsNullOrWhiteSpace(request.Email))
        errors.Add("Email is required.");
    else if (existingRunners.Any(r => r.Id != currentRunnerId && r.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
        errors.Add("Email already exists.");

    if (request.Age < 1 || request.Age > 120)
        errors.Add("Age must be between 1 and 120.");

    if (string.IsNullOrWhiteSpace(request.EmergencyPhone))
        errors.Add("Emergency phone is required.");

    if (!string.IsNullOrWhiteSpace(request.TeamName) && !Regex.IsMatch(request.TeamName.Trim(), "^[A-Za-z0-9]+$"))
        errors.Add("Team name may contain letters and numbers only.");

    return errors;
}

static List<string> ValidateAdminUpdate(AdminUpdateRunnerRequest request, int currentRunnerId, List<Runner> existingRunners)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(request.Name))
        errors.Add("Name is required.");

    if (string.IsNullOrWhiteSpace(request.Email))
        errors.Add("Email is required.");
    else if (existingRunners.Any(r => r.Id != currentRunnerId && r.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
        errors.Add("Email already exists.");

    if (request.Age < 1 || request.Age > 120)
        errors.Add("Age must be between 1 and 120.");

    if (string.IsNullOrWhiteSpace(request.EmergencyPhone))
        errors.Add("Emergency phone is required.");

    if (!string.IsNullOrWhiteSpace(request.TeamName) && !Regex.IsMatch(request.TeamName.Trim(), "^[A-Za-z0-9]+$"))
        errors.Add("Team name may contain letters and numbers only.");

    return errors;
}

static string? NormalizeTeamName(string? teamName)
{
    if (string.IsNullOrWhiteSpace(teamName))
        return null;
    return teamName.Trim();
}

static string? GetToken(HttpContext http)
{
    var authHeader = http.Request.Headers.Authorization.ToString();
    if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return authHeader["Bearer ".Length..].Trim();
    }

    if (http.Request.Headers.TryGetValue("X-Auth-Token", out var tokenHeader))
    {
        return tokenHeader.ToString().Trim();
    }

    return null;
}

static Runner? GetAuthenticatedRunner(HttpContext http, ConcurrentDictionary<string, int> sessions, List<Runner> runners)
{
    var token = GetToken(http);
    if (string.IsNullOrWhiteSpace(token))
        return null;

    if (!sessions.TryGetValue(token, out var runnerId))
        return null;

    return runners.FirstOrDefault(r => r.Id == runnerId);
}

record RegisterRequest(string Name, string Email, int Age, string EmergencyPhone, string Password, string? TeamName);
record LoginRequest(string Email, string Password);
record UpdateMyProfileRequest(string Name, string Email, int Age, string EmergencyPhone, string? Password, string? TeamName);
record AdminUpdateRunnerRequest(string Name, string Email, int Age, string EmergencyPhone, string? Password, string? TeamName, bool? IsAdmin);
record CharityInfo(string CharityName, string Description, string RaceDate, string Location, string Distance, string PublicAnnouncement);
record PublicRaceInfo(string RaceDate, string Location, string Distance, string StartTime, string RegistrationNote);
record PrivateRaceInfo(string RaceDate, string StartLocation, string FinishLocation, string RouteDescription, string RouteMapUrl);
record ScheduleItem(string Time, string Event, string Description);

class Runner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public string EmergencyPhone { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? TeamName { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }

    public object ToSafeDto() => new
    {
        Id,
        Name,
        Email,
        Age,
        EmergencyPhone,
        TeamName,
        IsAdmin,
        CreatedAt
    };
}

class RunnerStore
{
    private readonly string _filePath;
    public List<Runner> Runners { get; private set; } = new();

    public RunnerStore(string filePath)
    {
        _filePath = filePath;
    }

    public int NextId() => Runners.Count == 0 ? 1 : Runners.Max(r => r.Id) + 1;

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            Runners = new List<Runner>();
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath);
        Runners = JsonSerializer.Deserialize<List<Runner>>(json) ?? new List<Runner>();
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Runners, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task EnsureSeedDataAsync()
    {
        if (Runners.Count > 0) return;

        Runners.Add(new Runner
        {
            Id = 1,
            Name = "Admin Runner",
            Email = "admin@example.com",
            Age = 30,
            EmergencyPhone = "555-111-2222",
            PasswordHash = PasswordHelper.HashPassword("Admin123!"),
            TeamName = "FastFeet",
            IsAdmin = true,
            CreatedAt = DateTime.UtcNow
        });

        Runners.Add(new Runner
        {
            Id = 2,
            Name = "Sample Runner",
            Email = "runner@example.com",
            Age = 22,
            EmergencyPhone = "555-333-4444",
            PasswordHash = PasswordHelper.HashPassword("Runner123!"),
            TeamName = "FastFeet",
            IsAdmin = false,
            CreatedAt = DateTime.UtcNow
        });

        await SaveAsync();
    }
}

static class PasswordHelper
{
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");