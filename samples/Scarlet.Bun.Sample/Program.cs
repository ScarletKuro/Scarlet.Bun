using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Serve static files from wwwroot
app.UseStaticFiles();

// Simple endpoint to verify the app is running
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>Scarlet.Bun.Sample</title>
    <link rel=""stylesheet"" href=""/css/style.min.css"">
</head>
<body>
    <div class=""container"">
        <h1>Scarlet.Bun.Sample</h1>
        <p>This is a sample application demonstrating the Scarlet.Bun.MSBuild task.</p>
        <button class=""button"">Test Button</button>
    </div>
    <script src=""/js/bundle.min.js""></script>
</body>
</html>
", "text/html"));

app.Run();
