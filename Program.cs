using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting; // Lägga till för HostBuilder
using VideoTranscodingApp.Services; // Se till att detta matchar din namnrymd

var builder = WebApplication.CreateBuilder(args);

// Konfigurera Kestrel-serveralternativ
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // Timeout-inställningar för KeepAlive och RequestHeaders
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
    serverOptions.Limits.MaxRequestBodySize = 5368709120; // 5 GB
});

// Lägg till tjänster till containern
builder.Services.AddControllers();

// Konfigurera gränser för multipart-uppladdningar
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5368709120; // 5 GB
});

// Registrera VideoTranscodingService som en scoped-tjänst
builder.Services.AddScoped<VideoTranscodingService>();

var app = builder.Build();

// Aktivera routing
app.UseRouting();

// Aktivera CORS för att tillåta alla ursprung, metoder och rubriker
app.UseCors(builder => builder
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Servera statiska filer från wwwroot-mappen
app.UseStaticFiles(); // Lägg till denna rad

// Karta controller-ändpunkter
app.MapControllers();

// Om du vill att `index.html` ska vara startsidan när du navigerar till roten av servern
app.MapGet("/", async context =>
{
    await context.Response.WriteAsync("<html><body><h1>Redirecting to index.html...</h1><script>window.location.href = '/index.html';</script></body></html>");
});

app.MapGet("/subtitles/en.vtt", async context =>
{
    var subtitlePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "subtitles");
    var files = Directory.GetFiles(subtitlePath, "*.vtt")
        .Select(Path.GetFileName); // Hämta bara filnamnen
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(files);
});


// Kör applikationen
app.Run();
