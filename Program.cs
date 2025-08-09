var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    // Make Chat the root page
    options.Conventions.AddPageRoute("/Chat", "");
});
builder.Services.AddSession();

// Register AI client using environment variables or defaults (LM Studio compatible)
// Env: LMSTUDIO_BASE_URL, LMSTUDIO_API_KEY, LMSTUDIO_MODEL
var baseUrl = Environment.GetEnvironmentVariable("FOUNDRYLOCAL_BASE_URL") ?? "http://127.0.0.1:5555/v1";
var apiKey = Environment.GetEnvironmentVariable("FOUNDRYLOCAL_API_KEY") ?? "none";
var model = Environment.GetEnvironmentVariable("FOUNDRYLOCAL_MODEL") ?? "gpt-oss-20b-cuda-gpu";

builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ =>
{
    var inner = HarmonyTest.Services.OpenAIChatClientFactory.Create(baseUrl, apiKey, model);
    return new HarmonyTest.Services.HarmonyChatClient(inner);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();
app.UseSession();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
