using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Configuration;
using WebApplication1.Services.Membership;
using WebApplication1.Services.Midtrans;
using WebApplication1.Services.Time;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<MidtransOptions>(builder.Configuration.GetSection(MidtransOptions.SectionName));
builder.Services.Configure<OrderChargesOptions>(builder.Configuration.GetSection(OrderChargesOptions.SectionName));
builder.Services.Configure<BusinessTimeOptions>(builder.Configuration.GetSection(BusinessTimeOptions.SectionName));
builder.Services.Configure<PublicSiteOptions>(builder.Configuration.GetSection(PublicSiteOptions.SectionName));
builder.Services.AddHttpClient<MidtransService>();
builder.Services.AddScoped<PendingMemberSignupService>();
builder.Services.AddSingleton<IBusinessTime, BusinessTimeService>();

builder.Services.AddDbContext<AppDbContext>(options=>
options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

var app = builder.Build();

// Apply pending migrations before seeding identity data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
await IdentitySeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    const string correlationHeader = "X-Correlation-ID";
    var correlationId = context.Request.Headers[correlationHeader].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
    }

    context.Response.Headers[correlationHeader] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    }))
    {
        await next();
    }
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTime.UtcNow
})).AllowAnonymous();

app.MapControllerRoute(
    name: "track-order",
    pattern: "TrackOrder/{orderNumber?}",
    defaults: new { controller = "Home", action = "TrackOrder" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
