using RinkuDemo;

var builder = WebApplication.CreateBuilder(args);
var c = builder.Configuration;
Registry.Initialize(c);
var dynaActions = c.LoadActions();
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapController<Artist>(c, "Artist");
app.MapController<Album>(c, "Album");
app.MapController<Track>(c, "Track");
app.MapController<Reference>(c, "Genre");
app.MapController<Reference>(c, "MediaType");
app.MapController<Employee>(c, "Employee");
app.MapController<Customer>(c, "Customer");
app.MapController<Invoice>(c, "Invoice");
app.MapController<InvoiceLine>(c, "InvoiceLine");
app.MapDynaApi(dynaActions);
app.Run();
