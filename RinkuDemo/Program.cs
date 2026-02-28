using RinkuDemo;

var builder = WebApplication.CreateBuilder(args);
Registry.Initialize(builder.Configuration);
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapModule<ArtistModule, Artist>();
app.MapModule<AlbumModule, Album>();
app.MapModule<TrackModule, Track>();
app.MapModule<MediaTypeModule, Reference>();
app.MapModule<EmployeeModule, Employee>();
app.MapModule<CustomerModule, Customer>();
app.MapModule<InvoiceModule, Invoice>();
app.MapModule<InvoiceLineModule, InvoiceLine>();

app.Run();