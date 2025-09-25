using Npgsql;
using Microsoft.AspNetCore.Http.Json;
using System.ComponentModel.DataAnnotations;
using DotNetEnv; // ✅ Add this

var builder = WebApplication.CreateBuilder(args);

// ✅ Load .env file before accessing env vars
Env.Load();

// Enable case-insensitive JSON property matching
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();
var logger = app.Logger;

// ✅ PostgreSQL connection string from appsettings.json or .env
string connectionString = builder.Configuration.GetConnectionString("PostgresConnection")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? throw new InvalidOperationException("PostgreSQL connection string not found!");

// Health check endpoint
app.MapGet("/", () => Results.Ok("✅ Bulk API is running..."));

// Bulk insert endpoint
app.MapPost("/cashKuber", async (HttpContext context, List<MoneyViewUser> users) =>
{
    if (!context.Request.Headers.TryGetValue("api-key", out var apiKey) || apiKey != "moneyview")
        return Results.Json(new { message = "Unauthorized: Invalid api-key header" }, statusCode: 401);

    using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var inserted = new List<object>();
    var skipped = new List<object>();

    foreach (var user in users)
    {
        if (string.IsNullOrWhiteSpace(user.PartnerId))
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = "Missing PartnerId" });
            continue;
        }

        if (string.IsNullOrWhiteSpace(user.Phone) && string.IsNullOrWhiteSpace(user.Pan))
        {
            skipped.Add(new { user.Name, reason = "Missing Phone and PAN" });
            continue;
        }

        string checkQuery = "SELECT COUNT(*) FROM moneyview WHERE phone = @phone OR pan = @pan";
        using var checkCmd = new NpgsqlCommand(checkQuery, conn);
        checkCmd.Parameters.AddWithValue("@phone", user.Phone ?? (object)DBNull.Value);
        checkCmd.Parameters.AddWithValue("@pan", user.Pan ?? (object)DBNull.Value);
        int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        if (exists > 0)
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = "Duplicate phone or PAN" });
            continue;
        }

        string insertQuery = @"
            INSERT INTO moneyview (name, phone, email, employment, pan, pincode, income, city, state, dob, gender, partner_id)
            VALUES (@name, @phone, @email, @employment, @pan, @pincode, @income, @city, @state, @dob, @gender, @partner_id)";
        using var insertCmd = new NpgsqlCommand(insertQuery, conn);
        insertCmd.Parameters.AddWithValue("@name", user.Name ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@phone", user.Phone ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@email", user.Email ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@employment", user.Employment ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@pan", user.Pan ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@pincode", user.Pincode ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@income", user.Income ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@city", user.City ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@state", user.State ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@dob", user.Dob ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@gender", user.Gender ?? (object)DBNull.Value);
        insertCmd.Parameters.AddWithValue("@partner_id", user.PartnerId);

        int rows = await insertCmd.ExecuteNonQueryAsync();
        if (rows > 0)
        {
            inserted.Add(new
            {
                user.Name,
                user.Phone,
                user.Pan,
                status = "Inserted",
                createdDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            });

            logger.LogInformation("✅ Inserted user: {@User}", new
            {
                user.Name,
                user.Phone,
                user.Email,
                user.Employment,
                user.Pan,
                user.Pincode,
                user.Income,
                user.City,
                user.State,
                user.Dob,
                user.Gender,
                user.PartnerId
            });
        }
        else
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = "Insert failed" });
            logger.LogWarning("⚠️ Insert failed for user: {Phone}", user.Phone);
        }
    }

    return Results.Json(new
    {
        insertedCount = inserted.Count,
        skippedCount = skipped.Count,
        inserted,
        skipped
    }, statusCode: inserted.Any() && skipped.Any() ? 207 : 200);
});

app.Run();

// ✅ Model
public class MoneyViewUser
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Employment { get; set; }
    public string? Pan { get; set; }
    public string? Pincode { get; set; }
    public string? Income { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Dob { get; set; }
    public string? Gender { get; set; }

    [Required]
    public string PartnerId { get; set; } = default!;
}