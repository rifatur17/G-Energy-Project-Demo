using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;
namespace GEnergy.IntegrationTests;

public sealed class CapturingEmailSender : IEmailSender
{
    public bool Fail;
    public ConcurrentQueue<MimeMessage> Messages = new();
    public Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("Synthetic SMTP failure; no real email was sent.");
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
public sealed class RejectWelcomeRow : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        foreach (var row in data.Context!.ChangeTracker.Entries<WelcomeEmail>().Where(e => e.State == EntityState.Added)) row.Entity.Attempts = 11;
        return ValueTask.FromResult(result);
    }
}
public sealed class WelcomeEmailTests
{
    internal const string Password = "Welcome-test-only-passphrase-123";
    internal static object Registration(string email) => new { fullName = "QA Welcome <Owner>", email, phone = "01700000000", password = Password, role = "CarOwner" };
    internal static Dictionary<string, string?> Settings => new()
    {
        ["Email:Enabled"] = "true", ["Email:WorkerEnabled"] = "false", ["Email:Host"] = "smtp.example.test",
        ["Email:FromAddress"] = "welcome@example.test", ["Email:LoginUrl"] = "http://localhost:5173/login"
    };
    [Fact]
    public async Task Signup_without_mail_configuration_saves_one_job_and_never_claims_delivery()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        var result = await client.PostAsJsonAsync("/api/auth/register", Registration("welcome@example.test"));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var user = (await result.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.Equal("NotConfigured", user.WelcomeEmailStatus);
        Assert.DoesNotContain(Password, await result.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", Registration("WELCOME@example.test"))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
        var job = await db.WelcomeEmails.SingleAsync(); Assert.Equal(user.Id, job.UserId); Assert.Null(job.SentAtUtc); Assert.Equal(0, job.Attempts);
        Assert.False(await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(default));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { user.UserCode, password = Password })).StatusCode);
    }
    [Fact]
    public void Template_contains_user_id_welcome_thanks_and_safe_link_but_no_password_or_hash()
    {
        var user = new User { UserCode = "GE-OWN-009999", FullName = "QA <script>alert(1)</script>", Email = "recipient@example.test", Role = "CarOwner", PasswordHash = "NEVER-IN-MAIL-HASH" };
        var options = new EmailOptions { FromAddress = "welcome@example.test", LoginUrl = "https://example.test/login" };
        var message = WelcomeEmailMessage.Create(new() { Id = 12, User = user }, options);
        Assert.Equal(user.Email, message.To.Mailboxes.Single().Address); Assert.Contains(user.UserCode, message.TextBody); Assert.Contains("Welcome to G Energy", message.TextBody); Assert.Contains("Thank you", message.HtmlBody);
        Assert.Contains(options.LoginUrl, message.TextBody); Assert.DoesNotContain("<script>", message.HtmlBody); Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.DoesNotContain(user.PasswordHash, message.ToString()); Assert.DoesNotContain(Password, message.ToString());
        Assert.Equal(message.MessageId, WelcomeEmailMessage.Create(new() { Id = 12, User = user }, options).MessageId);
    }
    [Fact]
    public void Enabled_delivery_requires_tls_valid_sender_and_safe_public_login_url()
    {
        var options = new EmailOptions { Enabled = true, Host = "smtp.example.test", FromAddress = "welcome@example.test" };
        Assert.True(options.IsValid(true)); Assert.False(options.IsValid(false));
        options.LoginUrl = "https://example.test/login"; Assert.True(options.IsValid(false));
        options.Security = "None"; Assert.False(options.IsValid(true)); options.Security = "StartTls";
        options.Username = "smtp-user"; Assert.False(options.IsValid(true)); options.Password = "local-test-key"; Assert.True(options.IsValid(true));
        options.LoginUrl = "javascript:alert(1)"; Assert.False(options.IsValid(true));
    }
    [Fact]
    public async Task Configured_signup_reports_queued_without_calling_smtp_in_request()
    {
        var sender = new CapturingEmailSender(); await using var source = new AuthFactory();
        await using var app = source.WithWebHostBuilder(b => { b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings)); b.ConfigureServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }); });
        using var client = app.CreateClient(); var response = await client.PostAsJsonAsync("/api/auth/register", Registration("queued@example.test"));
        Assert.Equal("Queued", (await response.Content.ReadFromJsonAsync<UserResponse>())!.WelcomeEmailStatus); Assert.Empty(sender.Messages);
    }
}

[Collection("SharedSqlServer")]
public sealed class WelcomeEmailSqlServerTests
{
    [SharedSqlServerFact]
    public async Task Sql_failed_delivery_keeps_account_and_job_then_restart_can_retry_once()
    {
        var sender = new CapturingEmailSender { Fail = true }; var clock = new ManualChargingClock();
        await using var source = new SharedSqlServerFactory();
        await using var app = source.WithWebHostBuilder(b => { b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(WelcomeEmailTests.Settings)); b.ConfigureServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); }); });
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", WelcomeEmailTests.Registration($"qa-welcome-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var user = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        long id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); id = (await db.WelcomeEmails.SingleAsync(e => e.UserId == user.Id)).Id;
            Assert.True(await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(default, id));
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); var job = await db.WelcomeEmails.SingleAsync(e => e.Id == id);
            Assert.Null(job.SentAtUtc); Assert.Equal(1, job.Attempts); Assert.Null(job.LeaseUntilUtc); Assert.True(job.NextAttemptAtUtc > clock.GetUtcNow().UtcDateTime);
            Assert.False(await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(default, id));
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { user.UserCode, password = WelcomeEmailTests.Password })).StatusCode);
        clock.Advance(31); sender.Fail = false;
        await using var restarted = source.WithWebHostBuilder(b => { b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(WelcomeEmailTests.Settings)); b.ConfigureServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); }); });
        using var restartedClient = restarted.CreateClient();
        using (var scope = restarted.Services.CreateScope()) Assert.True(await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(default, id));
        using (var scope = restarted.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(default, id));
            var job = await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().WelcomeEmails.SingleAsync(e => e.Id == id);
            Assert.NotNull(job.SentAtUtc); Assert.Equal(2, job.Attempts);
        }
        Assert.Single(sender.Messages); Assert.Contains(user.UserCode, sender.Messages.Single().TextBody);
    }
    [SharedSqlServerFact]
    public async Task Sql_concurrent_dispatchers_send_one_message_and_enforce_unique_user_job()
    {
        var sender = new CapturingEmailSender(); await using var source = new SharedSqlServerFactory();
        await using var app = source.WithWebHostBuilder(b => { b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(WelcomeEmailTests.Settings)); b.ConfigureServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }); });
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", WelcomeEmailTests.Registration($"qa-welcome-race-{Guid.NewGuid():N}@example.test")); var user = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        long id; using (var scope = app.Services.CreateScope()) id = (await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().WelcomeEmails.SingleAsync(e => e.UserId == user.Id)).Id;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(default, id); }));
        Assert.Single(sender.Messages);
        using var verifyScope = app.Services.CreateScope(); var db = verifyScope.ServiceProvider.GetRequiredService<EnergyDbContext>();
        var job = await db.WelcomeEmails.SingleAsync(e => e.Id == id); Assert.Equal(1, job.Attempts); Assert.NotNull(job.SentAtUtc);
        db.WelcomeEmails.Add(new() { UserId = user.Id }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [SharedSqlServerFact]
    public async Task Sql_rejected_outbox_insert_rolls_back_user_insert_in_same_save()
    {
        await using var source = new SharedSqlServerFactory();
        await using var app = source.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddDbContext<EnergyDbContext>(o => o.AddInterceptors(new RejectWelcomeRow()))));
        using var client = app.CreateClient(); var email = $"qa-welcome-rollback-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/auth/register", WelcomeEmailTests.Registration(email))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
        Assert.False(await db.WelcomeEmails.AnyAsync(e => e.User.NormalizedEmail == email.ToUpperInvariant()));
    }
}
