using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();

// Path to the shared image file
var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "image.jpg");

// Ensure the image file exists
if (!File.Exists(imagePath))
{
    throw new FileNotFoundException("Image file not found. Place 'image.jpg' in the project root.");
}

// Serve pages dynamically
app.MapGet("/{pageName?}", (string? pageName, [FromQuery]int? links) =>
{
    pageName = pageName ?? "Home";
    // Generate three unique links for the page
    // Determine the number of links
    int numberOfLinks = links ?? 3; // Default to 3 if not specified

    // Generate unique links
    var generatedLinks = new List<Guid>();
    for (int i = 0; i < numberOfLinks; i++)
    {
        generatedLinks.Add(Guid.NewGuid());
    }

    // Create the page content
    var linkItems = string.Join("\n", generatedLinks.Select(link => $@"
        <li>
            <a href='/{link}?links={numberOfLinks}'>
                <img src='/images/{link}' alt='Image for Page {link}' style='width:100px;height:100px;'>
                Link to Page {link}
            </a>
        </li>"));

    string content = $@"
        <html>
        <head><title>Page {pageName}</title></head>
        <body>
            <h1>Welcome to Page {pageName}</h1>
            <ul>
                {linkItems}
            </ul>
        </body>
        </html>";

    return Results.Content(content, "text/html");
});

// Serve the same image file for any image link
app.MapGet("/images/{imageGuid}", (string imageGuid) =>
{
    var fileProvider = new FileExtensionContentTypeProvider();
    fileProvider.TryGetContentType(imagePath, out var contentType);

    contentType ??= "application/octet-stream";
    return Results.File(imagePath, contentType);
});

app.Run();
