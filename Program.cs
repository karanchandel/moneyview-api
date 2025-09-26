using Npgsql;
using Microsoft.AspNetCore.Http.Json;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// ✅ Bind to dynamic port (Render uses PORT env)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://*:{port}");

// ✅ Case-insensitive JSON property matching
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();
var logger = app.Logger;

// ✅ PostgreSQL connection string from env or config
string connectionString = builder.Configuration.GetConnectionString("PostgresConnection")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? throw new InvalidOperationException("PostgreSQL connection string not found!");

// ✅ Utility for handling NULL values in DB inserts
object ToDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

// ✅ Health check endpoint
app.MapGet("/", () => Results.Ok("✅ Bulk API is running..."));

// ✅ Bulk insert endpoint
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
        checkCmd.Parameters.AddWithValue("@phone", ToDbValue(user.Phone));
        checkCmd.Parameters.AddWithValue("@pan", ToDbValue(user.Pan));
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
        insertCmd.Parameters.AddWithValue("@name", ToDbValue(user.Name));
        insertCmd.Parameters.AddWithValue("@phone", ToDbValue(user.Phone));
        insertCmd.Parameters.AddWithValue("@email", ToDbValue(user.Email));
        insertCmd.Parameters.AddWithValue("@employment", ToDbValue(user.Employment));
        insertCmd.Parameters.AddWithValue("@pan", ToDbValue(user.Pan));
        insertCmd.Parameters.AddWithValue("@pincode", ToDbValue(user.Pincode));
        insertCmd.Parameters.AddWithValue("@income", ToDbValue(user.Income));
        insertCmd.Parameters.AddWithValue("@city", ToDbValue(user.City));
        insertCmd.Parameters.AddWithValue("@state", ToDbValue(user.State));
        insertCmd.Parameters.AddWithValue("@dob", ToDbValue(user.Dob));
        insertCmd.Parameters.AddWithValue("@gender", ToDbValue(user.Gender));
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
                createdDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
            });

            logger.LogInformation("✅ Inserted user: {@User}", user);
        }
        else
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = "Insert failed" });
            logger.LogWarning("⚠️ Insert failed for user: {Phone}", user.Phone);
        }
    }

    if (inserted.Count > 0 && skipped.Count > 0)
        return Results.Json(new { insertedCount = inserted.Count, skippedCount = skipped.Count, inserted, skipped }, statusCode: 207);
    if (inserted.Count == 0 && skipped.Count > 0)
        return Results.Json(new { skippedCount = skipped.Count, skipped }, statusCode: 409);

    return Results.Json(new { insertedCount = inserted.Count, inserted }, statusCode: 200);
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